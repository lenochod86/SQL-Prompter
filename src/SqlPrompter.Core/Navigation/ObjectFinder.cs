using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>One object found by the quick open window (#223).</summary>
    public sealed class FoundObject
    {
        public FoundObject(string schema, string name, string kind, MatchKind match)
        {
            Schema = schema;
            Name = name;
            Kind = kind;
            Match = match;
        }

        public string Schema { get; }

        public string Name { get; }

        /// <summary>Table, View, Procedure, Table function, Scalar function, Synonym or Trigger.</summary>
        public string Kind { get; }

        public MatchKind Match { get; }

        public string FullName => (string.IsNullOrEmpty(Schema) ? string.Empty : Schema + ".") + Name;
    }

    /// <summary>
    /// Searches the objects of the current database by a part of the name with the loose matching of <see cref="NameMatcher"/> (initials, words,
    /// characters in order): tables, views, procedures, functions, synonyms and triggers (never system objects). The list is made from the metadata
    /// that is loaded already, so it never waits for the database.
    /// </summary>
    public static class ObjectFinder
    {
        public static IReadOnlyList<FoundObject> Search(IDatabaseMetadata metadata, string typed, int max = 200)
        {
            var all = new List<FoundObject>();
            if (metadata == null)
            {
                return all;
            }

            foreach (var o in metadata.Objects ?? Array.Empty<DbObject>())
            {
                if (!o.IsSystem)
                {
                    all.Add(new FoundObject(o.Schema, o.Name, KindText(o.Kind), MatchKind.Prefix));
                }
            }

            foreach (var p in metadata.Procedures ?? Array.Empty<DbProcedure>())
            {
                all.Add(new FoundObject(p.Schema, p.Name, "Procedure", MatchKind.Prefix));
            }

            foreach (var t in metadata.Triggers ?? Array.Empty<DbTrigger>())
            {
                all.Add(new FoundObject(t.Schema, t.Name, "Trigger", MatchKind.Prefix));
            }

            typed = (typed ?? string.Empty).Trim();
            var result = new List<FoundObject>();
            foreach (var found in all)
            {
                // Typing schema.name filters by the full name, otherwise by the name alone.
                var text = typed.IndexOf('.') >= 0 ? found.FullName : found.Name;
                var match = NameMatcher.Match(text, typed);
                if (match != null)
                {
                    result.Add(new FoundObject(found.Schema, found.Name, found.Kind, match.Kind));
                }
            }

            return result
                .OrderBy(f => f.Match)
                .ThenBy(f => f.Name.Length)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Schema, StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .ToList();
        }

        private static string KindText(DbObjectKind kind)
        {
            switch (kind)
            {
                case DbObjectKind.Table: return "Table";
                case DbObjectKind.View: return "View";
                case DbObjectKind.TableFunction: return "Table function";
                case DbObjectKind.ScalarFunction: return "Scalar function";
                default: return "Synonym";
            }
        }
    }
}
