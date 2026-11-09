using System;
using System.Linq;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Coloring
{
    /// <summary>Changes the coloring rules from a window: "this window's server and database get this environment".</summary>
    public static class ColorRuleEditor
    {
        /// <summary>
        /// A copy of <paramref name="settings"/> in which the rule with exactly this server and database (compared case-insensitively, as
        /// <see cref="EnvironmentMatcher"/> does) has <paramref name="environment"/>; the rule is added when there is none. The other fields of
        /// an existing rule stay as they are, and a second rule for the same server and database is never created.
        /// </summary>
        public static ColorSettings SetEnvironment(ColorSettings settings, string server, string database, string environment)
        {
            var copy = settings.Clone();
            server = (server ?? string.Empty).Trim();
            database = (database ?? string.Empty).Trim();
            var rule = copy.Rules.FirstOrDefault(r => Same(r.Server, server) && Same(r.Database, database));
            if (rule == null)
            {
                copy.Rules.Add(new ColorRule { Server = server, Database = database, Environment = environment });
            }
            else
            {
                rule.Environment = environment;
            }

            return copy;
        }

        private static bool Same(string a, string b) => string.Equals((a ?? string.Empty).Trim(), b, StringComparison.OrdinalIgnoreCase);
    }
}
