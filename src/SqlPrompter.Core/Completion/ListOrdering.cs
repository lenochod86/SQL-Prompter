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
        /// The items whose filter text contains the typed text (case-insensitive), those that start with it first; the order of the input is kept
        /// within both parts. Everything for no typed text.
        /// </summary>
        public static IReadOnlyList<T> Filter<T>(IEnumerable<T> items, string typed, Func<T, string> filterText)
        {
            if (string.IsNullOrEmpty(typed))
            {
                return items.ToList();
            }

            var matches = items
                .Select(item => new { item, text = filterText(item) ?? string.Empty })
                .Where(x => x.text.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            return matches.Where(x => x.text.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).Concat(matches.Where(x => !x.text.StartsWith(typed, StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.item)
                .ToList();
        }

        /// <summary>True when the item matches the typed text (also for none).</summary>
        public static bool Matches(string filterText, string typed) =>
            string.IsNullOrEmpty(typed) || (filterText ?? string.Empty).IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
