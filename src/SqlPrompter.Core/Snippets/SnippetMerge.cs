using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Snippets
{
    /// <summary>Combines the personal snippets with those of the shared folders (#228).</summary>
    public static class SnippetMerge
    {
        /// <summary>
        /// The personal snippets and then the shared ones, marked with <see cref="Snippet.IsShared"/>. A snippet whose prefix exists in the personal
        /// folder, or in a shared folder that comes earlier, is left out. The result is sorted by prefix.
        /// </summary>
        public static List<Snippet> Merge(IEnumerable<Snippet> personal, IEnumerable<IEnumerable<Snippet>> sharedFolders)
        {
            var result = new List<Snippet>(personal ?? Enumerable.Empty<Snippet>());
            var prefixes = new HashSet<string>(result.Select(s => s.Prefix), StringComparer.OrdinalIgnoreCase);
            foreach (var folder in sharedFolders ?? Enumerable.Empty<IEnumerable<Snippet>>())
            {
                var taken = new List<string>();
                foreach (var snippet in folder)
                {
                    if (prefixes.Contains(snippet.Prefix))
                    {
                        continue;
                    }

                    snippet.IsShared = true;
                    result.Add(snippet);
                    taken.Add(snippet.Prefix);
                }

                // Two snippets of the same shared folder with one prefix are both kept; only later folders lose to it.
                foreach (var prefix in taken)
                {
                    prefixes.Add(prefix);
                }
            }

            result.Sort((a, b) => string.Compare(a.Prefix, b.Prefix, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
}
