using System;
using System.Linq;
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
        private static readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
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
                    var shared = SettingsService.Current.Snippets.SharedFolderList().Select(f => Environment.ExpandEnvironmentVariables(f)).ToList();
                    Watch(new[] { folder }.Concat(shared).ToList());
                    var store = new SnippetStore(folder);
                    if (SettingsService.Current.Snippets.InstallBuiltIn)
                    {
                        var installed = new BuiltInInstaller(store).Install();
                        if (installed > 0)
                        {
                            Log.Write("Snippets: installed " + installed + " built-in snippets into " + folder + ".");
                        }
                    }

                    var result = store.Load();
                    var sharedLoaded = new List<IReadOnlyList<Snippet>>();
                    foreach (var sharedFolder in shared)
                    {
                        var loaded = new SnippetStore(sharedFolder).Load();
                        sharedLoaded.Add(loaded.Snippets);
                        if (loaded.Errors.Count > 0)
                        {
                            Log.Write("Snippets: " + loaded.Errors.Count + " files of the shared folder " + sharedFolder + " could not be read.");
                        }
                    }

                    var merged = SnippetMerge.Merge(result.Snippets, sharedLoaded);
                    lock (Gate)
                    {
                        snippets = merged;
                    }

                    Log.Write("Snippets: loaded " + merged.Count + " (" + shared.Count + " shared folders) from " + folder + (result.Errors.Count > 0 ? "; " + result.Errors.Count + " files could not be read: " + string.Join("; ", result.Errors) : "."));
                    Changed?.Invoke(null, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error("Loading the snippets failed", ex);
                }
            });
        }

        private static void Watch(System.Collections.Generic.IReadOnlyList<string> folders)
        {
            lock (Gate)
            {
                var key = string.Join("|", folders);
                if (string.Equals(key, watchedFolder, StringComparison.OrdinalIgnoreCase) && watchers.Count > 0)
                {
                    return;
                }

                foreach (var old in watchers)
                {
                    old.Dispose();
                }

                watchers.Clear();
                watchedFolder = key;

                // A burst of changes (copying many files) reloads once.
                debounce = debounce ?? new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
                foreach (var folder in folders)
                {
                    try
                    {
                        if (!Directory.Exists(folder))
                        {
                            continue;
                        }

                        var watcher = new FileSystemWatcher(folder, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                        FileSystemEventHandler onChange = (s, e) => debounce.Change(700, Timeout.Infinite);
                        watcher.Changed += onChange;
                        watcher.Created += onChange;
                        watcher.Deleted += onChange;
                        watcher.Renamed += (s, e) => debounce.Change(700, Timeout.Infinite);
                        watcher.EnableRaisingEvents = true;
                        watchers.Add(watcher);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Watching the snippet folder " + folder + " failed", ex);
                    }
                }
            }
        }
    }
}