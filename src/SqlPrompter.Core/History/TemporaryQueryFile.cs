using System;

namespace SqlPrompter.Core.History
{
    /// <summary>
    /// The file behind a new query window: SSMS keeps it in the temp folder as <c>SQLQuery1.sql</c>. It is not a document of the user, so the name of the tab
    /// (a caption of the window) is ours to give; a file the user saved is renamed by SSMS itself (#534).
    /// </summary>
    public static class TemporaryQueryFile
    {
        /// <summary>True when <paramref name="path"/> is a file called <c>SQLQuery...</c> directly in <paramref name="tempFolder"/>; only the text of the paths is compared (both separators are accepted).</summary>
        public static bool Is(string path, string tempFolder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(tempFolder))
            {
                return false;
            }

            var file = path.Replace('/', '\');
            var folder = tempFolder.Replace('/', '\').TrimEnd('\');
            var slash = file.LastIndexOf('\');
            return slash > 0 &&
                string.Equals(file.Substring(0, slash), folder, StringComparison.OrdinalIgnoreCase) &&
                file.Substring(slash + 1).StartsWith("SQLQuery", StringComparison.OrdinalIgnoreCase);
        }
    }
}
