using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SqlPrompter.Core.Snippets
{
    /// <summary>Text forms of a snippet for the editing window and for creating a snippet from a selection.</summary>
    public static class SnippetText
    {
        private static readonly Regex SelectionPlaceholder = new Regex(@"\$(SELECTEDTEXT|SELECTIONSTART|SELECTIONEND)\$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The placeholders as one line each: <c>name = default value</c>.</summary>
        public static string PlaceholdersToText(IEnumerable<SnippetPlaceholder> placeholders) =>
            string.Join(Environment.NewLine, (placeholders ?? Enumerable.Empty<SnippetPlaceholder>()).Select(p => p.Name + " = " + p.DefaultValue));

        /// <summary>Reads <c>name = default value</c> lines; lines without a name or with a repeated name are left out.</summary>
        public static List<SnippetPlaceholder> ParsePlaceholders(string text)
        {
            var result = new List<SnippetPlaceholder>();
            foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                var index = line.IndexOf('=');
                var name = (index < 0 ? line : line.Substring(0, index)).Trim();
                if (name.Length == 0 || result.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(new SnippetPlaceholder { Name = name, DefaultValue = index < 0 ? string.Empty : line.Substring(index + 1).Trim() });
            }

            return result;
        }

        /// <summary>True if the snippet wraps what is selected: it uses <c>$SELECTEDTEXT$</c> or the selection markers.</summary>
        public static bool UsesSelection(Snippet snippet) => SelectionPlaceholder.IsMatch(snippet.Body ?? string.Empty);

        /// <summary>
        /// The body of a new snippet made of selected text: dollar signs are doubled so that they stay literal, and the caret ends
        /// at the end of the snippet.
        /// </summary>
        public static string BodyFromSelection(string selectedText) => (selectedText ?? string.Empty).Replace("$", "$$");

        /// <summary>Names of the custom fields used in a body (built-ins and <c>$$</c> excluded), in order of appearance.</summary>
        public static IReadOnlyList<string> FieldNames(string body) =>
            SnippetExpander.Expand(new Snippet { Body = body ?? string.Empty }, new SnippetContext()).Fields.Select(f => f.Name).ToList();
    }
}
