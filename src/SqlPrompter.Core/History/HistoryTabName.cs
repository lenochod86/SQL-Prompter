using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SqlPrompter.Core.History
{
    /// <summary>The names of query tabs in the history (#511): the temporary names SSMS gives to the files of unsaved tabs never reach the user.</summary>
    public static class HistoryTabName
    {
        /// <summary>What a row shows when the history does not know the name of the tab.</summary>
        public const string Unnamed = "(unnamed query)";

        /// <summary>Whether the name is the temporary file of an unsaved tab (<c>~vsr5khfdjz.sql</c>), which says nothing to the user.</summary>
        public static bool IsTemporary(string name)
        {
            var text = (name ?? string.Empty).Trim();
            return text.StartsWith("~vs", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The name a history row shows: the recorded name of the tab, or <see cref="Unnamed"/> when it is empty or temporary.</summary>
        public static string Display(string recorded)
        {
            var text = (recorded ?? string.Empty).Trim();
            return text.Length == 0 || IsTemporary(text) ? Unnamed : text;
        }

        /// <summary>The name a tab opened from the history is to get, or null when the history does not know a usable one (the tab keeps the name SSMS gives it).</summary>
        public static string ForReopening(string recorded)
        {
            var text = (recorded ?? string.Empty).Trim();
            return text.Length == 0 || IsTemporary(text) ? null : text;
        }

        /// <summary>
        /// The name of a tab from the caption of its window: the caption without the suffix SSMS adds (<c>SQLQuery2.sql - server.db (login (55))*</c>). Null when the
        /// caption does not end with the suffix or leaves nothing.
        /// </summary>
        public static string FromCaption(string caption, string editorCaption)
        {
            var text = (caption ?? string.Empty).TrimEnd('*', ' ');
            var suffix = (editorCaption ?? string.Empty).TrimEnd('*', ' ');
            if (suffix.Length == 0 || !text.EndsWith(suffix, StringComparison.Ordinal))
            {
                return null;
            }

            var name = text.Substring(0, text.Length - suffix.Length).Trim();
            return name.Length == 0 || IsTemporary(name) ? null : name;
        }

        /// <summary>
        /// A name that no open tab has: <paramref name="name"/> itself, or with a number in front of the extension, <c>SQLQuery2 (2).sql</c>, <c>SQLQuery2 (3).sql</c>
        /// (the lowest number from 2 that is free). Names are compared without regard to case.
        /// </summary>
        public static string Unique(string name, IEnumerable<string> taken)
        {
            var used = new HashSet<string>(taken ?? new string[0], StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(name))
            {
                return name;
            }

            var extension = Path.GetExtension(name);
            var stem = name.Substring(0, name.Length - extension.Length);
            for (var number = 2; ; number++)
            {
                var candidate = stem + " (" + number.ToString(CultureInfo.InvariantCulture) + ")" + extension;
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
    }

    /// <summary>What opening a new tab does (#511): the text goes into the tab whatever happened to the connection.</summary>
    public enum NewTabOutcome
    {
        /// <summary>The window exists and holds the text.</summary>
        Filled,

        /// <summary>No new window came out of it (nothing was inserted).</summary>
        NoWindow,

        /// <summary>The window exists but the text could not be inserted.</summary>
        NotFilled,
    }

    public static class NewTabOpener
    {
        /// <summary>
        /// Creates the window (<paramref name="create"/>, which also connects it: it may show the Connect dialog of SSMS, be cancelled, fail or throw), then inserts the text
        /// into the window when there is one (<paramref name="hasNewWindow"/>), always, whatever the connection did. A failure of <paramref name="create"/> is reported
        /// to <paramref name="failed"/> and ignored.
        /// </summary>
        public static NewTabOutcome Open(Action create, Func<bool> hasNewWindow, Func<bool> insertText, Action<Exception> failed)
        {
            try
            {
                create();
            }
            catch (Exception ex)
            {
                failed?.Invoke(ex);
            }

            if (!hasNewWindow())
            {
                return NewTabOutcome.NoWindow;
            }

            return insertText() ? NewTabOutcome.Filled : NewTabOutcome.NotFilled;
        }
    }
}
