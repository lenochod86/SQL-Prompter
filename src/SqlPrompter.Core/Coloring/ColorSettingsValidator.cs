using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Coloring
{
    /// <summary>Checks the coloring settings the editor in the options builds, and says in words what is wrong.</summary>
    public static class ColorSettingsValidator
    {
        public static IReadOnlyList<string> Validate(ColorSettings settings)
        {
            var problems = new List<string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < settings.Environments.Count; i++)
            {
                var environment = settings.Environments[i];
                var name = (environment.Name ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    problems.Add("Environment " + (i + 1) + " has no name.");
                }
                else if (!names.Add(name))
                {
                    problems.Add("The environment name \"" + name + "\" is used more than once.");
                }

                if (!EnvironmentMatcher.TryParseColor(environment.Color ?? string.Empty, out _, out _, out _))
                {
                    problems.Add("The environment \"" + (name.Length == 0 ? "#" + (i + 1) : name) + "\" has no valid color.");
                }
            }

            for (var i = 0; i < settings.Rules.Count; i++)
            {
                var rule = settings.Rules[i];
                if (string.IsNullOrWhiteSpace(rule.Server))
                {
                    problems.Add("Rule " + (i + 1) + " has no server (use * for every server).");
                }

                var environment = (rule.Environment ?? string.Empty).Trim();
                if (environment.Length == 0)
                {
                    problems.Add("Rule " + (i + 1) + " has no environment.");
                }
                else if (!names.Contains(environment))
                {
                    problems.Add("Rule " + (i + 1) + " uses the environment \"" + environment + "\", which does not exist.");
                }
            }

            return problems;
        }

        /// <summary>A copy of the settings that keeps only the environments some rule uses (the editor creates environments from a rule, so an unused one is a leftover).</summary>
        public static ColorSettings WithoutUnusedEnvironments(ColorSettings settings)
        {
            var copy = settings.Clone();
            var used = new HashSet<string>(copy.Rules.Select(r => (r.Environment ?? string.Empty).Trim()), StringComparer.OrdinalIgnoreCase);
            copy.Environments.RemoveAll(e => !used.Contains((e.Name ?? string.Empty).Trim()));
            return copy;
        }
        /// <summary>The color as <c>#RRGGBB</c> in upper case, or null when the text is not a color.</summary>
        public static string Normalize(string color) =>
            color != null && EnvironmentMatcher.TryParseColor(color, out _, out _, out _) ? "#" + color.Trim().TrimStart('#').ToUpperInvariant() : null;
    }
}
