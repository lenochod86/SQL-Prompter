using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Coloring
{
    /// <summary>Picks the environment of a connection from the coloring rules.</summary>
    public static class EnvironmentMatcher
    {
        /// <summary>
        /// The environment of the most specific matching rule, or null when coloring is off or nothing matches. The order of the rules does not
        /// matter: a rule with a database beats a rule for the whole server, then a rule without wildcards beats one with wildcards, then the
        /// rule with more literal characters wins; what is still equal is decided by the names, so the result is always the same.
        /// </summary>
        public static ColorEnvironment Find(ColorSettings settings, string server, string database) => FindMatch(settings, server, database, out _);

        /// <summary>The rule that <see cref="Find"/> picks, or null.</summary>
        public static ColorRule FindRule(ColorSettings settings, string server, string database)
        {
            FindMatch(settings, server, database, out var rule);
            return rule;
        }

        private static ColorEnvironment FindMatch(ColorSettings settings, string server, string database, out ColorRule matched)
        {
            matched = null;
            if (settings == null || !settings.Enabled || string.IsNullOrEmpty(server))
            {
                return null;
            }

            ColorEnvironment best = null;
            ColorRule bestRule = null;
            foreach (var rule in settings.Rules)
            {
                if (!Matches(rule.Server, server) || !Matches(rule.Database, database ?? string.Empty))
                {
                    continue;
                }

                var environment = settings.Environments.FirstOrDefault(e => string.Equals(e.Name, rule.Environment, StringComparison.OrdinalIgnoreCase));
                if (environment != null && (bestRule == null || Compare(rule, bestRule) < 0))
                {
                    best = environment;
                    bestRule = rule;
                }
            }

            matched = bestRule;
            return best;
        }

        /// <summary>Negative when <paramref name="a"/> is the more specific rule.</summary>
        private static int Compare(ColorRule a, ColorRule b)
        {
            var result = HasDatabase(b).CompareTo(HasDatabase(a));
            if (result == 0)
            {
                result = HasWildcards(a).CompareTo(HasWildcards(b));
            }

            if (result == 0)
            {
                result = LiteralLength(b).CompareTo(LiteralLength(a));
            }

            if (result == 0)
            {
                result = string.Compare(a.Environment, b.Environment, StringComparison.OrdinalIgnoreCase);
            }

            if (result == 0)
            {
                result = string.Compare(a.Server, b.Server, StringComparison.OrdinalIgnoreCase);
            }

            return result == 0 ? string.Compare(a.Database, b.Database, StringComparison.OrdinalIgnoreCase) : result;
        }

        private static bool HasDatabase(ColorRule rule) => !string.IsNullOrEmpty(rule.Database);

        private static bool HasWildcards(ColorRule rule) => ((rule.Server ?? string.Empty) + (rule.Database ?? string.Empty)).IndexOfAny(new[] { '*', '?' }) >= 0;

        private static int LiteralLength(ColorRule rule) => ((rule.Server ?? string.Empty).Trim() + (rule.Database ?? string.Empty).Trim()).Count(c => c != '*' && c != '?');
        /// <summary>Case-insensitive match with * (any text) and ? (one character); an empty pattern matches everything.</summary>
        public static bool Matches(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            var regex = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value ?? string.Empty, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }

        /// <summary>Parses #RRGGBB (the # is optional).</summary>
        public static bool TryParseColor(string text, out byte red, out byte green, out byte blue)
        {
            red = 0;
            green = 0;
            blue = 0;
            var hex = (text ?? string.Empty).Trim().TrimStart('#');
            return hex.Length == 6
                && byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red)
                && byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green)
                && byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue);
        }
    }
}
