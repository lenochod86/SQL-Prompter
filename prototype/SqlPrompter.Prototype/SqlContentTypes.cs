using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Prototype
{
    /// <summary>
    /// Název typu obsahu SQL editoru v SSMS 22 neznáme, proto se rozšíření registruje
    /// pro "any" a SQL editor pozná až za běhu. Výsledek je odpověď na otázku 1.
    /// </summary>
    internal static class SqlContentTypes
    {
        /// <summary>Přesný název typu obsahu lze vynutit proměnnou prostředí.</summary>
        public const string OverrideVariable = "SQLPROMPTER_CONTENT_TYPE";

        public static bool IsSql(IContentType contentType)
        {
            if (contentType == null)
            {
                return false;
            }

            var forced = Environment.GetEnvironmentVariable(OverrideVariable);
            if (!string.IsNullOrWhiteSpace(forced))
            {
                return contentType.IsOfType(forced.Trim());
            }

            return SelfAndBaseTypes(contentType)
                .Any(t => t.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Např. "SQL Server Tools -> code -> text".</summary>
        public static string Describe(IContentType contentType)
        {
            if (contentType == null)
            {
                return "(null)";
            }

            return Describe(contentType, new HashSet<IContentType>());
        }

        private static string Describe(IContentType contentType, HashSet<IContentType> visited)
        {
            if (!visited.Add(contentType))
            {
                return "\"" + contentType.TypeName + "\"";
            }

            var bases = contentType.BaseTypes.ToList();
            var self = "\"" + contentType.TypeName + "\"";
            if (bases.Count == 0)
            {
                return self;
            }

            return self + " -> (" + string.Join(" | ", bases.Select(b => Describe(b, visited))) + ")";
        }

        private static IEnumerable<IContentType> SelfAndBaseTypes(IContentType contentType)
        {
            var visited = new HashSet<IContentType>();
            var stack = new Stack<IContentType>();
            stack.Push(contentType);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                yield return current;
                foreach (var b in current.BaseTypes)
                {
                    stack.Push(b);
                }
            }
        }
    }
}
