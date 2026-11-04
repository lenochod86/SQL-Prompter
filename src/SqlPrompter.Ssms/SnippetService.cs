using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The snippets of the configured folder, loaded in the background and reloaded when the folder or the setting changes.
    /// Readers take <see cref="Snippets"/>; it is never null and never blocks.
    /// </summary>
    internal static class SnippetService
    {
        private static readonly object Gate = new object();
        private static IReadOnlyList<Snippet> snippets = new Snippet[0];
        private static FileSystemWatcher watcher;
        private static Timer debounce;
        private static string watchedFolder;
        private static bool started;

        public static event EventHandler Changed;

        /// <summary>Default folder next to the settings file.</summary>
        public static string DefaultFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SqlPrompter",
            "Snippets");

        /// <summary>The folder from the settings, or the default one.</summary>
        public static string Folder
        {
            get
            {
                var configured = SettingsService.Current.Snippets.Folder;
                return string.IsNullOrWhiteSpace(configured) ? DefaultFolder : Environment.ExpandEnvironmentVariables(configured.Trim());
            }
        }

        public static IReadOnlyList<Snippet> Snippets
        {
            get
            {
                lock (Gate)
                {
                    return snippets;
                }
            }
        }

        /// <summary>Loads the snippets and starts watching the folder; also when the folder in the settings changes.</summary>
        public static void Start()
        {
            lock (Gate)
            {
                if (started)
                {
                    return;
                }

                started = true;
            }

            SettingsService.Changed += (s, e) => Reload();
            Reload();
        }

        public static void Reload()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var folder = Folder;
                    Watch(folder);
                    var result = new SnippetStore(folder).Load();
                    lock (Gate)
                    {
                        snippets = result.Snippets;
                    }

                    Log.Write("Snippets: loaded " + result.Snippets.Count + " from " + folder + (result.Errors.Count > 0 ? "; " + result.Errors.Count + " files could not be read: " + string.Join("; ", result.Errors) : "."));
                    Changed?.Invoke(null, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error("Loading the snippets failed", ex);
                }
            });
        }

        private static void Watch(string folder)
        {
            lock (Gate)
            {
                if (string.Equals(folder, watchedFolder, StringComparison.OrdinalIgnoreCase) && watcher != null)
                {
                    return;
                }

                watcher?.Dispose();
                watcher = null;
                watchedFolder = folder;
                if (!Directory.Exists(folder))
                {
                    return;
                }

                // A burst of changes (copying many files) reloads once.
                debounce = debounce ?? new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
                watcher = new FileSystemWatcher(folder, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler onChange = (s, e) => debounce.Change(700, Timeout.Infinite);
                watcher.Changed += onChange;
                watcher.Created += onChange;
                watcher.Deleted += onChange;
                watcher.Renamed += (s, e) => debounce.Change(700, Timeout.Infinite);
                watcher.EnableRaisingEvents = true;
            }
        }
    }
}
