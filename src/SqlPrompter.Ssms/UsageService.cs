using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Completion;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The usage statistics that order the list: %APPDATA%\SqlPrompter\usage.json, loaded in the background at start and saved a few
    /// seconds after the last change. Local only; nothing is sent anywhere.
    /// </summary>
    internal static class UsageService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SqlPrompter",
            "usage.json");

        private static readonly object Gate = new object();
        private static UsageStatistics statistics = new UsageStatistics();
        private static Timer saveTimer;

        /// <summary>The statistics; empty until the file has been read.</summary>
        public static UsageStatistics Statistics
        {
            get
            {
                lock (Gate)
                {
                    return statistics;
                }
            }
        }

        public static void Start()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var loaded = File.Exists(FilePath) ? UsageStatistics.FromJson(File.ReadAllText(FilePath, Encoding.UTF8)) : new UsageStatistics();
                    lock (Gate)
                    {
                        // What was chosen before the file was read is kept.
                        loaded.MergeFrom(statistics);
                        statistics = loaded;
                    }

                    Log.Write("Usage statistics: " + loaded.EntryCount + " items.");
                }
                catch (Exception ex)
                {
                    Log.Error("Loading the usage statistics failed", ex);
                }
            });
        }

        public static void Record(string key)
        {
            Statistics.Record(key);
            ScheduleSave();
        }

        /// <summary>Forgets everything that was counted, also in the file.</summary>
        public static void Reset()
        {
            Statistics.Clear();
            Save();
            Log.Write("Usage statistics reset.");
        }

        private static void ScheduleSave()
        {
            lock (Gate)
            {
                if (saveTimer == null)
                {
                    saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
                }

                saveTimer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            }
        }

        private static void Save()
        {
            try
            {
                var current = Statistics;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, current.ToJson(), new UTF8Encoding(false));
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }

                File.Move(temp, FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("Saving the usage statistics failed", ex);
            }
        }
    }
}
