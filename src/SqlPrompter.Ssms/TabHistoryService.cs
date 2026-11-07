using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.History;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The history of query tabs (#302): every few seconds the open query tabs are compared with what the history knows (the work on the UI thread
    /// is reading the text of each tab, nothing touches the database), a changed text that stayed the same for one check becomes a version, and a
    /// tab that is closed, saved or run, or SSMS shutting down, stores the text at once. The file is %APPDATA%\SqlPrompter\tabhistory.jsonl.
    /// Needs the history to be on (<c>KeepHistory</c>); local only.
    /// </summary>
    internal static class TabHistoryService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SqlPrompter",
            "tabhistory.jsonl");

        private static readonly TabHistoryStore Store = new TabHistoryStore(FilePath);
        private static readonly Dictionary<string, ConnectionSettings> Connections = new Dictionary<string, ConnectionSettings>(StringComparer.OrdinalIgnoreCase);
        private static readonly object ChainGate = new object();
        private static Task chain = Task.CompletedTask;
        private static volatile bool ready;
        private static readonly HashSet<string> KnownOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static DispatcherTimer timer;
        private static DTE dte;

        /// <summary>The tabs as rows of the history window.</summary>
        public static IReadOnlyList<HistoryEntry> Entries => ready ? Store.Entries() : new HistoryEntry[0];

        public static void Delete(IEnumerable<string> ids) => Store.Delete(ids);

        public static void Start(DTE environment)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte = environment;
            Enqueue(() =>
            {
                try
                {
                    Store.Load();
                    var removed = Store.Prune(SettingsService.Current.Behavior.HistoryDays, DateTime.UtcNow);
                    ready = true;
                    Log.Write("Tab history: " + Store.Tabs.Count + " tabs" + (removed > 0 ? ", " + removed + " old ones removed." : "."));
                }
                catch (Exception ex)
                {
                    Log.Error("Loading the tab history failed", ex);
                }
            });

            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(Math.Max(1, SettingsService.Current.Behavior.TabCheckSeconds)) };
            timer.Tick += (s, e) => Check(false);
            timer.Start();
            SettingsService.Changed += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                timer.Interval = TimeSpan.FromSeconds(Math.Max(1, SettingsService.Current.Behavior.TabCheckSeconds));
            };
        }

        private static bool Active =>
            ready && dte != null && SettingsService.Current.Behavior.KeepHistory && SettingsService.Current.Behavior.TrackTabs;

        /// <summary>The store work runs one item after another in the background, so the order of the events is kept and the editor never waits for the file.</summary>
        private static void Enqueue(Action work)
        {
            lock (ChainGate)
            {
                chain = chain.ContinueWith(_ =>
                {
                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Updating the tab history failed", ex);
                    }
                }, TaskScheduler.Default);
            }
        }

        internal sealed class Snapshot
        {
            public string Key;
            public string Name;
            public string Server;
            public string Database;
            public string Text;
        }

        /// <summary>
        /// Reads the open query tabs and hands them to the store. <paramref name="flush"/> stores a changed text at once instead of waiting for a
        /// second check (a tab was saved or run, or SSMS is shutting down).
        /// </summary>
        public static void Check(bool flush)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (!Active)
                {
                    return;
                }

                var snapshots = Read(null);
                var now = DateTime.UtcNow;
                var behavior = SettingsService.Current.Behavior;
                var present = new HashSet<string>(snapshots.Select(t => t.Key), StringComparer.OrdinalIgnoreCase);
                var gone = KnownOpen.Where(k => !present.Contains(k)).ToList();
                KnownOpen.Clear();
                KnownOpen.UnionWith(present);
                Enqueue(() =>
                {
                    foreach (var key in gone)
                    {
                        Store.Close(key, now);
                    }

                    Store.MaxVersions = behavior.TabVersionsMax;
                    Store.MinVersionInterval = TimeSpan.FromSeconds(Math.Max(1, behavior.TabCheckSeconds));
                    foreach (var tab in snapshots)
                    {
                        Store.Track(tab.Key, tab.Name, tab.Server, tab.Database, tab.Text, now);
                        if (flush)
                        {
                            Store.Flush(tab.Key, tab.Text, now);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error("Checking the query tabs failed", ex);
            }
        }

        /// <summary>
        /// A query tab was closed by the user (not as a part of the shutdown). The window is not read: reading a document that is closing keeps it
        /// alive in SSMS (it stayed in the list of documents), so the store closes the tab with the text the last check saw.
        /// </summary>
        public static void Closing(string document)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (!Active || string.IsNullOrEmpty(document) || TabSessionService.Current?.IsShuttingDown == true)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                Enqueue(() => Store.Close(document, now));
                lock (Connections)
                {
                    Connections.Remove(document);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Recording the closing of the tab failed", ex);
            }
        }
        /// <summary>The query tabs (or the one named <paramref name="only"/>) with their text and connection.</summary>
        private static List<Snapshot> Read(string only)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<Snapshot>();
            var active = dte.ActiveDocument;
            if (active != null && string.Equals(active.Language, "SQL", StringComparison.OrdinalIgnoreCase))
            {
                var connection = SsmsConnection.GetActiveOfWindow();
                lock (Connections)
                {
                    if (connection == null)
                    {
                        Connections.Remove(active.FullName);
                    }
                    else
                    {
                        Connections[active.FullName] = connection;
                    }
                }
            }

            foreach (Document document in dte.Documents)
            {
                try
                {
                    if (!string.Equals(document.Language, "SQL", StringComparison.OrdinalIgnoreCase) || (only != null && !string.Equals(document.FullName, only, StringComparison.OrdinalIgnoreCase)) || TabSessionService.IsClosedWindow(document))
                    {
                        continue;
                    }

                    var textDocument = document.Object("TextDocument") as TextDocument;
                    var text = textDocument?.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
                    ConnectionSettings connection;
                    lock (Connections)
                    {
                        Connections.TryGetValue(document.FullName, out connection);
                    }

                    result.Add(new Snapshot
                    {
                        Key = document.FullName,
                        Name = TabSessionService.Current?.NameOf(document.FullName) ?? Path.GetFileName(document.FullName),
                        Server = connection?.Server,
                        Database = connection?.Database,
                        Text = text,
                    });
                }
                catch (Exception)
                {
                    // A window that is opening or closing cannot always be read (E_FAIL); the next check finds it.
                }
            }

            return result;
        }
    }
}
