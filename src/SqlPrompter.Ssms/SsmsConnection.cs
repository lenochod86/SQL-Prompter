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

        /// <summary>The script factory type for the compatibility check (no log line when it is missing; the check logs).</summary>
        internal static Type ScriptFactoryTypeOrNull() => ScriptFactoryType();

        /// <summary>Server and database of a window's <c>UIConnectionInfo</c> (the object of <c>SqlScriptEditorControl.Connection</c>); false when it has no server.</summary>
        public static bool TryReadServerAndDatabase(object info, out string server, out string database)
        {
            server = null;
            database = null;
            if (info == null)
            {
                return false;
            }

            var type = info.GetType();
            server = type.GetProperty("ServerName")?.GetValue(info)?.ToString();
            database = (type.GetProperty("AdvancedOptions")?.GetValue(info) as NameValueCollection)?["DATABASE"];
            return !string.IsNullOrEmpty(server);
        }

        /// <summary>
        /// The SQL logins SSMS has a live connection for in this run (their data, with the password kept in memory only, as SSMS keeps it in its
        /// own connection info; never written to a file or the log). A restored tab with the same server and login is connected with it (#322).
        /// </summary>
        private static readonly ConcurrentDictionary<string, ConnectionSettings> LiveSqlLogins = new ConcurrentDictionary<string, ConnectionSettings>();

        /// <summary>Server and login that the user cancelled the connect dialog for in this run: the other tabs of that login are not asked again.</summary>
        private static readonly ConcurrentDictionary<string, bool> DeclinedSqlLogins = new ConcurrentDictionary<string, bool>();

        /// <summary>The encryption options of the last connection seen to each server (any login): a tab that is opened without its own options (a history entry) gets them.</summary>
        private static readonly ConcurrentDictionary<string, ConnectionSettings> LastSeenByServer = new ConcurrentDictionary<string, ConnectionSettings>();

        private static string LoginKey(string server, string login) => ((server ?? string.Empty) + "|" + (login ?? string.Empty)).ToUpperInvariant();

        /// <summary>Connection data of the active window, or null if the window is not connected.</summary>
        public static ConnectionSettings GetActive()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!SsmsCompatibility.Has(SqlPrompter.Core.Compatibility.SsmsFeature.ActiveConnection))
            {
                return null;
            }

            var factoryType = ScriptFactoryType();
            var factory = factoryType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var active = factory?.GetType().GetProperty("CurrentlyActiveWndConnectionInfo")?.GetValue(factory);
            var info = active?.GetType().GetProperty("UIConnectionInfo")?.GetValue(active);
            return FromInfo(info);
        }

        /// <summary>
        /// The connection of the active window, null when that window is not connected: <see cref="GetActive"/> can still return the connection of the
        /// window that was active before, so the state of the window itself decides (#317, #322).
        /// </summary>
        public static ConnectionSettings GetActiveOfWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var connection = GetActive();
            var main = System.Windows.Application.Current?.MainWindow;
            if (connection != null && main != null && WindowStatusColoring.TryGetFront(new System.Windows.Interop.WindowInteropHelper(main).Handle, out var connected, out _) && !connected)
            {
                return null;
            }

            return connection;
        }

        private static object mruManager;

        /// <summary>
        /// The connection that SSMS itself remembered for a server and SQL login (the user ticked <i>Remember Password</i> in the Connect dialog): the
        /// recent connections of the dialog (<c>IConnectionMruManager.GetMruList</c> of <c>VSConnectionMruManager</c>) hold the password, which is used in
        /// memory for the reconnect and never written anywhere by SQL Prompter (#331). Null when there is none.
        /// </summary>
        private static ConnectionSettings FindRemembered(string server, string login, string database)
        {
            try
            {
                if (mruManager == null)
                {
                    // The assembly of the Connect dialog is loaded only when SSMS first shows the dialog, which is after the tabs are restored when SSMS starts empty (#543).
                    var type = AssemblyTypes.FindLoaded("Microsoft.SqlServer.ConnectionDlg.VS.VSConnectionMruManager") ?? AssemblyTypes.LoadFromSsms("Microsoft.SqlServer.ConnectionDlg.VS", "Microsoft.SqlServer.ConnectionDlg.VS.VSConnectionMruManager");
                    if (type == null)
                    {
                        Log.Write("The connections remembered by SSMS cannot be read: the class of the Connect dialog was not found.");
                    }

                    mruManager = type == null ? null : Activator.CreateInstance(type);
                }

                var list = mruManager?.GetType().GetMethod("GetMruList", Type.EmptyTypes)?.Invoke(mruManager, null) as System.Collections.IEnumerable;
                ConnectionSettings best = null;
                foreach (var item in list ?? new object[0])
                {
                    var settings = ParseInfo(item.GetType().GetProperty("UIConnectionInfo")?.GetValue(item), false);
                    if (settings != null && settings.Authentication == AuthenticationKind.SqlPassword && !string.IsNullOrEmpty(settings.Password) &&
                        string.Equals(settings.Server, server, StringComparison.OrdinalIgnoreCase) && string.Equals(settings.UserName, login, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(settings.Database, database, StringComparison.OrdinalIgnoreCase))
                        {
                            return settings;
                        }

                        best = best ?? settings;
                    }
                }

                return best;
            }
            catch (Exception ex)
            {
                Log.Error("Reading the connections remembered by SSMS failed", ex);
                return null;
            }
        }

        /// <summary>The data of a <c>UIConnectionInfo</c>; remembers a SQL login that has a password for reconnecting restored tabs.</summary>
        private static ConnectionSettings FromInfo(object info) => ParseInfo(info, true);

        private static ConnectionSettings ParseInfo(object info, bool remember)
        {
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
            var settings = new ConnectionSettings(
                server,
                options?["DATABASE"],
                authentication,
                Read("UserName"),
                authentication == AuthenticationKind.SqlPassword ? Read("Password") : null,
                options?["ENCRYPT_CONNECTION"],
                options?["TRUST_SERVER_CERTIFICATE"]);
            if (remember)
            {
                LastSeenByServer[(server ?? string.Empty).ToUpperInvariant()] = settings;
                if (authentication == AuthenticationKind.SqlPassword && !string.IsNullOrEmpty(settings.Password))
                {
                    LiveSqlLogins[LoginKey(settings.Server, settings.UserName)] = settings;
                }
            }

            return settings;
        }

        /// <summary>
        /// Opens a new query window connected like the active one but to <paramref name="database"/> and puts <paramref name="text"/> into it
        /// (verified in SSMS 22.11, see docs/architecture.md). Returns false when there is no connected window to copy from.
        /// </summary>
        public static bool OpenNewWindow(string database, string text, string server = null, string login = null, OpenedTabKind kind = OpenedTabKind.UserText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!SsmsCompatibility.Has(SqlPrompter.Core.Compatibility.SsmsFeature.OpenWindow))
            {
                return false;
            }

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
                // A SQL login gets the password of a live connection or the one SSMS remembered (never stored by us); without one SSMS asks.
                ConnectionSettings known = null;
                if (login != null && !LiveSqlLogins.TryGetValue(LoginKey(server, login), out known))
                {
                    known = FindRemembered(server, login, database);
                }

                SetProperty(clone, "Password", known?.Password);
                SetProperty(clone, "InMemoryPassword", null);
                LastSeenByServer.TryGetValue(server.ToUpperInvariant(), out var seen);
                var encrypt = known?.Encrypt ?? seen?.Encrypt;
                var trust = known?.TrustServerCertificate ?? seen?.TrustServerCertificate;
                if (!string.IsNullOrEmpty(encrypt))
                {
                    cloneOptions["ENCRYPT_CONNECTION"] = encrypt;
                }

                if (!string.IsNullOrEmpty(trust))
                {
                    cloneOptions["TRUST_SERVER_CERTIFICATE"] = trust;
                }
            }

            var create = factory.GetType().GetMethods().FirstOrDefault(m =>
                m.Name == "CreateNewBlankScript" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType == infoType);
            if (create == null)
            {
                return false;
            }

            var scriptType = Enum.Parse(create.GetParameters()[0].ParameterType, "Sql");
            var before = ActiveDocumentName();
            var differentServer = !string.IsNullOrEmpty(server) && !string.Equals(server, Convert.ToString(infoType.GetProperty("ServerName")?.GetValue(info)), StringComparison.OrdinalIgnoreCase);
            // The window is created and connected in one call that may show the Connect dialog (modal), fail or throw; the text goes into the tab whatever came of it (#511).
            var outcome = NewTabOpener.Open(
                () => create.Invoke(factory, new[] { scriptType, clone, null }),
                () => WaitForNewWindow(before),
                () => InsertIntoActiveWindow(text),
                ex => Log.Error("Creating the query window failed (the connection was refused or cancelled); the text is put into the tab anyway", ex));
            if (outcome == NewTabOutcome.Filled && differentServer)
            {
                SayWhenNotConnected(server);
            }

            if (outcome == NewTabOutcome.Filled && OpenedTabState.StartsUnmodified(kind))
            {
                // A text the user did not write: the tab starts as saved, closing it unchanged asks nothing (#512).
                UnmodifiedTab.Schedule(ActiveDocumentName(), text ?? string.Empty);
            }

            return outcome == NewTabOutcome.Filled;
        }

        /// <summary>Opens a new query tab with the DTE command File.NewQuery (SSMS asks for a connection itself) and puts the text into it, whatever the connection did (#511).</summary>
        public static bool OpenNewQueryWithText(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var before = ActiveDocumentName();
            return NewTabOpener.Open(
                () => ExecuteNewQuery(),
                () => WaitForNewWindow(before),
                () => InsertIntoActiveWindow(text),
                ex => Log.Error("Opening a new query tab failed; the text is put into the tab anyway", ex)) == NewTabOutcome.Filled;
        }

        private static void ExecuteNewQuery()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            (Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ExecuteCommand("File.NewQuery");
        }

        /// <summary>The document that is active now (the new window of SSMS becomes the active one), or null.</summary>
        private static string ActiveDocumentName()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return (Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ActiveDocument?.FullName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Waits (the message loop keeps running) for up to two seconds until another document than <paramref name="before"/> is the active one.</summary>
        private static bool WaitForNewWindow(string before)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (true)
            {
                var now = ActiveDocumentName();
                if (now != null && !string.Equals(now, before, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }

                PumpMessages(50);
            }
        }

        private static void PumpMessages(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        /// <summary>
        /// Puts the text at the start of the active window: through its text view, or, when the view is not there yet (a window whose connection was cancelled
        /// may take a moment to show its editor), through the DTE text document. False when neither worked.
        /// </summary>
        private static bool InsertIntoActiveWindow(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var content = text ?? string.Empty;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var view = ActiveEditor.GetView();
                if (view != null)
                {
                    view.TextBuffer.Insert(0, content);
                    return true;
                }

                PumpMessages(100);
            }

            try
            {
                var document = (Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ActiveDocument;
                if (document?.Object("TextDocument") is EnvDTE.TextDocument textDocument)
                {
                    textDocument.StartPoint.CreateEditPoint().Insert(content);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Putting the text into the new window failed", ex);
            }

            return false;
        }

        /// <summary>The new window is in front: when it is not connected the status bar says so (SSMS has already told the reason itself).</summary>
        private static void SayWhenNotConnected(string server)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var main = System.Windows.Application.Current?.MainWindow;
                if (main == null || !WindowStatusColoring.TryGetFront(new System.Windows.Interop.WindowInteropHelper(main).Handle, out var connected, out _) || connected)
                {
                    return;
                }

                Log.Write("The query window for " + server + " is not connected; the text is in the tab.");
                if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar)) is Microsoft.VisualStudio.Shell.Interop.IVsStatusbar statusBar)
                {
                    statusBar.SetText("SQL Prompter: could not connect " + server + ", the query is opened without a connection.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Checking the connection of the new window failed", ex);
            }
        }

        /// <summary>
        /// Opens a window for a saved tab: connected with Windows authentication when the tab had that, otherwise unconnected with a comment
        /// that names the server and database it had (a password is never stored). Returns false when SSMS refused.
        /// </summary>
        public static bool OpenSavedTab(SavedTab tab, out bool connected)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            connected = false;
            if (!SsmsCompatibility.Has(SqlPrompter.Core.Compatibility.SsmsFeature.RestoreTabs))
            {
                return false;
            }

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
            Log.Write("Opening a saved tab: server " + tab.Server + ", database " + tab.Database + ", login " + (string.IsNullOrEmpty(tab.Login) ? "(Windows authentication or none)" : tab.Login) + ", reconnect " + SettingsService.Current.Behavior.ReconnectSqlTabs + ".");
            object info = null;
            var sqlKey = tab.IsConnected && !string.IsNullOrEmpty(tab.Login) ? LoginKey(tab.Server, tab.Login) : null;
            var reconnect = sqlKey != null && SettingsService.Current.Behavior.ReconnectSqlTabs && !DeclinedSqlLogins.ContainsKey(sqlKey);
            ConnectionSettings live = null;
            var remembered = false;
            if (reconnect)
            {
                LiveSqlLogins.TryGetValue(sqlKey, out live);
                if (live == null)
                {
                    live = FindRemembered(tab.Server, tab.Login, tab.Database);
                    remembered = live != null;
                }
            }

            if (tab.IsConnected && (string.IsNullOrEmpty(tab.Login) || reconnect))
            {
                var infoType = create.GetParameters()[1].ParameterType;
                info = Activator.CreateInstance(infoType);
                SetProperty(info, "ServerName", tab.Server);
                SetProperty(info, "ServerType", new Guid("8c91a03d-f9b4-46c0-a305-b5dcc79ff907")); // the database engine, as the connection info of a window SSMS connected itself has it
                SetProperty(info, "ApplicationName", "Microsoft SQL Server Management Studio");
                SetProperty(info, "AuthenticationType", reconnect ? 1 : 0);
                if (reconnect)
                {
                    // The password is the one SSMS holds for a live connection of this server and login, or none: SSMS then opens its own Connect
                    // dialog with the server, login and database filled in (verified in SSMS 22, see docs/architecture.md).
                    SetProperty(info, "UserName", tab.Login);
                    SetProperty(info, "Password", live?.Password);
                    if (live == null && SettingsService.Current.Behavior.PrefillRememberPassword)
                    {
                        // Opt-in (#333): the Connect dialog that SSMS opens for a tab without a password has Remember Password already ticked.
                        SetProperty(info, "PersistPassword", true);
                    }
                }

                var options = (NameValueCollection)infoType.GetProperty("AdvancedOptions").GetValue(info);
                options["DATABASE"] = tab.Database ?? string.Empty;
                // The encryption options of the tab, or those of the live connection of the same login (a missing "trust the certificate" fails the login).
                LastSeenByServer.TryGetValue((tab.Server ?? string.Empty).ToUpperInvariant(), out var seen);
                var encrypt = !string.IsNullOrEmpty(tab.Encrypt) ? tab.Encrypt : live?.Encrypt ?? seen?.Encrypt;
                var trust = !string.IsNullOrEmpty(tab.TrustServerCertificate) ? tab.TrustServerCertificate : live?.TrustServerCertificate ?? seen?.TrustServerCertificate;
                if (!string.IsNullOrEmpty(encrypt))
                {
                    options["ENCRYPT_CONNECTION"] = encrypt;
                }

                if (!string.IsNullOrEmpty(trust))
                {
                    options["TRUST_SERVER_CERTIFICATE"] = trust;
                }
            }
            else if (tab.IsConnected && !text.StartsWith("-- SQL Prompter: this tab was connected", StringComparison.Ordinal) && !reconnect)
            {
                text = "-- SQL Prompter: this tab was connected to " + tab.Server + (string.IsNullOrEmpty(tab.Database) ? string.Empty : ", database " + tab.Database) +
                    " as " + tab.Login + ". Connect again to continue.\r\n" + text;
            }

            var scriptType = Enum.Parse(create.GetParameters()[0].ParameterType, "Sql");
            var documentBefore = ActiveDocumentName();
            try
            {
                create.Invoke(factory, new[] { scriptType, info, null });
            }
            catch (Exception ex)
            {
                // The connection was refused or the dialog cancelled and SSMS threw: the window may exist all the same, and the text goes into it (#511).
                Log.Error("Opening the window of a saved tab failed; the text is put into the tab anyway", ex);
            }

            connected = info != null;
            if (info == null)
            {
                // With no connection given SSMS copies the one of the active window, so the new window is disconnected explicitly, and the result is
                // checked: a window that stayed connected would run the tab's text on another server (#324).
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        (Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ExecuteCommand("Query.Disconnect");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Disconnecting the restored tab failed", ex);
                    }

                    var window = System.Windows.Application.Current?.MainWindow;
                    object ignored = null;
                    var stillConnected = false;
                    if (window == null || !WindowStatusColoring.TryGetFront(new System.Windows.Interop.WindowInteropHelper(window).Handle, out stillConnected, out ignored) || !stillConnected)
                    {
                        break;
                    }

                    Log.Write("The restored tab is still connected after Query.Disconnect (attempt " + attempt + ").");
                }
            }
            else if (reconnect)
            {
                // The window opened with the data of a live connection (no prompt) or after the Connect dialog of SSMS; see whether it is connected.
                var main = System.Windows.Application.Current?.MainWindow;
                object front = null;
                var known = main != null && WaitForConnectedWindow(new System.Windows.Interop.WindowInteropHelper(main).Handle, out connected, out front);
                if (!known)
                {
                    // The new window has not shown its editor yet (it was seen after a reopening with the key): SSMS is connecting it, which is not a failure.
                    Log.Write("A restored tab was opened as " + tab.Login + " to " + tab.Server + "; the state of the new window could not be read yet, it is taken as connected.");
                    connected = true;
                }
                else if (connected)
                {
                    FromInfo(front);
                    DeclinedSqlLogins.TryRemove(sqlKey, out _);
                    text = WithoutReconnectComment(text);
                    Log.Write("A restored tab was connected as " + tab.Login + " to " + tab.Server + (live == null ? " (the user typed the password in the Connect dialog)." : remembered ? " (with the password SSMS remembered for the login)." : " (with the live connection of the same login)."));
                }
                else
                {
                    connected = false;
                    // Whatever the reason (the Connect dialog was cancelled, the login was refused), the other tabs of this login are not tried or asked again in this run.
                    DeclinedSqlLogins[sqlKey] = true;
                    Log.Write("A restored tab was not connected as " + tab.Login + " to " + tab.Server + (live != null ? " with a live connection" : " after the Connect dialog") + " (" + (known ? "cancelled or refused" : "the window state is unknown") + "); the tab stays disconnected and the other tabs of this login are not tried again.");
                    if (!text.StartsWith("-- SQL Prompter: this tab was connected", StringComparison.Ordinal))
                    {
                        text = "-- SQL Prompter: this tab was connected to " + tab.Server + (string.IsNullOrEmpty(tab.Database) ? string.Empty : ", database " + tab.Database) +
                            " as " + tab.Login + ". Connect again to continue.\r\n" + text;
                    }
                }
            }

            if (!WaitForNewWindow(documentBefore))
            {
                Log.Write("Restoring a tab: no new window came out of it.");
                return false;
            }

            return InsertIntoActiveWindow(text);
        }

        /// <summary>
        /// Waits (the message loop keeps running) for up to three seconds until the window in front is connected; returns false when no query window
        /// has been shown at all (its editor does not exist yet), true when one is, connected or not.
        /// </summary>
        private static bool WaitForConnectedWindow(IntPtr mainWindow, out bool connected, out object info)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            var found = false;
            connected = false;
            info = null;
            while (true)
            {
                found = WindowStatusColoring.TryGetFront(mainWindow, out connected, out info);
                if ((found && connected) || DateTime.UtcNow >= deadline)
                {
                    return found;
                }

                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    frame.Continue = false;
                };
                timer.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
        }

        /// <summary>
        /// Connects an existing query window (its <c>SqlScriptEditorControl</c>) with the data of a saved tab, silently: Windows authentication, or a SQL
        /// login whose password is that of a live connection or the one SSMS remembered. No dialog is ever opened. Returns false (and the reason) when
        /// there is nothing to connect with. Uses the public <c>SqlScriptEditorControl.SetConnection(UIConnectionInfo)</c> (#335).
        /// </summary>
        public static bool ReconnectEditor(object editor, SavedTab tab, out string why)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            why = null;
            var infoType = editor?.GetType().GetProperty("Connection")?.PropertyType;
            var setConnection = infoType == null ? null : editor.GetType().GetMethod("SetConnection", new[] { infoType });
            if (setConnection == null || !tab.IsConnected)
            {
                why = "the window cannot be connected from here";
                return false;
            }

            var sql = !string.IsNullOrEmpty(tab.Login);
            ConnectionSettings known = null;
            if (sql)
            {
                if (!LiveSqlLogins.TryGetValue(LoginKey(tab.Server, tab.Login), out known))
                {
                    known = FindRemembered(tab.Server, tab.Login, tab.Database);
                }

                if (known == null)
                {
                    why = "no password to connect with (no live connection and none remembered by SSMS)";
                    return false;
                }
            }

            var info = Activator.CreateInstance(infoType);
            SetProperty(info, "ServerName", tab.Server);
            SetProperty(info, "ServerType", new Guid("8c91a03d-f9b4-46c0-a305-b5dcc79ff907"));
            SetProperty(info, "ApplicationName", "Microsoft SQL Server Management Studio");
            SetProperty(info, "AuthenticationType", sql ? 1 : 0);
            if (sql)
            {
                SetProperty(info, "UserName", tab.Login);
                SetProperty(info, "Password", known.Password);
            }

            LastSeenByServer.TryGetValue((tab.Server ?? string.Empty).ToUpperInvariant(), out var seen);
            var options = (NameValueCollection)infoType.GetProperty("AdvancedOptions").GetValue(info);
            options["DATABASE"] = tab.Database ?? string.Empty;
            var encrypt = !string.IsNullOrEmpty(tab.Encrypt) ? tab.Encrypt : known?.Encrypt ?? seen?.Encrypt;
            var trust = !string.IsNullOrEmpty(tab.TrustServerCertificate) ? tab.TrustServerCertificate : known?.TrustServerCertificate ?? seen?.TrustServerCertificate;
            if (!string.IsNullOrEmpty(encrypt))
            {
                options["ENCRYPT_CONNECTION"] = encrypt;
            }

            if (!string.IsNullOrEmpty(trust))
            {
                options["TRUST_SERVER_CERTIFICATE"] = trust;
            }

            // SetConnection only hands the new connection info to the window; its (non-public) Connect() makes the connection (verified: after
            // SetConnection alone the window stayed disconnected).
            setConnection.Invoke(editor, new[] { info });
            editor.GetType().GetMethod("Connect", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null)?.Invoke(editor, null);
            return true;
        }

        private const string ReconnectComment = "-- SQL Prompter: this tab was connected";

        /// <summary>The text without the comment line that a tab gets when it could not be reconnected (it is connected now).</summary>
        internal static string WithoutReconnectComment(string text)
        {
            if (text == null || !text.StartsWith(ReconnectComment, StringComparison.Ordinal))
            {
                return text;
            }

            var end = text.IndexOf('\n');
            return end < 0 ? string.Empty : text.Substring(end + 1);
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
        /// <summary>The type of an assembly of SSMS that is not loaded yet: by its name, then from the folders of SSMS next to the main executable (the shell keeps its extensions there).</summary>
        public static Type LoadFromSsms(string assemblyName, string fullName)
        {
            try
            {
                try
                {
                    return System.Reflection.Assembly.Load(assemblyName)?.GetType(fullName, throwOnError: false);
                }
                catch (System.IO.IOException)
                {
                    // Not found by name: look in the folders of SSMS.
                }

                var folder = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                foreach (var candidate in new[] { folder, System.IO.Path.Combine(folder, "Extensions", "Application") })
                {
                    var file = System.IO.Path.Combine(candidate, assemblyName + ".dll");
                    if (System.IO.File.Exists(file))
                    {
                        return System.Reflection.Assembly.LoadFrom(file).GetType(fullName, throwOnError: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Loading " + assemblyName + " failed", ex);
            }

            return null;
        }

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
