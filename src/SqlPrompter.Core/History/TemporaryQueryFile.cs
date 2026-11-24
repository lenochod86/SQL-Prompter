using System;
using System.IO;

namespace SqlPrompter.Core.History
{
    /// <summary>
    /// The file behind a new query window: SSMS keeps it in the temp folder as <c>SQLQuery1.sql</c>. It is not a document of the user, so the name of the tab
    /// (a caption of the window) is ours to give; a file the user saved is renamed by SSMS itself (#534).
    /// </summary>
    public static class TemporaryQueryFile
    {
        public static bool Is(string path, string tempFolder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(tempFolder))
            {
                return false;
            }

            try
            {
                return Path.IsPathRooted(path) &&
                    string.Equals(Path.GetDirectoryName(path), tempFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(path).StartsWith("SQLQuery", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
