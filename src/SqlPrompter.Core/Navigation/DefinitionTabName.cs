using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>
    /// The name of the tab a definition is opened in (#437): <c>schema.object</c> and a running number, <c>Crm.p_Contract1.sql</c>. The number is always there, also for
    /// the first tab, so these tabs are told apart from a regular file that has the name of the object (<c>Crm.p_Contract.sql</c>); the lowest number not taken by an
    /// open tab of the same object is used, so a number comes back after its tab is closed.
    /// </summary>
    public static class DefinitionTabName
    {
        private static readonly char[] Invalid = { '<', '>', ':', '"', '/', '\\', '|', '?', '*', '[', ']' };

        /// <summary>The name without the number and the extension: the schema and the object (without brackets), characters a file name cannot have replaced by an underscore.</summary>
        public static string Stem(string schema, string name)
        {
            var text = string.IsNullOrWhiteSpace(schema) ? (name ?? string.Empty) : schema.Trim() + "." + (name ?? string.Empty);
            var builder = new StringBuilder(text.Length);
            foreach (var c in text.Trim())
            {
                // Brackets are the quoting of a name, not a part of it.
                if (c == '[' || c == ']')
                {
                    continue;
                }

                builder.Append(c < ' ' || Array.IndexOf(Invalid, c) >= 0 ? '_' : c);
            }

            // A trailing dot or space is not allowed in a file name either.
            return builder.ToString().TrimEnd('.', ' ');
        }

        /// <summary>The file name the Save As dialog proposes for a tab with this name: characters a file name cannot have are replaced, and <c>.sql</c> is added when the name does not end with it.</summary>
        public static string FileNameOf(string tabName)
        {
            var text = (tabName ?? string.Empty).Trim();
            var builder = new StringBuilder(text.Length + 4);
            foreach (var c in text)
            {
                builder.Append(c < ' ' || Array.IndexOf(Invalid, c) >= 0 ? '_' : c);
            }

            var name = builder.ToString().TrimEnd('.', ' ');
            return name.Length == 0 ? string.Empty : (name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ? name : name + ".sql");
        }

        public static string Format(string stem, int number) => stem + number.ToString(CultureInfo.InvariantCulture) + ".sql";

        /// <summary>The name for the next tab of the object: the lowest number (from 1) that no name of <paramref name="takenNames"/> (the names of the open tabs) has.</summary>
        public static string Next(string schema, string name, IEnumerable<string> takenNames)
        {
            var stem = Stem(schema, name);
            var pattern = new Regex("^" + Regex.Escape(stem) + @"(\d+)\.sql$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var used = new HashSet<int>();
            foreach (var taken in takenNames ?? new string[0])
            {
                var match = taken == null ? null : pattern.Match(taken);
                if (match != null && match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    used.Add(number);
                }
            }

            var next = 1;
            while (used.Contains(next))
            {
                next++;
            }

            return Format(stem, next);
        }
    }
}
