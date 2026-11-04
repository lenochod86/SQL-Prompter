using System;
using System.Globalization;
using System.Text.RegularExpressions;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Coloring
{
    /// <summary>Picks the environment of a connection from the coloring rules.</summary>
    public static class EnvironmentMatcher
    {
        /// <summary>
        /// The environment of the first matching rule, or null when coloring is off or nothing matches. Rules that name a database
        /// come before rules for a whole server, so that "server X" does not hide "server X, database Y".
        /// </summary>
        public static ColorEnvironment Find(ColorSettings settings, string server, string database)
        {
            if (settings == null || !settings.Enabled || string.IsNullOrEmpty(server))
            {
                return null;
            }

            foreach (var withDatabase in new[] { true, false })
            {
                foreach (var rule in settings.Rules)
                {
                    if (string.IsNullOrEmpty(rule.Database) == withDatabase
                        || !Matches(rule.Server, server) || !Matches(rule.Database, database ?? string.Empty))
                    {
                        continue;
                    }

                    foreach (var environment in settings.Environments)
                    {
                        if (string.Equals(environment.Name, rule.Environment, StringComparison.OrdinalIgnoreCase))
                        {
                            return environment;
                        }
                    }
                }
            }

            return null;
        }

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
