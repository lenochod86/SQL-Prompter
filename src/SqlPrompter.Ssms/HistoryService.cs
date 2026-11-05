using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.History;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The history of executed queries: %APPDATA%\SqlPrompter\history.jsonl, read in the background at start. Every run is recorded with the
    /// server and database of its window; when the window is closed the runs of that window get the closing time. Local only.
    /// </summary>
    internal static class HistoryService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SqlPrompter",
            "history.jsonl");

        private static readonly object Gate = new object();
        private static readonly HistoryStore Store = new HistoryStore(FilePath);

        // The runs of every open window, by document name, for the closing time.
        private static readonly Dictionary<string, List<string>> RunsByDocument = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<HistoryEntry> Entries => Store.Entries;

        public static void Start()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    Store.Load();
                    var removed = Store.Prune(SettingsService.Current.Behavior.HistoryDays, DateTime.UtcNow);
                    Log.Write("Query history: " + Store.Entries.Count + " runs" + (removed > 0 ? ", " + removed + " old ones removed." : "."));
                }
                catch (Exception ex)
                {
                    Log.Error("Loading the query history failed", ex);
                }
            });
        }

        /// <summary>Records a run of <paramref name="text"/> in the window <paramref name="document"/>; call on the UI thread.</summary>
        public static void Record(string text, string document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!SettingsService.Current.Behavior.KeepHistory || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var connection = SsmsConnection.GetActive();
            var login = connection != null && connection.Authentication == AuthenticationKind.SqlPassword ? connection.UserName : null;
            var server = connection?.Server;
            var database = connection?.Database;
            var key = document ?? string.Empty;
            var tab = TabName(key);
            _ = Task.Run(() =>
            {
                try
                {
                    var entry = Store.Add(text, server, database, login, DateTime.UtcNow, tab);
                    if (entry == null)
                    {
                        return;
                    }

                    lock (Gate)
                    {
                        if (!RunsByDocument.TryGetValue(key, out var runs))
                        {
                            RunsByDocument[key] = runs = new List<string>();
                        }

                        runs.Add(entry.Id);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Recording the query failed", ex);
                }
            });
        }

        /// <summary>The name of the query window: the file name of the document (SQLQuery1.sql, or the saved script).</summary>
        private static string TabName(string document)
        {
            try
            {
                return System.IO.Path.GetFileName(document);
            }
            catch (ArgumentException)
            {
                return document;
            }
        }

        public static void DocumentClosed(string document)
        {
            List<string> runs;
            var key = document ?? string.Empty;
            lock (Gate)
            {
                if (!RunsByDocument.TryGetValue(key, out runs))
                {
                    return;
                }

                RunsByDocument.Remove(key);
            }

            _ = Task.Run(() =>
            {
                try
                {
                    Store.MarkClosed(runs, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    Log.Error("Recording the closing of the window failed", ex);
                }
            });
        }

        public static void Delete(IEnumerable<string> ids) => Store.Delete(ids);
    }
}
