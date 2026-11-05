using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.History;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Keeps the open query tabs in %APPDATA%\SqlPrompter\tabs.json (text and connection, never a password) and opens them again when SSMS
    /// starts. Saving runs every few seconds on the UI thread (reading the text of a few documents is cheap) and stops when SSMS begins to
    /// shut down, so that closing the windows one by one does not empty the file. The connection of a window is only readable while it is
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
        private bool shuttingDown;
        private bool sawTabs;

        private TabSessionService(DTE dte)
        {
            this.dte = dte;
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
                service.dteEvents.OnBeginShutdown += () => service.shuttingDown = true;

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
                };
                wait.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Starting the tab session failed", ex);
            }

            return service;
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

        private bool OpenTab(SavedTab tab)
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

        private void Save()
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
                foreach (Document document in dte.Documents)
                {
                    if (!IsQuery(document))
                    {
                        continue;
                    }

                    var saved = Describe(document, ReferenceEquals(document, active) || document.FullName == active?.FullName);
                    if (saved != null)
                    {
                        tabs.Add(saved);
                    }
                }

                // An empty list is saved only after tabs were seen in this session (or restored), so a start that restored nothing never
                // wipes the saved tabs.
                sawTabs |= tabs.Count > 0;
                if (sawTabs)
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
            };
        }
    }
}
