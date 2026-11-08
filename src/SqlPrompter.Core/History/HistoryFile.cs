using System;
using System.IO;

namespace SqlPrompter.Core.History
{
    /// <summary>What opening a history entry does when its tab was a file (#372).</summary>
    public enum HistoryOpenAction
    {
        /// <summary>The entry has no file (an unsaved tab): the text opens in a new tab.</summary>
        OpenText,

        /// <summary>The file exists and has the text of the entry: open the file.</summary>
        OpenFile,

        /// <summary>The file exists but its text differs from the entry (it changed since): open the file and offer the text of the entry too.</summary>
        OpenFileOrText,

        /// <summary>The file is gone (moved, deleted, drive offline): the text opens in a new tab and the status bar says why.</summary>
        FileMissingOpenText,
    }

    public static class HistoryFile
    {
        /// <summary>
        /// The path of the document of a query tab when it is a file of the user: a full path of an existing file, not the temporary file a new window is
        /// backed by (<c>SQLQuery1.sql</c> in the temp folder). Empty otherwise.
        /// </summary>
        public static string OfDocument(string fullName, bool exists, string tempFolder)
        {
            if (string.IsNullOrEmpty(fullName) || !exists)
            {
                return string.Empty;
            }

            try
            {
                if (!Path.IsPathRooted(fullName))
                {
                    return string.Empty;
                }

                var temporary = string.Equals(Path.GetDirectoryName(fullName), (tempFolder ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(fullName).StartsWith("SQLQuery", StringComparison.OrdinalIgnoreCase);
                return temporary ? string.Empty : fullName;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// What to do with an entry: <paramref name="fileText"/> is the text of the file now (null when the file does not exist). Line endings and the
        /// end of the text do not count as a change (the editor may normalize them).
        /// </summary>
        public static HistoryOpenAction Decide(string file, string fileText, string entryText)
        {
            if (string.IsNullOrEmpty(file))
            {
                return HistoryOpenAction.OpenText;
            }

            if (fileText == null)
            {
                return HistoryOpenAction.FileMissingOpenText;
            }

            return Same(fileText, entryText) ? HistoryOpenAction.OpenFile : HistoryOpenAction.OpenFileOrText;
        }

        private static string Normalize(string text) => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

        private static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
    }
}
