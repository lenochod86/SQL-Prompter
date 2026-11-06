using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SqlPrompter.Core.Completion
{
    /// <summary>The text of the descriptions that people keep in the database (extended property MS_Description) for the area next to the list (#206).</summary>
    public static class DescriptionText
    {
        public const int MaxObjectLength = 600;
        public const int MaxColumnLength = 120;
        public const int MaxColumns = 12;

        /// <summary>The description of the object, then one line per described column (name: text); null when there is nothing to show. Long texts are shortened.</summary>
        public static string Format(string objectDescription, IReadOnlyList<KeyValuePair<string, string>> columns)
        {
            var text = new StringBuilder();
            var main = Clean(objectDescription, MaxObjectLength);
            if (main.Length > 0)
            {
                text.Append(main);
            }

            var described = (columns ?? new KeyValuePair<string, string>[0]).Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToList();
            foreach (var column in described.Take(MaxColumns))
            {
                if (text.Length > 0)
                {
                    text.Append('\n');
                }

                text.Append(column.Key).Append(": ").Append(Clean(column.Value, MaxColumnLength));
            }

            if (described.Count > MaxColumns)
            {
                text.Append("\n... and ").Append(described.Count - MaxColumns).Append(" more described columns");
            }

            return text.Length == 0 ? null : text.ToString();
        }

        private static string Clean(string value, int max)
        {
            var text = (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (max < 200)
            {
                // Columns are shown on one line each.
                text = string.Join(" ", text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
            }

            return text.Length > max ? text.Substring(0, max).TrimEnd() + "..." : text;
        }
    }
}
