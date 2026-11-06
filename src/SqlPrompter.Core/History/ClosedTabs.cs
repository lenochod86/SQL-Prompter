using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.History
{
    /// <summary>
    /// The query tabs that were closed lately, newest first, so that the last one can be opened again (#226). It follows the list of open tabs
    /// that the tab session writes: a tab that was in the previous list and is not in the new one has been closed, and its last known state is
    /// kept. A new, unsaved tab without text is not kept.
    /// </summary>
    public sealed class ClosedTabs
    {
        private readonly int capacity;
        private readonly List<SavedTab> closed = new List<SavedTab>();
        private Dictionary<string, SavedTab> previous = new Dictionary<string, SavedTab>(StringComparer.OrdinalIgnoreCase);

        public ClosedTabs(int capacity = 20)
        {
            this.capacity = Math.Max(1, capacity);
        }

        public int Count => closed.Count;

        /// <summary>The closed tabs, the one closed last first.</summary>
        public IReadOnlyList<SavedTab> Items => closed.ToList();

        /// <summary>Gives the tabs that are open now, by a key that stays the same while a tab is open (its document name); returns how many were found closed.</summary>
        public int Update(IEnumerable<KeyValuePair<string, SavedTab>> open)
        {
            var current = new Dictionary<string, SavedTab>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in open ?? Enumerable.Empty<KeyValuePair<string, SavedTab>>())
            {
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                {
                    current[pair.Key] = pair.Value;
                }
            }

            var found = 0;
            foreach (var pair in previous.Where(p => !current.ContainsKey(p.Key)))
            {
                var tab = pair.Value;
                if (string.IsNullOrEmpty(tab.File) && string.IsNullOrWhiteSpace(tab.Text))
                {
                    continue;
                }

                closed.Insert(0, tab);
                found++;
            }

            while (closed.Count > capacity)
            {
                closed.RemoveAt(closed.Count - 1);
            }

            previous = current;
            return found;
        }

        /// <summary>Takes the tab that was closed last, or null.</summary>
        public SavedTab Pop()
        {
            if (closed.Count == 0)
            {
                return null;
            }

            var tab = closed[0];
            closed.RemoveAt(0);
            return tab;
        }
    }
}
