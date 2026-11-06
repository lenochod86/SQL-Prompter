using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// The order and the filter of a completion list, in one place for the lists SQL Prompter runs itself (the own list and its picker) and for tests.
    /// The editor's session sorts by the sort text of its items (ordinal) and filters on its own; the rules here are the same, so that both look alike.
    /// </summary>
    public static class ListOrdering
    {
        /// <summary>Items in the order of their sort text (ordinal, stable): group first, then usage and name, see <see cref="Suggestion.RankedSortText"/>.</summary>
        public static IReadOnlyList<T> Sort<T>(IEnumerable<T> items, Func<T, string> sortText) =>
            items.Select((item, index) => new { item, index, key = sortText(item) ?? string.Empty })
                .OrderBy(x => x.key, StringComparer.Ordinal)
                .ThenBy(x => x.index)
                .Select(x => x.item)
                .ToList();

        /// <summary>
        /// The items that match the typed text (see <see cref="NameMatcher"/>): exact, then prefix, then the humps and words of the name, then the substring,
        /// then the characters in order; the order of the input is kept within each kind. Everything for no typed text.
        /// </summary>
        /// <param name="loose">The humps, words and characters in order match too; without it only the prefix and the substring.</param>
        public static IReadOnlyList<T> Filter<T>(IEnumerable<T> items, string typed, Func<T, string> filterText, bool loose = true)
        {
            if (string.IsNullOrEmpty(typed))
            {
                return items.ToList();
            }

            return items
                .Select((item, index) => new { item, index, match = NameMatcher.Match(filterText(item), typed, loose) })
                .Where(x => x.match != null)
                .OrderBy(x => (int)x.match.Kind)
                .ThenBy(x => x.index)
                .Select(x => x.item)
                .ToList();
        }

        /// <summary>True when the item matches the typed text (also for none).</summary>
        public static bool Matches(string filterText, string typed, bool loose = true) =>
            string.IsNullOrEmpty(typed) || NameMatcher.Match(filterText, typed, loose) != null;
    }
}
