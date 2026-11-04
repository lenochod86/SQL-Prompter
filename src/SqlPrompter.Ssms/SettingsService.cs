using System;
using System.IO;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Holds the current settings, shared by the completion source, the commit manager and the options page.
    /// The file is %APPDATA%\SqlPrompter\settings.json. A change applies at once: readers take <see cref="Current"/> on every use.
    /// </summary>
    internal static class SettingsService
    {
        private static readonly object Gate = new object();
        private static PrompterSettings current;

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SqlPrompter",
            "settings.json");

        public static event EventHandler Changed;

        /// <summary>Do not modify the returned object; call <see cref="Update"/> with a changed copy.</summary>
        public static PrompterSettings Current
        {
            get
            {
                lock (Gate)
                {
                    return current ?? (current = LoadFromDisk());
                }
            }
        }

        public static void Update(PrompterSettings settings)
        {
            var copy = settings.Clone();
            lock (Gate)
            {
                current = copy;
            }

            try
            {
                SettingsJson.Save(FilePath, copy);
            }
            catch (Exception ex)
            {
                Log.Error("Saving the settings failed", ex);
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static PrompterSettings LoadFromDisk()
        {
            try
            {
                return SettingsJson.Load(FilePath);
            }
            catch (Exception ex)
            {
                // A broken file must not disable completion; defaults are used until the next save replaces it.
                Log.Error("Loading the settings failed, using defaults", ex);
                return new PrompterSettings();
            }
        }
    }
}
