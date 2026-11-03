using System;
using System.Collections.Specialized;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace SqlPrompter.Prototype
{
    /// <summary>
    /// Question 3: how to get the connection of the active window.
    /// SSMS assemblies are not on NuGet and their API is undocumented, so
    /// <c>ScriptFactory</c> is accessed through reflection. The prototype therefore does not depend on the SSMS version
    /// at build time and logs at runtime what really exists.
    /// </summary>
    internal static class ConnectionProbe
    {
        private const string Area = "q3";
        private const string ScriptFactoryTypeName = "Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory";

        /// <summary>Connection data in memory. The password is never written to the log or to disk.</summary>
        internal sealed class Snapshot
        {
            public string ServerName;
            public string DatabaseName;
            public string UserName;
            public string Password;
            public string AuthenticationType;
            public string Encrypt;
            public string TrustServerCertificate;
        }

        public static Snapshot DumpActiveConnection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Log.Write(Area, "=== Active window connection dump ===");
            try
            {
                LogCandidateTypes();

                var factoryType = FindLoadedType(ScriptFactoryTypeName);
                if (factoryType == null)
                {
                    Log.Write(Area, "Typ " + ScriptFactoryTypeName + " is not in the loaded assemblies. Open a query window and try again.");
                    return null;
                }

                Log.Write(Area, "ScriptFactory nalezen v " + factoryType.Assembly.FullName + " (" + SafeLocation(factoryType.Assembly) + ")");
                var factory = factoryType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (factory == null)
                {
                    Log.Write(Area, "ScriptFactory.Instance is null or does not exist.");
                    return null;
                }

                var activeProperty = factory.GetType().GetProperty("CurrentlyActiveWndConnectionInfo");
                if (activeProperty == null)
                {
                    Log.Write(Area, "Property CurrentlyActiveWndConnectionInfo does not exist. Public properties: " +
                        string.Join(", ", factory.GetType().GetProperties().Select(p => p.Name)));
                    return null;
                }

                var active = activeProperty.GetValue(factory);
                if (active == null)
                {
                    Log.Write(Area, "CurrentlyActiveWndConnectionInfo is null: the active window has no connection.");
                    return null;
                }

                ObjectDumper.Dump(Area, "CurrentlyActiveWndConnectionInfo", active, maxDepth: 3);

                var ui = active.GetType().GetProperty("UIConnectionInfo")?.GetValue(active);
                return ui == null ? null : ToSnapshot(ui);
            }
            catch (Exception ex)
            {
                Log.Error(Area, "Connection dump failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Opens our own read-only connection and runs a query without side effects.
        /// Uses the SqlClient already loaded in the SSMS process so that the prototype does not have to
        /// ship its own copy. Runs in the background.
        /// </summary>
        public static async Task TestConnectionAsync(Snapshot snapshot, CancellationToken token)
        {
            await TaskScheduler.Default;
            var watch = Stopwatch.StartNew();
            try
            {
                var connectionType = FindLoadedType("Microsoft.Data.SqlClient.SqlConnection");
                Log.Write(Area, connectionType != null
                    ? "Using Microsoft.Data.SqlClient from the SSMS process: " + connectionType.Assembly.FullName
                    : "Microsoft.Data.SqlClient is not loaded, using System.Data.SqlClient (without Entra Interactive).");
                connectionType = connectionType ?? typeof(System.Data.SqlClient.SqlConnection);

                var builder = new DbConnectionStringBuilder
                {
                    ["Data Source"] = snapshot.ServerName,
                    ["Application Name"] = "SQL Prompter prototype",
                    ["Connect Timeout"] = 15,
                };
                if (!string.IsNullOrEmpty(snapshot.DatabaseName))
                {
                    builder["Initial Catalog"] = snapshot.DatabaseName;
                }

                // Encryption is taken over from the window's settings in SSMS. The SqlClient 6 defaults
                // (Encrypt=Mandatory) would fail against a server with a self-signed certificate.
                if (!string.IsNullOrEmpty(snapshot.Encrypt))
                {
                    builder["Encrypt"] = snapshot.Encrypt;
                }

                if (!string.IsNullOrEmpty(snapshot.TrustServerCertificate))
                {
                    builder["Trust Server Certificate"] = snapshot.TrustServerCertificate;
                }

                var mode = ChooseAuthentication(snapshot, connectionType, builder);
                Log.Write(Area, "Authentication mode for the test: " + mode + "; connection string without secrets: " + Redact(builder));

                using (var connection = (DbConnection)Activator.CreateInstance(connectionType))
                {
                    connection.ConnectionString = builder.ConnectionString;
                    await connection.OpenAsync(token);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT @@SERVERNAME, DB_NAME(), SUSER_SNAME()";
                        using (var reader = await command.ExecuteReaderAsync(token))
                        {
                            if (await reader.ReadAsync(token))
                            {
                                Log.Write(Area, string.Format(
                                    "Connection test OK in {0} ms: server={1}, database={2}, login={3}",
                                    watch.ElapsedMilliseconds, reader.GetValue(0), reader.GetValue(1), reader.GetValue(2)));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(Area, "Connection test failed after " + watch.ElapsedMilliseconds + " ms", ex);
            }
        }

        private static string ChooseAuthentication(Snapshot s, Type connectionType, DbConnectionStringBuilder builder)
        {
            var auth = s.AuthenticationType ?? string.Empty;
            // Older SSMS: 0 = Windows, 1 = SQL Server. Values for Entra ID are unknown, so they are logged.
            if (auth == "0" || auth.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                builder["Integrated Security"] = true;
                return "Windows";
            }

            if (auth == "1" || auth.Equals("SqlLogin", StringComparison.OrdinalIgnoreCase) || auth.IndexOf("SqlPassword", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                builder["User ID"] = s.UserName;
                builder["Password"] = s.Password ?? string.Empty;
                return "SQL Server (password from SSMS memory available: " + !string.IsNullOrEmpty(s.Password) + ")";
            }

            if (connectionType.Assembly.GetName().Name == "Microsoft.Data.SqlClient")
            {
                builder["Authentication"] = "Active Directory Interactive";
                if (!string.IsNullOrEmpty(s.UserName))
                {
                    builder["User ID"] = s.UserName;
                }

                return "Entra ID Interactive (may show a sign-in window; checks whether the token is shared with SSMS)";
            }

            return "unknown authentication type \"" + auth + "\", trying without further parameters";
        }

        private static Snapshot ToSnapshot(object ui)
        {
            var type = ui.GetType();
            string Read(string name) => type.GetProperty(name)?.GetValue(ui)?.ToString();

            var snapshot = new Snapshot
            {
                ServerName = Read("ServerName"),
                UserName = Read("UserName"),
                Password = Read("Password"),
                AuthenticationType = Read("AuthenticationType"),
            };

            if (type.GetProperty("AdvancedOptions")?.GetValue(ui) is NameValueCollection options)
            {
                snapshot.DatabaseName = options["DATABASE"];
                snapshot.Encrypt = options["ENCRYPT_CONNECTION"];
                snapshot.TrustServerCertificate = options["TRUST_SERVER_CERTIFICATE"];
            }

            Log.Write(Area, string.Format(
                "Summary: server={0}, database={1}, user={2}, authenticationType={3}, password available={4}, encrypt={5}, trustServerCertificate={6}",
                snapshot.ServerName, snapshot.DatabaseName ?? "(not set)", snapshot.UserName, snapshot.AuthenticationType,
                !string.IsNullOrEmpty(snapshot.Password), snapshot.Encrypt ?? "(not set)", snapshot.TrustServerCertificate ?? "(not set)"));
            return snapshot;
        }

        /// <summary>Helps to find a replacement if ScriptFactory did not exist in SSMS 22.</summary>
        private static void LogCandidateTypes()
        {
            var names = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Where(a =>
                {
                    var n = a.GetName().Name;
                    return n.StartsWith("Microsoft.SqlServer", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("SqlWorkbench", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("SQLEditors", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase);
                })
                .SelectMany(a => SafeTypes(a))
                .Where(t => t.Name.IndexOf("ConnectionInfo", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name == "ScriptFactory"
                    || t.Name == "ServiceCache")
                .Select(t => t.FullName + " [" + t.Assembly.GetName().Name + " " + t.Assembly.GetName().Version + "]")
                .Distinct()
                .OrderBy(n => n)
                .Take(200)
                .ToList();

            Log.Write(Area, "Candidate types in the loaded SSMS assemblies (" + names.Count + "):");
            foreach (var name in names)
            {
                Log.Write(Area, "  " + name);
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                try
                {
                    var type = assembly.GetType(fullName, throwOnError: false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // Some assemblies cannot be searched; skip them.
                }
            }

            return null;
        }

        private static Type[] SafeTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).ToArray();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static string SafeLocation(Assembly assembly)
        {
            try
            {
                return assembly.Location;
            }
            catch
            {
                return "?";
            }
        }

        private static string Redact(DbConnectionStringBuilder builder)
        {
            var copy = new DbConnectionStringBuilder { ConnectionString = builder.ConnectionString };
            foreach (var key in copy.Keys.Cast<string>().ToList())
            {
                if (ObjectDumper.IsSecretName(key))
                {
                    copy[key] = "***";
                }
            }

            return copy.ConnectionString;
        }
    }
}
