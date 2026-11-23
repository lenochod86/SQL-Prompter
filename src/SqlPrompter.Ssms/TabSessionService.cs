using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlPrompter.Core.History;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Keeps the open query tabs in %APPDATA%\SqlPrompter\tabs.json (text and connection, never a password) and opens them again when SSMS
    /// starts. The list is written when the user opens or saves a query window, when one is closed (so a tab closed by hand is gone from the
    /// file and a crash loses nothing) and every few seconds for the text of unsaved queries. At shutdown SSMS closes all the windows in a burst and
    /// announces the shutdown a few milliseconds later, so the write that follows a close waits a moment and is dropped when the shutdown is
    /// announced; the file then keeps the tabs that were open before the burst (TabSessionPolicy). The connection of a window is only readable while it is
    /// the active one, so it is remembered per document the moment the window is in front.
    /// </summary>
    internal sealed class TabSessionService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SqlPrompter",
            "tabs.json");

        private readonly DTE dte;
        private readonly TabSessionStore store = new TabSessionStore(FilePath);
        private readonly Dictionary<string, ConnectionSettings> connections = new Dictionary<string, ConnectionSettings>(StringComparer.OrdinalIgnoreCase);
        private DispatcherTimer timer;
        private DTEEvents dteEvents;
        private IVsRunningDocumentTable rdt;
        private uint rdtCookie;
        private bool shuttingDown;
        private readonly TabShutdownGuard shutdown = new TabShutdownGuard();
        private DispatcherTimer shutdownTimer;
        private CommandEvents commandEvents;
        private SaveAsNamer saveAsNamer;
        private string lastSummary;
        private readonly ClosedTabs closedTabs = new ClosedTabs();
        private bool sawTabs;
        private int emptyTicks;
        private readonly HashSet<string> describeFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Tabs that were restored without a connection (a SQL login that could not be reconnected): what they had, by document, until they get a connection (#322).</summary>
        private readonly Dictionary<string, SavedTab> intended = new Dictionary<string, SavedTab>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The service of this SSMS instance (the commands and the quick actions reach it through this).</summary>
        public static TabSessionService Current { get; private set; }

        private TabSessionService(DTE dte)
        {
            this.dte = dte;
            Current = this;
            saveAsNamer = SaveAsNamer.Start(() => { ThreadHelper.ThrowIfNotOnUIThread(); try { return dte.ActiveDocument?.FullName; } catch { return null; } }, NameOf);
        }

        /// <summary>SSMS is shutting down or a shutdown has begun: the closing of the windows is not the user's doing.</summary>
        public bool IsShuttingDown => shuttingDown || shutdown.Pending;

        /// <summary>The window of the document gets the name of its history record as the caption and as its name (#375): the history keeps one title for the work.</summary>
        public void AdoptName(string document, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(document) || string.IsNullOrWhiteSpace(name) || string.Equals(name, System.IO.Path.GetFileName(document), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // A name that an open tab has already is kept apart with a number (SQLQuery2 (2).sql), so two tabs do not look the same (#511).
            var taken = new List<string>();
            foreach (Document known in dte.Documents.Cast<Document>().ToList())
            {
                if (known.Windows.Count > 0 && !IsClosedWindow(known) && !string.Equals(known.FullName, document, StringComparison.OrdinalIgnoreCase))
                {
                    taken.Add(NameOf(known.FullName) ?? TabCaption.DisplayName(known.FullName));
                }
            }

            name = SqlPrompter.Core.History.HistoryTabName.Unique(name.Trim(), taken);
            names[document] = name;
            TabCaption.Apply(document, name);
        }

        /// <summary>Brings the open window of the document to the front; false when there is none (it was closed meanwhile).</summary>
        public bool ActivateDocument(string document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                foreach (Document known in dte.Documents.Cast<Document>().ToList())
                {
                    if (string.Equals(known.FullName, document, StringComparison.OrdinalIgnoreCase) && known.Windows.Count > 0 && !IsClosedWindow(known))
                    {
                        known.Activate();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Activating the tab of a history entry failed", ex);
            }

            return false;
        }

        /// <summary>
        /// The active window was opened for the definition of an object (F12, quick open, find in code, dependencies, #437): its tab is named <c>schema.object1.sql</c>
        /// (<c>DefinitionTabName</c>), the lowest number the open tabs of this object do not have. The name is kept like a name from <c>Rename Query Tab</c>, so it comes back with
        /// the tab after a restart.
        /// </summary>
        public void NameDefinitionTab(string schema, string objectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var document = dte.ActiveDocument;
                if (document == null || !IsQuery(document))
                {
                    return;
                }

                var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Document known in dte.Documents.Cast<Document>().ToList())
                {
                    if (known.Windows.Count > 0 && !IsClosedWindow(known) && !string.Equals(known.FullName, document.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        open.Add(known.FullName);
                    }
                }

                var taken = names.Where(pair => open.Contains(pair.Key)).Select(pair => pair.Value).ToList();
                var tabName = SqlPrompter.Core.Navigation.DefinitionTabName.Next(schema, objectName, taken);
                names[document.FullName] = tabName;
                if (!TabCaption.Apply(document.FullName, tabName))
                {
                    names.Remove(document.FullName);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Naming the tab of a definition failed", ex);
            }
        }

        /// <summary>The name the user gave the tab of the document, or null.</summary>
        public string NameOf(string document) => document != null && names.TryGetValue(document, out var name) ? name : null;

        /// <summary>The tabs that can be reopened, the one closed last first.</summary>
        public IReadOnlyList<SavedTab> ClosedTabsList
        {
            get
            {
                ApplyClosedTabLimits();
                return closedTabs.Items;
            }
        }

        private void ApplyClosedTabLimits()
        {
            var behavior = SettingsService.Current.Behavior;
            closedTabs.Capacity = Math.Max(1, behavior.ClosedTabsKept);
            closedTabs.MaxAge = behavior.ClosedTabsHours > 0 ? TimeSpan.FromHours(behavior.ClosedTabsHours) : TimeSpan.Zero;
        }

        /// <summary>Asks for a name for the active query tab and shows it as the caption; the name is saved with the tab and comes back after a restart (#226).</summary>
        public void RenameActiveTab()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = dte.ActiveDocument;
            if (document == null || !IsQuery(document))
            {
                TellStatus("SQL Prompter: there is no query tab to rename.");
                return;
            }

            names.TryGetValue(document.FullName, out var current);
            var window = new TextPromptWindow("Rename query tab", "Name of the tab (empty gives the name SSMS chose back):", current ?? System.IO.Path.GetFileName(document.FullName));
            ActiveEditor.SetOwner(window);
            if (window.ShowDialog() != true)
            {
                return;
            }

            var name = window.Value;
            var temporaryName = System.IO.Path.GetFileName(document.FullName);
            if (name.Length == 0 || string.Equals(name, temporaryName, StringComparison.OrdinalIgnoreCase))
            {
                names.Remove(document.FullName);
                TabCaption.Apply(document.FullName, null);
            }
            else
            {
                names[document.FullName] = name;
                if (!TabCaption.Apply(document.FullName, name))
                {
                    TellStatus("SQL Prompter: the tab could not be renamed (see the log).");
                    return;
                }
            }

            Save(true);
            TellStatus("SQL Prompter: the tab is named " + (name.Length == 0 ? temporaryName : name) + ".");
        }

        private static void TellStatus(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar)) is Microsoft.VisualStudio.Shell.Interop.IVsStatusbar statusBar)
            {
                statusBar.SetText(message);
            }
        }

        public static TabSessionService Start(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var service = new TabSessionService(dte);
            if (!SettingsService.Current.Behavior.RestoreTabs)
            {
                return service;
            }

            try
            {
                service.dteEvents = dte.Events.DTEEvents;
                service.dteEvents.OnBeginShutdown += () =>
                {
                    // SSMS is closing for good; the session was written when the shutdown began (BeginShutdown) and stays as it is.
                    service.shuttingDown = true;
                    service.shutdownTimer?.Stop();
                    Log.Write("Shutdown: SSMS announces the shutdown; the tabs written when it began are kept.");
                };

                // Wait until the shell has opened what it opens by itself, then restore and only afterwards start saving, so that the
                // empty start does not overwrite the saved tabs.
                var wait = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
                var tries = 0;
                wait.Tick += (s, e) =>
                {
                    // A modal dialog (the Connect dialog SSMS may show at start) blocks the main window; opening tabs has to wait for it.
                    if (!IsMainWindowEnabled() && ++tries < 90)
                    {
                        return;
                    }

                    wait.Stop();
                    service.Restore();
                    service.timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
                    service.timer.Tick += (s2, e2) => service.Save();
                    service.timer.Start();
                    service.Subscribe();
                };
                wait.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Starting the tab session failed", ex);
            }

            return service;
        }

        /// <summary>Follows the windows being opened, saved and closed through the running document table (DTE document events did not fire for a window the user closed).</summary>
        private void Subscribe()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                rdt = (IVsRunningDocumentTable)Package.GetGlobalService(typeof(SVsRunningDocumentTable));
                rdt.AdviseRunningDocTableEvents(new TableEvents(this), out rdtCookie);
                WatchShutdown();
            }
            catch (Exception ex)
            {
                Log.Error("Subscribing to the running document table failed", ex);
            }
        }

        /// <summary>A window was opened or saved: write the list now.</summary>
        private void OnOpenedOrSaved()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Not inside the event of the running document table: reading the documents there (DTE) while one is locked or unlocked kept a closed file
            // from being opened again in the same run (Ctrl+O and the history did nothing, "the user cancelled the save dialog"). The work is done after the
            // event has returned, once for a burst of events.
            if (afterEvent == null)
            {
                afterEvent = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
                afterEvent.Tick += (s, e) =>
                {
                    afterEvent.Stop();
                    ThreadHelper.ThrowIfNotOnUIThread();
                    Save(true);
                    TabHistoryService.Check(true);
                };
            }

            afterEvent.Stop();
            afterEvent.Start();
        }

        private DispatcherTimer afterEvent;

        private const int WmClose = 0x0010;
        private const int WmQueryEndSession = 0x0011;
        private const int WmSysCommand = 0x0112;
        private const int ScClose = 0xF060;

        /// <summary>
        /// SSMS closes every window of a shutdown in a burst and announces the shutdown only afterwards, so the signal that the shutdown has begun
        /// has to come from earlier: the close messages of the main window (the X button, Alt+F4, the system menu, logging off) and the command
        /// File.Exit. When one comes the open tabs are written at once (BeginShutdown).
        /// </summary>
        private void WatchShutdown()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var window = System.Windows.Application.Current?.MainWindow;
                var source = window == null ? null : System.Windows.PresentationSource.FromVisual(window) as System.Windows.Interop.HwndSource;
                source?.AddHook(WindowMessage);
                var uiContext = System.Threading.SynchronizationContext.Current;
                Microsoft.Win32.SystemEvents.PowerModeChanged += (sender, args) =>
                {
                    if (args.Mode == Microsoft.Win32.PowerModes.Resume)
                    {
                        // The event comes on another thread: continue on the UI thread, after a pause (the network is not back at once).
#pragma warning disable VSTHRD001 // the power event has no task to switch with; the context captured on the UI thread posts back to it
                        uiContext?.Post(state =>
                        {
                            ThreadHelper.ThrowIfNotOnUIThread();
                            var later = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
                            later.Tick += (s, e) =>
                            {
                                later.Stop();
                                OnResume();
                            };
                            later.Start();
                        }, null);
#pragma warning restore VSTHRD001
                    }
                };
                WatchTabDoubleClick(window);
                commandEvents = dte.Events.CommandEvents;
                commandEvents.BeforeExecute += (string guid, int id, object customIn, object customOut, ref bool cancelDefault) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (string.Equals(guid, "{5EFC7975-14BC-11CF-9B2B-00AA00573819}", StringComparison.OrdinalIgnoreCase) && id == 229)
                    {
                        BeginShutdown("File.Exit");
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Error("Watching the shutdown failed", ex);
            }
        }

        /// <summary>A double click on a document tab asks for a name for that tab (#226); the first click has already made it the active one.</summary>
        private void WatchTabDoubleClick(System.Windows.Window window)
        {
            window?.AddHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler((sender, args) =>
            {
                if (args.ClickCount != 2 || !IsOnDocumentTab(args.OriginalSource as System.Windows.DependencyObject))
                {
                    return;
                }

                var later = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
                later.Tick += (s, e) =>
                {
                    later.Stop();
                    ThreadHelper.ThrowIfNotOnUIThread();
                    RenameActiveTab();
                };
                later.Start();
            }), true);
        }

        private static bool IsOnDocumentTab(System.Windows.DependencyObject element)
        {
            while (element != null)
            {
                if (element.GetType().Name == "DocumentTabItem")
                {
                    return true;
                }

                element = element is System.Windows.Media.Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                    : System.Windows.LogicalTreeHelper.GetParent(element);
            }

            return false;
        }

        private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (message == WmClose || message == WmQueryEndSession || (message == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScClose))
            {
                BeginShutdown("window message 0x" + message.ToString("X"));
            }

            return IntPtr.Zero;
        }

        /// <summary>The main window is being closed: write the tabs that are open now, then leave the file alone while the windows close (until the shutdown is cancelled).</summary>
        private void BeginShutdown(string reason)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (shuttingDown || shutdown.Pending)
                {
                    return;
                }

                Log.Write("Shutdown begins (" + reason + "): the open tabs are written now.");
                Save(true);
                TabHistoryService.Check(true);
                shutdown.Begin();
                if (shutdownTimer == null)
                {
                    shutdownTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
                    shutdownTimer.Tick += (s, e) =>
                    {
                        if (shutdown.Poll(DateTime.UtcNow, IsMainWindowEnabled()))
                        {
                            shutdownTimer.Stop();
                            Log.Write("Shutdown was cancelled: the tabs that are open are written again.");
                            Save(true);
                        }
                    };
                }

                shutdownTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Starting the shutdown failed", ex);
            }
        }

        private DispatcherTimer resumeTimer;
        private DateTime resumeUntil;
        private readonly Dictionary<string, int> resumeAttempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The computer has resumed from sleep or hibernation (#335): for ten minutes the tab in front is connected again when it was restored without a
        /// connection (`intended`) and the credentials are at hand (a live connection of the same login or a password SSMS remembered; no dialog),
        /// which covers a restore that failed because the network was not back yet. A tab is tried every three seconds, at most twenty times, and
        /// only while it is in front. Connections that broke during the sleep cannot be seen from outside (the window still says connected) and SSMS
        /// reconnects them when the next query runs.
        /// </summary>
        public void OnResume()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!SettingsService.Current.Behavior.ReconnectSqlTabs || intended.Count == 0)
            {
                return;
            }

            Log.Write("The computer resumed: tabs that are not connected will be tried again (" + intended.Count + ").");
            resumeAttempts.Clear();
            resumeUntil = DateTime.UtcNow.AddMinutes(10);
            if (resumeTimer == null)
            {
                resumeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
                resumeTimer.Tick += (s, e) => TryReconnectFront();
            }

            resumeTimer.Start();
        }

        private void TryReconnectFront()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (DateTime.UtcNow > resumeUntil || intended.Count == 0 || shuttingDown)
                {
                    resumeTimer.Stop();
                    return;
                }

                var document = dte.ActiveDocument;
                var main = System.Windows.Application.Current?.MainWindow;
                if (document == null || main == null || !intended.TryGetValue(document.FullName, out var want))
                {
                    return;
                }

                var handle = new System.Windows.Interop.WindowInteropHelper(main).Handle;
                if (!WindowStatusColoring.TryGetFront(handle, out var connected, out _) || connected)
                {
                    return;
                }

                resumeAttempts.TryGetValue(document.FullName, out var attempts);
                if (attempts >= 20)
                {
                    return;
                }

                resumeAttempts[document.FullName] = attempts + 1;
                if (!SsmsConnection.ReconnectEditor(WindowStatusColoring.FrontEditor(handle), want, out var why))
                {
                    if (attempts == 0)
                    {
                        Log.Write("The tab " + System.IO.Path.GetFileName(document.FullName) + " is not reconnected: " + why + ".");
                    }

                    return;
                }

                if (WindowStatusColoring.TryGetFront(handle, out connected, out _) && connected)
                {
                    intended.Remove(document.FullName);
                    if (ActiveEditor.GetView() is Microsoft.VisualStudio.Text.Editor.IWpfTextView view)
                    {
                        var text = view.TextBuffer.CurrentSnapshot.GetText();
                        var without = SsmsConnection.WithoutReconnectComment(text);
                        if (without != text)
                        {
                            view.TextBuffer.Replace(new Microsoft.VisualStudio.Text.Span(0, text.Length - without.Length), string.Empty);
                        }
                    }

                    Log.Write("The tab " + System.IO.Path.GetFileName(document.FullName) + " was connected again to " + want.Server + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reconnecting the tab after the resume failed", ex);
            }
        }

        private DispatcherTimer closeWatch;
        private DateTime closeWatchUntil;
        private bool closedSeen;
        private static readonly HashSet<string> ShownDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True when the document had a window and has none any more. SSMS keeps the document of a closed query window (an unsaved one) in the list
        /// of documents and in the running document table for a long time, with no windows (verified: <c>Windows.Count</c> is 0), so the list alone
        /// does not tell which tabs are open. A new document has no window for a moment before it gets one, hence the "had a window" condition.
        /// </summary>
        internal static bool IsClosedWindow(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (document.Windows.Count > 0)
                {
                    ShownDocuments.Add(document.FullName);
                    return false;
                }

                return ShownDocuments.Contains(document.FullName);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The running document table says a lock on a document is going away; that is also so while the window stays open and while the user has
        /// not yet answered "save changes?". The open windows are therefore looked at every 300 ms for a few seconds until one turns out to be closed
        /// (no window left); then the list is written at once, without that window (an empty list is a valid session), and the history closes the tab.
        /// The text of a closing document is never read here: that kept the document alive.
        /// </summary>
        private void OnClosing(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (shuttingDown || shutdown.Pending)
            {
                return;
            }

            closeWatchUntil = DateTime.UtcNow.AddSeconds(15);
            closedSeen = false;
            if (closeWatch == null)
            {
                closeWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
                closeWatch.Tick += (s, e) =>
                {
                    if (shuttingDown || shutdown.Pending || closedSeen || DateTime.UtcNow > closeWatchUntil)
                    {
                        closeWatch.Stop();
                        return;
                    }

                    Save(true);
                    TabHistoryService.Check(false);
                };
            }

            closeWatch.Stop();
            closeWatch.Start();
        }
        /// <summary>The events of the running document table.</summary>
        private sealed class TableEvents : IVsRunningDocTableEvents
        {
            private readonly TabSessionService owner;

            public TableEvents(TabSessionService owner)
            {
                this.owner = owner;
            }

            public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                owner.OnOpenedOrSaved();
                return VSConstants.S_OK;
            }

            public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                owner.OnClosing(docCookie);

                return VSConstants.S_OK;
            }

            public int OnAfterSave(uint docCookie)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                owner.OnOpenedOrSaved();
                return VSConstants.S_OK;
            }

            public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;

            public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame) => VSConstants.S_OK;

            public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) => VSConstants.S_OK;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr window);

        private static bool IsMainWindowEnabled()
        {
            var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            return handle == IntPtr.Zero || IsWindowEnabled(handle);
        }

        private void Restore()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var saved = store.Load();
                var opened = 0;
                SsmsConnection.GetActive(); // remembers the SQL login of the window the user connected at start, so that restored tabs of that login connect with it

                // The tab that was in front is opened last so that it is in front again.
                foreach (var tab in saved.Where(t => !t.Active).Concat(saved.Where(t => t.Active)))
                {
                    try
                    {
                        if (OpenTab(tab))
                        {
                            opened++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Restoring a tab failed", ex);
                    }
                }

                sawTabs = saved.Count == 0 || opened > 0;
                Log.Write("Tabs restored: " + opened + " of " + saved.Count + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Restoring the tabs failed", ex);
            }
        }

        /// <summary>Opens the query tab that was closed last again, with its text and connection (#226).</summary>
        public void ReopenClosedTab(SavedTab chosen = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string message;
            try
            {
                ApplyClosedTabLimits();
                var tab = chosen != null && closedTabs.Remove(chosen) ? chosen : closedTabs.Pop();
                if (tab == null)
                {
                    message = "SQL Prompter: there is no closed query tab to reopen.";
                }
                else if (OpenTab(tab))
                {
                    message = "SQL Prompter: reopened a closed query tab (" + closedTabs.Count + " more can be reopened).";
                }
                else
                {
                    message = "SQL Prompter: the closed tab could not be reopened (its file is gone or it has no connection to copy).";
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reopening a closed tab failed", ex);
                message = "SQL Prompter: reopening the tab failed (see the log).";
            }

            if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar)) is Microsoft.VisualStudio.Shell.Interop.IVsStatusbar statusBar)
            {
                statusBar.SetText(message);
            }
        }

        /// <summary>
        /// Opens the file of a history entry (#372): an open window of the file is brought to the front instead of opening a second copy; a window that is
        /// not connected gets the connection of the entry silently (as a restored tab does), when there is something to connect with.
        /// </summary>
        private void OpenDocumentFile(string file)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                dte.ItemOperations.OpenFile(file);
            }
            catch (System.Runtime.InteropServices.COMException first)
            {
                // After the window of this file was closed in this run, ItemOperations.OpenFile fails with a cancelled save prompt; the shell opens it.
                Log.Write("Opening " + System.IO.Path.GetFileName(file) + " with ItemOperations failed (" + first.Message + "); trying the command File.OpenFile.");
                try
                {
                    dte.ExecuteCommand("File.OpenFile", "\"" + file + "\"");
                }
                catch (System.Runtime.InteropServices.COMException second)
                {
                    Log.Write("File.OpenFile failed too (" + second.Message + "); trying Documents.Open.");
                    dte.Documents.Open(file, "Auto", false);
                }
            }
        }

        public bool OpenFileTab(SavedTab tab)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!File.Exists(tab.File))
                {
                    return false;
                }

                // A window of the file that is open is brought to the front; the document of a window that was closed stays in the list of SSMS for a while and
                // would make opening the file again fail (the save prompt is cancelled), so it is closed first.
                var alreadyOpen = false;
                foreach (Document known in dte.Documents.Cast<Document>().ToList())
                {
                    try
                    {
                        if (!string.Equals(known.FullName, tab.File, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        Log.Write("History file: document " + System.IO.Path.GetFileName(known.FullName) + ", windows " + known.Windows.Count + ", saved " + known.Saved + ".");
                        if (known.Windows.Count > 0)
                        {
                            known.Activate();
                            alreadyOpen = true;
                        }
                        else
                        {
                            // The document of a window that was closed: asking it to show itself opens a window for it again.
                            known.Activate();
                            Log.Write("History file: activated the document of the closed window, windows now " + known.Windows.Count + ".");
                            alreadyOpen = known.Windows.Count > 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write("A document of " + System.IO.Path.GetFileName(tab.File) + " could not be activated or closed: " + ex.Message);
                    }
                }

                if (!alreadyOpen)
                {
                    OpenDocumentFile(tab.File);
                }

                var document = dte.ActiveDocument;
                var main = System.Windows.Application.Current?.MainWindow;
                if (document == null || main == null || !string.Equals(document.FullName, tab.File, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var handle = new System.Windows.Interop.WindowInteropHelper(main).Handle;
                if (tab.IsConnected && WindowStatusColoring.TryGetFront(handle, out var connected, out _) && !connected)
                {
                    if (SsmsConnection.ReconnectEditor(WindowStatusColoring.FrontEditor(handle), tab, out var why))
                    {
                        if (WindowStatusColoring.TryGetFront(handle, out connected, out _) && connected)
                        {
                            connections[document.FullName] = SsmsConnection.GetActiveOfWindow();
                        }
                    }
                    else
                    {
                        Log.Write("The file " + System.IO.Path.GetFileName(tab.File) + " from the history is not connected: " + why + ".");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Opening the file of a history entry failed", ex);
                return false;
            }
        }

        private bool OpenTab(SavedTab tab)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var opened = OpenTabCore(tab, out var connected);
            if (opened && tab.Pinned && dte.ActiveDocument != null && IsQuery(dte.ActiveDocument))
            {
                TabCaption.Pin(dte.ActiveDocument.FullName);
            }

            if (opened && !string.IsNullOrEmpty(tab.Name) && dte.ActiveDocument != null && IsQuery(dte.ActiveDocument))
            {
                names[dte.ActiveDocument.FullName] = tab.Name;
                TabCaption.Apply(dte.ActiveDocument.FullName, tab.Name);
            }

            if (opened && connected && dte.ActiveDocument != null && IsQuery(dte.ActiveDocument))
            {
                // The window is in front and connected: its connection is only readable now, so it is remembered for the saves that follow.
                var now = SsmsConnection.GetActiveOfWindow();
                if (now != null)
                {
                    connections[dte.ActiveDocument.FullName] = now;
                }
            }

            if (opened && !connected && tab.IsConnected && !string.IsNullOrEmpty(tab.Login) && dte.ActiveDocument != null && IsQuery(dte.ActiveDocument))
            {
                // The tab stays disconnected, but it keeps the server, database and login it had, so that the next restart (or reopening) tries again.
                intended[dte.ActiveDocument.FullName] = tab;
            }

            return opened;
        }

        private bool OpenTabCore(SavedTab tab, out bool connected)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            connected = false;
            if (!string.IsNullOrEmpty(tab.File))
            {
                if (!File.Exists(tab.File))
                {
                    return false;
                }

                dte.ItemOperations.OpenFile(tab.File);
                if (!string.IsNullOrEmpty(tab.Text) && ActiveEditor.GetView() is Microsoft.VisualStudio.Text.Editor.IWpfTextView view && view.TextBuffer.CurrentSnapshot.GetText() != tab.Text)
                {
                    // The file had unsaved changes.
                    view.TextBuffer.Replace(new Microsoft.VisualStudio.Text.Span(0, view.TextBuffer.CurrentSnapshot.Length), tab.Text);
                }

                return true;
            }

            return SsmsConnection.OpenSavedTab(tab, out connected);
        }

        private void Save(bool userChange = false, string closing = null)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (shuttingDown || shutdown.Pending || !SettingsService.Current.Behavior.RestoreTabs)
                {
                    return;
                }

                var active = dte.ActiveDocument;
                if (active != null && IsQuery(active))
                {
                    var connection = SsmsConnection.GetActiveOfWindow();
                    if (connection == null)
                    {
                        connections.Remove(active.FullName);
                    }
                    else
                    {
                        connections[active.FullName] = connection;
                    }
                }

                var tabs = new List<SavedTab>();
                var openKeys = new List<KeyValuePair<string, SavedTab>>();
                foreach (Document document in dte.Documents)
                {
                    if (!IsQuery(document) || (closing != null && string.Equals(document.FullName, closing, StringComparison.OrdinalIgnoreCase)) || IsClosedWindow(document))
                    {
                        continue;
                    }

                    SavedTab saved = null;
                    try
                    {
                        saved = Describe(document, ReferenceEquals(document, active) || document.FullName == active?.FullName);
                    }
                    catch (Exception ex)
                    {
                        // One document that cannot be read (a window that is closing, E_FAIL) must not stop the others from being saved.
                        if (describeFailures.Add(document.FullName))
                        {
                            Log.Error("A tab could not be described and is skipped: " + document.FullName, ex);
                        }
                    }

                    if (saved != null)
                    {
                        tabs.Add(saved);
                        openKeys.Add(new KeyValuePair<string, SavedTab>(document.FullName, saved));
                    }
                }

                var documentNames = new List<string>();
                foreach (Document listed in dte.Documents)
                {
                    try
                    {
                        documentNames.Add(Path.GetFileName(listed.FullName));
                    }
                    catch (Exception)
                    {
                        documentNames.Add("?");
                    }
                }

                var summary = string.Join(", ", openKeys.Select(k => Path.GetFileName(k.Key))) + " of " + string.Join(", ", documentNames);
                if (summary != lastSummary)
                {
                    lastSummary = summary;
                    Log.Write("Tabs open (described): " + summary);
                }

                ApplyClosedTabLimits();
                var newlyClosed = closedTabs.Update(openKeys);
                if (newlyClosed > 0)
                {
                    closedSeen = true;
                    Log.Write("Tabs: " + newlyClosed + " closed; " + closedTabs.Count + " can be reopened.");
                }

                // An empty list is saved only after tabs were seen in this session (or restored), so a start that restored nothing never
                // wipes the saved tabs.
                sawTabs |= tabs.Count > 0;

                // SSMS closes the documents before it announces the shutdown, so a list that has just become empty may be the end of the
                // session; it is saved only when it stays empty for a second tick (the user really closed every tab).
                emptyTicks = tabs.Count == 0 && !userChange ? emptyTicks + 1 : (tabs.Count == 0 ? emptyTicks : 0);
                if (TabSessionPolicy.ShouldSave(tabs.Count, sawTabs, shuttingDown, userChange, emptyTicks))
                {
                    store.Save(tabs);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Saving the tabs failed", ex);
            }
        }

        /// <summary>True when the active document of the shell is a query tab (#503); null when it cannot be told.</summary>
        public bool? HasActiveQueryDocument()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var document = dte.ActiveDocument;
                return document != null && IsQuery(document) && dte.ActiveWindow?.Kind == "Document";
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsQuery(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return string.Equals(document.Language, "SQL", StringComparison.OrdinalIgnoreCase);
        }

        private SavedTab Describe(Document document, bool active)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var name = document.FullName;
            // A new window is backed by a file in the temp folder (SQLQuery1.sql); that is not a document of the user.
            var isFile = Path.IsPathRooted(name) && File.Exists(name) &&
                !(string.Equals(Path.GetDirectoryName(name), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(name).StartsWith("SQLQuery", StringComparison.OrdinalIgnoreCase));
            string text = null;
            if (!isFile || !document.Saved)
            {
                var textDocument = document.Object("TextDocument") as EnvDTE.TextDocument;
                text = textDocument?.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
            }

            connections.TryGetValue(name, out var connection);
            if (!isFile && string.IsNullOrWhiteSpace(text))
            {
                return null; // an empty new window is not worth restoring
            }

            intended.TryGetValue(name, out var wanted);
            if (connection != null)
            {
                intended.Remove(name);
                wanted = null;
            }

            return new SavedTab
            {
                File = isFile ? name : string.Empty,
                Text = text,
                Server = connection?.Server ?? wanted?.Server,
                Database = connection?.Database ?? wanted?.Database,
                Login = connection != null ? (connection.Authentication == AuthenticationKind.SqlPassword ? connection.UserName : null) : wanted?.Login,
                Encrypt = connection?.Encrypt ?? wanted?.Encrypt,
                TrustServerCertificate = connection?.TrustServerCertificate ?? wanted?.TrustServerCertificate,
                Active = active,
                Pinned = TabCaption.IsPinned(name),
                Name = names.TryGetValue(name, out var tabName) ? tabName : null,
            };
        }
    }
}
