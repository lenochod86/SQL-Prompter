using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Completion
{
    /// <summary>Generates the alias inserted with a table, e.g. OrderItems → oi.</summary>
    public static class AliasGenerator
    {
        /// <param name="objectName">Name of the table or view without schema.</param>
        /// <param name="usedNames">Aliases and unaliased table names already in the query; the result differs from all of them.</param>
        public static string Generate(string objectName, IEnumerable<string> usedNames, AliasSettings settings)
        {
            var used = new HashSet<string>(usedNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var custom = settings.CustomAliases.FirstOrDefault(r => string.Equals(r.Name, objectName, StringComparison.OrdinalIgnoreCase));
            var baseAlias = custom != null ? custom.Alias : Initials(StripPrefix(objectName, settings.IgnoredPrefixes));
            if (custom == null && settings.CapitaliseGeneratedAliases)
            {
                baseAlias = baseAlias.ToUpperInvariant();
            }

            var alias = baseAlias;
            for (var n = 1; used.Contains(alias) || Keywords.CannotBeAlias(alias); n++)
            {
                alias = baseAlias + n;
            }

            return alias;
        }

        private static string StripPrefix(string name, IEnumerable<string> prefixes)
        {
            foreach (var prefix in prefixes.OrderByDescending(p => p.Length))
            {
                if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return name.Substring(prefix.Length);
                }
            }

            return name;
        }

        /// <summary>First letter of every word: words are split at separators, lower→upper changes and before the last capital of an acronym.</summary>
        private static string Initials(string name)
        {
            var result = new StringBuilder();
            var startsWord = true;
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (!char.IsLetterOrDigit(c))
                {
                    startsWord = true;
                    continue;
                }

                if (!startsWord && char.IsUpper(c))
                {
                    var previous = name[i - 1];
                    var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    startsWord = char.IsLower(previous) || (char.IsUpper(previous) && nextIsLower);
                }

                if (startsWord)
                {
                    if (char.IsLetter(c))
                    {
                        result.Append(char.ToLowerInvariant(c));
                    }

                    startsWord = false;
                }
            }

            return result.Length > 0 ? result.ToString() : "t";
        }
    }
}
