using System;

namespace SqlPrompter.Core.History
{
    /// <summary>
    /// The file behind a new query window: SSMS keeps it in the temp folder as <c>SQLQuery1.sql</c>. It is not a document of the user, so the name of the tab
    /// (a caption of the window) is ours to give; a file the user saved is renamed by SSMS itself (#534).
    /// </summary>
    public static class TemporaryQueryFile
    {
        private const char Separator = (char)92; // a backslash

        /// <summary>True when <paramref name="path"/> is a file called <c>SQLQuery...</c> directly in <paramref name="tempFolder"/>; only the text of the paths is compared (both separators are accepted).</summary>
        public static bool Is(string path, string tempFolder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(tempFolder))
            {
                return false;
            }

            var file = path.Replace('/', Separator);
            var folder = tempFolder.Replace('/', Separator).TrimEnd(Separator);
            var slash = file.LastIndexOf(Separator);
            var name = file.Substring(slash + 1);
            if (slash <= 0)
            {
                return false;
            }

            // With some SSMS settings (seen with the Miscellaneous Files project shown and hot exit off) a new window is backed by a file called ~vs1a2b3c.sql (#543).
            // It is a scratch file wherever it lives and is gone after a restart.
            if (name.StartsWith("~vs", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(file.Substring(0, slash), folder, StringComparison.OrdinalIgnoreCase) &&
                name.StartsWith("SQLQuery", StringComparison.OrdinalIgnoreCase);
        }
    }
}
