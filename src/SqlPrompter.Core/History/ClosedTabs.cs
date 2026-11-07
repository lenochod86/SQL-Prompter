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
        private readonly Func<DateTime> clock;
        private readonly List<SavedTab> closed = new List<SavedTab>();
        private readonly List<DateTime> closedAt = new List<DateTime>();
        private Dictionary<string, SavedTab> previous = new Dictionary<string, SavedTab>(StringComparer.OrdinalIgnoreCase);

        public ClosedTabs(int capacity = 20, Func<DateTime> clock = null)
        {
            Capacity = Math.Max(1, capacity);
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>How many closed tabs are kept; the oldest go first (settings can change it while SSMS runs).</summary>
        public int Capacity { get; set; }

        /// <summary>A closed tab older than this is forgotten; zero keeps it as long as the list lives.</summary>
        public TimeSpan MaxAge { get; set; }

        public int Count
        {
            get
            {
                Prune();
                return closed.Count;
            }
        }

        /// <summary>The closed tabs, the one closed last first.</summary>
        public IReadOnlyList<SavedTab> Items
        {
            get
            {
                Prune();
                return closed.ToList();
            }
        }

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
                closedAt.Insert(0, clock());
                found++;
            }

            previous = current;
            Prune();
            return found;
        }

        private void Prune()
        {
            while (closed.Count > Math.Max(1, Capacity) || (MaxAge > TimeSpan.Zero && closed.Count > 0 && clock() - closedAt[closedAt.Count - 1] > MaxAge))
            {
                closed.RemoveAt(closed.Count - 1);
                closedAt.RemoveAt(closedAt.Count - 1);
            }
        }

        /// <summary>Takes the tab that was closed last, or null.</summary>
        public SavedTab Pop()
        {
            Prune();
            if (closed.Count == 0)
            {
                return null;
            }

            var tab = closed[0];
            closed.RemoveAt(0);
            closedAt.RemoveAt(0);
            return tab;
        }

        /// <summary>Takes a chosen closed tab out of the list (a pick from a list instead of the last one); false when it is not there.</summary>
        public bool Remove(SavedTab tab)
        {
            var index = closed.IndexOf(tab);
            if (index < 0)
            {
                return false;
            }

            closed.RemoveAt(index);
            closedAt.RemoveAt(index);
            return true;
        }
    }
}
