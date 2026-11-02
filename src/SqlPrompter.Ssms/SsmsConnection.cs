using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Data.Common;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Připojení aktivního okna dotazu. Sestavení SSMS nejsou na NuGetu a jejich API je
    /// nezdokumentované, proto se k ScriptFactory přistupuje reflexí (ověřeno prototypem v SSMS 22.11).
    /// Typy SSMS za hranici této třídy neodcházejí.
    /// </summary>
    internal static class SsmsConnection
    {
        private const string ScriptFactoryTypeName = "Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory";
        private static readonly ConcurrentDictionary<string, bool> ReportedAuthentication = new ConcurrentDictionary<string, bool>();
        private static Type scriptFactoryType;

        /// <summary>Údaje o připojení aktivního okna, nebo null, pokud okno není připojené.</summary>
        public static ConnectionSettings GetActive()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var factoryType = scriptFactoryType ?? (scriptFactoryType = AssemblyTypes.FindLoaded(ScriptFactoryTypeName));
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

            // Hodnoty AuthenticationType ověřené v SSMS 22.11: 1 = SQL Server. 0 = Windows podle starších verzí.
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
                        Log.Write("Nepodporovaný typ ověření AuthenticationType=" + rawAuthentication + ", metadata se nenačtou.");
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
    }

    /// <summary>
    /// Vytváří Microsoft.Data.SqlClient.SqlConnection z kopie, kterou má SSMS načtenou.
    /// Rozšíření si vlastní SqlClient nepřibaluje, aby v procesu nebyly dvě verze.
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
                    // Některá sestavení nejde prohledat.
                }
            }

            return null;
        }
    }
}
