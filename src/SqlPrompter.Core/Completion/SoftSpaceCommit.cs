using System;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Where the editor selects only softly (after a table source in FROM, where an alias may be typed) it does not commit on typed
    /// characters. Space must still commit a keyword there (<c>WHE</c> + Space gives <c>WHERE </c>), but not an alias the user types:
    /// the typed word has to be a keyword (or the start of one, at least <see cref="MinimumPrefix"/> letters, so that a short alias
    /// such as <c>j</c> or <c>or</c> is kept as typed).
    /// </summary>
    public static class SoftSpaceCommit
    {
        public const int MinimumPrefix = 3;

        /// <summary>True if Space after <paramref name="typed"/> inserts the keyword <paramref name="keyword"/>.</summary>
        public static bool Commits(string typed, string keyword)
        {
            if (string.IsNullOrEmpty(typed) || string.IsNullOrEmpty(keyword) || keyword.Length < typed.Length)
            {
                return false;
            }

            if (!keyword.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return typed.Length == keyword.Length || typed.Length >= MinimumPrefix;
        }
    }
}
