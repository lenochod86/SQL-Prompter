using System;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// A keyword item of several words (<c>IS NULL</c>, <c>ORDER BY</c>, <c>LEFT JOIN</c>) while the user types its first words:
    /// the space after a complete first word does not insert the item, it is typed, and the list is built again for the new position of the
    /// caret (what can follow <c>IS</c>, <c>ORDER</c>, ...), see #444.
    /// </summary>
    public static class MultiWordKeywords
    {
        /// <summary>True if <paramref name="typed"/> is the complete leading word(s) of <paramref name="item"/> and more words follow.</summary>
        public static bool IsLeadingWords(string typed, string item)
        {
            if (string.IsNullOrEmpty(typed) || item == null || item.Length <= typed.Length + 1)
            {
                return false;
            }

            return item.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && item[typed.Length] == ' ' && char.IsLetter(item[typed.Length + 1]);
        }
    }
}
