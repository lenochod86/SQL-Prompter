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
    /// starts. The list is written when the user opens or saves a query window, shortly after one is closed (so a tab closed by hand is gone from the
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
        private DispatcherTimer closeTimer;
        private bool shuttingDown;
        private string lastSummary;
        private readonly ClosedTabs closedTabs = new ClosedTabs();
        private bool sawTabs;
        private int emptyTicks;
        private readonly HashSet<string> describeFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The service of this SSMS instance (the commands and the quick actions reach it through this).</summary>
        public static TabSessionService Current { get; private set; }

        private TabSessionService(DTE dte)
        {
            this.dte = dte;
            Current = this;
        }

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
                    // The windows were closed a moment ago by SSMS, not by the user: the write that waits for them is dropped.
                    service.closeTimer?.Stop();
                    service.shuttingDown = true;
                    Log.Write("Shutdown: SSMS announces the shutdown; the tabs of the last write are kept.");
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
            Save(true);
        }

        /// <summary>A window is closing: write the list in a moment, unless SSMS announces the shutdown first (it closes every window then).</summary>
        private void OnClosing()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (shuttingDown)
            {
                return;
            }

            if (closeTimer == null)
            {
                closeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
                closeTimer.Tick += (s, e) =>
                {
                    closeTimer.Stop();
                    Save(true);
                };
            }

            closeTimer.Stop();
            closeTimer.Start();
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
                if (dwReadLocksRemaining == 0 && dwEditLocksRemaining == 0)
                {
                    owner.OnClosing();
                }

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

        private bool OpenTab(SavedTab tab)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var opened = OpenTabCore(tab);
            if (opened && !string.IsNullOrEmpty(tab.Name) && dte.ActiveDocument != null && IsQuery(dte.ActiveDocument))
            {
                names[dte.ActiveDocument.FullName] = tab.Name;
                TabCaption.Apply(dte.ActiveDocument.FullName, tab.Name);
            }

            return opened;
        }

        private bool OpenTabCore(SavedTab tab)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
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

            return SsmsConnection.OpenSavedTab(tab);
        }

        private void Save(bool userChange = false)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (shuttingDown || !SettingsService.Current.Behavior.RestoreTabs)
                {
                    return;
                }

                var active = dte.ActiveDocument;
                if (active != null && IsQuery(active))
                {
                    var connection = SsmsConnection.GetActive();
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
                    if (!IsQuery(document))
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

            return new SavedTab
            {
                File = isFile ? name : string.Empty,
                Text = text,
                Server = connection?.Server,
                Database = connection?.Database,
                Login = connection != null && connection.Authentication == AuthenticationKind.SqlPassword ? connection.UserName : null,
                Encrypt = connection?.Encrypt,
                TrustServerCertificate = connection?.TrustServerCertificate,
                Active = active,
                Name = names.TryGetValue(name, out var tabName) ? tabName : null,
            };
        }
    }
}
