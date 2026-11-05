using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.History;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Connection of the active query window. SSMS assemblies are not on NuGet and their API is
    /// undocumented, so ScriptFactory is accessed through reflection (verified by the prototype in SSMS 22.11).
    /// SSMS types never leave this class.
    /// </summary>
    internal static class SsmsConnection
    {
        private const string ScriptFactoryTypeName = "Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory";
        private static readonly ConcurrentDictionary<string, bool> ReportedAuthentication = new ConcurrentDictionary<string, bool>();
        private static Type scriptFactoryType;

        /// <summary>
        /// The type of the script factory. It lives in SQLEditors.dll, which SSMS loads only when the first query window or connection
        /// needs it, so on a cold start (when the tabs are restored) it is loaded explicitly.
        /// </summary>
        private static Type ScriptFactoryType()
        {
            if (scriptFactoryType != null)
            {
                return scriptFactoryType;
            }

            var type = AssemblyTypes.FindLoaded(ScriptFactoryTypeName);
            if (type == null)
            {
                try
                {
                    type = Assembly.Load("SQLEditors")?.GetType(ScriptFactoryTypeName, throwOnError: false);
                }
                catch (Exception ex)
                {
                    Log.Write("Loading SQLEditors by name failed: " + ex.Message);
                }
            }

            if (type == null)
            {
                try
                {
                    var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extensions", "Application", "SQLEditors.dll");
                    type = System.IO.File.Exists(path) ? Assembly.LoadFrom(path).GetType(ScriptFactoryTypeName, throwOnError: false) : null;
                }
                catch (Exception ex)
                {
                    Log.Write("Loading SQLEditors from its folder failed: " + ex.Message);
                }
            }

            if (type == null)
            {
                Log.Write("The script factory type was not found (SQLEditors is not loaded and could not be loaded).");
            }

            scriptFactoryType = type;
            return type;
        }

        /// <summary>Connection data of the active window, or null if the window is not connected.</summary>
        public static ConnectionSettings GetActive()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var factoryType = ScriptFactoryType();
            var factory = factoryType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var active = factory?.GetType().GetProperty("CurrentlyActiveWndConnectionInfo")?.GetValue(factory);
            var info = active?.GetType().GetProperty("UIConnectionInfo")?.GetValue(active);
            if (info == null)
            {
                return null;
            }

            var type = info.GetType();
            string Read(string name) => type.GetProperty(name)?.GetValue(info)?.ToString();

            var server = Read("ServerName");
            if (string.IsNullOrEmpty(server))
            {
                return null;
            }

            // AuthenticationType values verified in SSMS 22.11: 1 = SQL Server, 0 = Windows (LocalDB window loads without a password).
            var rawAuthentication = Read("AuthenticationType");
            AuthenticationKind authentication;
            switch (rawAuthentication)
            {
                case "0":
                    authentication = AuthenticationKind.Windows;
                    break;
                case "1":
                    authentication = AuthenticationKind.SqlPassword;
                    break;
                default:
                    authentication = AuthenticationKind.Unsupported;
                    if (ReportedAuthentication.TryAdd(rawAuthentication ?? "(null)", true))
                    {
                        Log.Write("Unsupported authentication type AuthenticationType=" + rawAuthentication + "; metadata will not be loaded.");
                    }

                    break;
            }

            var options = type.GetProperty("AdvancedOptions")?.GetValue(info) as NameValueCollection;
            return new ConnectionSettings(
                server,
                options?["DATABASE"],
                authentication,
                Read("UserName"),
                authentication == AuthenticationKind.SqlPassword ? Read("Password") : null,
                options?["ENCRYPT_CONNECTION"],
                options?["TRUST_SERVER_CERTIFICATE"]);
        }

        /// <summary>
        /// Opens a new query window connected like the active one but to <paramref name="database"/> and puts <paramref name="text"/> into it
        /// (verified in SSMS 22.11, see docs/architecture.md). Returns false when there is no connected window to copy from.
        /// </summary>
        public static bool OpenNewWindow(string database, string text, string server = null, string login = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var factoryType = ScriptFactoryType();
            var factory = factoryType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var active = factory?.GetType().GetProperty("CurrentlyActiveWndConnectionInfo")?.GetValue(factory);
            var info = active?.GetType().GetProperty("UIConnectionInfo")?.GetValue(active);
            if (info == null)
            {
                return false;
            }

            var infoType = info.GetType();
            var clone = Activator.CreateInstance(infoType);
            foreach (var property in infoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 && property.Name != "AdvancedOptions")
                {
                    property.SetValue(clone, property.GetValue(info));
                }
            }

            var options = (NameValueCollection)infoType.GetProperty("AdvancedOptions").GetValue(info);
            var cloneOptions = (NameValueCollection)infoType.GetProperty("AdvancedOptions").GetValue(clone);
            foreach (var key in options.AllKeys)
            {
                cloneOptions[key] = options[key];
            }

            if (!string.IsNullOrEmpty(database))
            {
                cloneOptions["DATABASE"] = database;
            }

            // Another server than the window the copy is made from: Windows authentication, or the SQL login (the password is never stored;
            // SSMS asks for it if it has to).
            if (!string.IsNullOrEmpty(server) && !string.Equals(server, Convert.ToString(infoType.GetProperty("ServerName")?.GetValue(info)), StringComparison.OrdinalIgnoreCase))
            {
                SetProperty(clone, "ServerName", server);
                SetProperty(clone, "AuthenticationType", login == null ? 0 : 1);
                SetProperty(clone, "UserName", login);
                SetProperty(clone, "Password", null);
            }

            var create = factory.GetType().GetMethods().FirstOrDefault(m =>
                m.Name == "CreateNewBlankScript" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType == infoType);
            if (create == null)
            {
                return false;
            }

            var scriptType = Enum.Parse(create.GetParameters()[0].ParameterType, "Sql");
            create.Invoke(factory, new[] { scriptType, clone, null });
            var view = ActiveEditor.GetView();
            view?.TextBuffer.Insert(0, text ?? string.Empty);
            return view != null;
        }

        /// <summary>
        /// Opens a window for a saved tab: connected with Windows authentication when the tab had that, otherwise unconnected with a comment
        /// that names the server and database it had (a password is never stored). Returns false when SSMS refused.
        /// </summary>
        public static bool OpenSavedTab(SavedTab tab)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var factoryType = ScriptFactoryType();
            var factory = factoryType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var create = factory?.GetType().GetMethods().FirstOrDefault(m =>
                m.Name == "CreateNewBlankScript" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.Name == "UIConnectionInfo");
            if (create == null)
            {
                Log.Write("Restoring a tab: " + (factoryType == null ? "the script factory type was not found" : factory == null ? "ScriptFactory.Instance is null" : "CreateNewBlankScript was not found among ") + string.Join("; ", (factory?.GetType().GetMethods() ?? new MethodInfo[0]).Where(m => m.Name == "CreateNewBlankScript").Select(m => m.ToString())));
                return false;
            }

            var text = tab.Text ?? string.Empty;
            object info = null;
            if (tab.IsConnected && string.IsNullOrEmpty(tab.Login))
            {
                var infoType = create.GetParameters()[1].ParameterType;
                info = Activator.CreateInstance(infoType);
                SetProperty(info, "ServerName", tab.Server);
                SetProperty(info, "AuthenticationType", 0);
                var options = (NameValueCollection)infoType.GetProperty("AdvancedOptions").GetValue(info);
                options["DATABASE"] = tab.Database ?? string.Empty;
                if (!string.IsNullOrEmpty(tab.Encrypt))
                {
                    options["ENCRYPT_CONNECTION"] = tab.Encrypt;
                }

                if (!string.IsNullOrEmpty(tab.TrustServerCertificate))
                {
                    options["TRUST_SERVER_CERTIFICATE"] = tab.TrustServerCertificate;
                }
            }
            else if (tab.IsConnected && !text.StartsWith("-- SQL Prompter: this tab was connected", StringComparison.Ordinal))
            {
                text = "-- SQL Prompter: this tab was connected to " + tab.Server + (string.IsNullOrEmpty(tab.Database) ? string.Empty : ", database " + tab.Database) +
                    " as " + tab.Login + ". Connect again to continue.\r\n" + text;
            }

            var scriptType = Enum.Parse(create.GetParameters()[0].ParameterType, "Sql");
            create.Invoke(factory, new[] { scriptType, info, null });
            if (info == null)
            {
                // With no connection given SSMS copies the one of the active window, so the new window is disconnected explicitly.
                try
                {
                    (Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ExecuteCommand("Query.Disconnect");
                }
                catch (Exception ex)
                {
                    Log.Error("Disconnecting the restored tab failed", ex);
                }
            }

            var view = ActiveEditor.GetView();
            if (view == null)
            {
                Log.Write("Restoring a tab: the new window has no editor view.");
                return false;
            }

            view.TextBuffer.Insert(0, text);
            return view != null;
        }

        private static void SetProperty(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            var type = property.PropertyType;
            property.SetValue(target, type.IsEnum && value is int number ? Enum.ToObject(type, number) : value);
        }
    }

    /// <summary>
    /// Creates Microsoft.Data.SqlClient.SqlConnection from the copy SSMS has loaded.
    /// The extension does not ship its own SqlClient so that the process never has two versions.
    /// </summary>
    internal static class SqlClientConnections
    {
        private const string ConnectionTypeName = "Microsoft.Data.SqlClient.SqlConnection";
        private const string AssemblyQualifiedName = ConnectionTypeName + ", Microsoft.Data.SqlClient, Version=6.0.0.0, Culture=neutral, PublicKeyToken=23ec7fc2d6eaa4a5";
        private static Type connectionType;

        public static DbConnection Create(string connectionString)
        {
            var type = connectionType ?? (connectionType =
                AssemblyTypes.FindLoaded(ConnectionTypeName) ?? Type.GetType(AssemblyQualifiedName, throwOnError: true));
            var connection = (DbConnection)Activator.CreateInstance(type);
            connection.ConnectionString = connectionString;
            return connection;
        }
    }

    internal static class AssemblyTypes
    {
        public static Type FindLoaded(string fullName)
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
                    // Some assemblies cannot be searched.
                }
            }

            return null;
        }
    }
}
