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
    /// Otázka 3: jak získat připojení aktivního okna.
    /// Sestavení SSMS nejsou na NuGetu a jejich API je nezdokumentované, proto se
    /// k <c>ScriptFactory</c> přistupuje reflexí. Prototyp tak nezávisí na verzi SSMS
    /// při sestavení a za běhu vypíše, co skutečně existuje.
    /// </summary>
    internal static class ConnectionProbe
    {
        private const string Area = "q3";
        private const string ScriptFactoryTypeName = "Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory";

        /// <summary>Údaje o připojení v paměti. Heslo se nikdy nezapisuje do logu ani na disk.</summary>
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
            Log.Write(Area, "=== Výpis připojení aktivního okna ===");
            try
            {
                LogCandidateTypes();

                var factoryType = FindLoadedType(ScriptFactoryTypeName);
                if (factoryType == null)
                {
                    Log.Write(Area, "Typ " + ScriptFactoryTypeName + " není v načtených sestaveních. Otevřete okno dotazu a zkuste znovu.");
                    return null;
                }

                Log.Write(Area, "ScriptFactory nalezen v " + factoryType.Assembly.FullName + " (" + SafeLocation(factoryType.Assembly) + ")");
                var factory = factoryType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (factory == null)
                {
                    Log.Write(Area, "ScriptFactory.Instance je null nebo neexistuje.");
                    return null;
                }

                var activeProperty = factory.GetType().GetProperty("CurrentlyActiveWndConnectionInfo");
                if (activeProperty == null)
                {
                    Log.Write(Area, "Vlastnost CurrentlyActiveWndConnectionInfo neexistuje. Veřejné vlastnosti: " +
                        string.Join(", ", factory.GetType().GetProperties().Select(p => p.Name)));
                    return null;
                }

                var active = activeProperty.GetValue(factory);
                if (active == null)
                {
                    Log.Write(Area, "CurrentlyActiveWndConnectionInfo je null: aktivní okno nemá připojení.");
                    return null;
                }

                ObjectDumper.Dump(Area, "CurrentlyActiveWndConnectionInfo", active, maxDepth: 3);

                var ui = active.GetType().GetProperty("UIConnectionInfo")?.GetValue(active);
                return ui == null ? null : ToSnapshot(ui);
            }
            catch (Exception ex)
            {
                Log.Error(Area, "Výpis připojení selhal", ex);
                return null;
            }
        }

        /// <summary>
        /// Otevře vlastní připojení jen pro čtení a spustí dotaz bez vedlejších účinků.
        /// Používá SqlClient, který už je v procesu SSMS načtený, aby prototyp nemusel
        /// přibalovat vlastní kopii. Běží na pozadí.
        /// </summary>
        public static async Task TestConnectionAsync(Snapshot snapshot, CancellationToken token)
        {
            await TaskScheduler.Default;
            var watch = Stopwatch.StartNew();
            try
            {
                var connectionType = FindLoadedType("Microsoft.Data.SqlClient.SqlConnection");
                Log.Write(Area, connectionType != null
                    ? "Používám Microsoft.Data.SqlClient z procesu SSMS: " + connectionType.Assembly.FullName
                    : "Microsoft.Data.SqlClient není načtený, používám System.Data.SqlClient (bez Entra Interactive).");
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

                // Šifrování přebíráme z nastavení okna v SSMS. Výchozí hodnoty SqlClient 6
                // (Encrypt=Mandatory) by u serveru s certifikátem podepsaným sám sebou selhaly.
                if (!string.IsNullOrEmpty(snapshot.Encrypt))
                {
                    builder["Encrypt"] = snapshot.Encrypt;
                }

                if (!string.IsNullOrEmpty(snapshot.TrustServerCertificate))
                {
                    builder["Trust Server Certificate"] = snapshot.TrustServerCertificate;
                }

                var mode = ChooseAuthentication(snapshot, connectionType, builder);
                Log.Write(Area, "Režim ověření pro test: " + mode + "; připojovací řetězec bez tajemství: " + Redact(builder));

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
                                    "Test připojení OK za {0} ms: server={1}, databáze={2}, login={3}",
                                    watch.ElapsedMilliseconds, reader.GetValue(0), reader.GetValue(1), reader.GetValue(2)));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(Area, "Test připojení selhal po " + watch.ElapsedMilliseconds + " ms", ex);
            }
        }

        private static string ChooseAuthentication(Snapshot s, Type connectionType, DbConnectionStringBuilder builder)
        {
            var auth = s.AuthenticationType ?? string.Empty;
            // Starší SSMS: 0 = Windows, 1 = SQL Server. Hodnoty pro Entra ID jsou neznámé, proto se logují.
            if (auth == "0" || auth.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                builder["Integrated Security"] = true;
                return "Windows";
            }

            if (auth == "1" || auth.Equals("SqlLogin", StringComparison.OrdinalIgnoreCase) || auth.IndexOf("SqlPassword", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                builder["User ID"] = s.UserName;
                builder["Password"] = s.Password ?? string.Empty;
                return "SQL Server (heslo z paměti SSMS dostupné: " + !string.IsNullOrEmpty(s.Password) + ")";
            }

            if (connectionType.Assembly.GetName().Name == "Microsoft.Data.SqlClient")
            {
                builder["Authentication"] = "Active Directory Interactive";
                if (!string.IsNullOrEmpty(s.UserName))
                {
                    builder["User ID"] = s.UserName;
                }

                return "Entra ID Interactive (může zobrazit přihlašovací okno; ověřujeme, zda se token sdílí s SSMS)";
            }

            return "neznámý typ ověření \"" + auth + "\", zkouším bez dalších parametrů";
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
                "Shrnutí: server={0}, databáze={1}, uživatel={2}, typOvěření={3}, heslo k dispozici={4}, encrypt={5}, trustServerCertificate={6}",
                snapshot.ServerName, snapshot.DatabaseName ?? "(nezadáno)", snapshot.UserName, snapshot.AuthenticationType,
                !string.IsNullOrEmpty(snapshot.Password), snapshot.Encrypt ?? "(nezadáno)", snapshot.TrustServerCertificate ?? "(nezadáno)"));
            return snapshot;
        }

        /// <summary>Pomůže najít náhradu, kdyby ScriptFactory v SSMS 22 neexistoval.</summary>
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

            Log.Write(Area, "Kandidátní typy v načtených sestaveních SSMS (" + names.Count + "):");
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
                    // Některá sestavení nejde prohledat, přeskočíme je.
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
