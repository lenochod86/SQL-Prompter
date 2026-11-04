using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// After JOIN: the tables that have a foreign key to a table of the query, or that a table of the query points to, with the
    /// whole clause <c>dbo.Orders AS o ON o.IdUser = u.IdUser</c>. A key over several columns gives all conditions joined by AND.
    /// </summary>
    internal static class JoinSuggestions
    {
        /// <summary>The group of these items: before everything else (the sort key puts "-01" before "00").</summary>
        public const int Group = -1;

        public static IEnumerable<Suggestion> Build(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            if (metadata == null || metadata.ForeignKeys.Count == 0)
            {
                yield break;
            }

            var byId = metadata.Objects.ToDictionary(o => o.Id);
            var quote = context.QuoteIdentifiers;
            var used = context.ReferenceNamesInUse().ToList();
            var sources = context.Sources
                .Where(s => s.ScopeLevel == 0 && s.Kind == TableSourceKind.Object)
                .Select(s => new { Source = s, Object = Readiness.Resolve(s, metadata) })
                .Where(x => x.Object != null && x.Object.Kind != DbObjectKind.Synonym)
                .ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var x in sources)
            {
                foreach (var fk in metadata.ForeignKeys)
                {
                    // The table of the query has the key: the new table is the one it points to.
                    if (fk.ChildObjectId == x.Object.Id && byId.TryGetValue(fk.ReferencedObjectId, out var referenced))
                    {
                        var pairs = fk.Columns.Select(c => new KeyValuePair<string, string>(c.ReferencedColumn, c.Column)).ToList();
                        var item = Item(context, options, x.Source, referenced, fk, pairs, quote, used);
                        if (item != null && seen.Add(item.InsertText))
                        {
                            yield return item;
                        }
                    }

                    // The new table has a key that points to the table of the query.
                    if (fk.ReferencedObjectId == x.Object.Id && byId.TryGetValue(fk.ChildObjectId, out var child))
                    {
                        var pairs = fk.Columns.Select(c => new KeyValuePair<string, string>(c.Column, c.ReferencedColumn)).ToList();
                        var item = Item(context, options, x.Source, child, fk, pairs, quote, used);
                        if (item != null && seen.Add(item.InsertText))
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        /// <param name="pairs">(column of the new table, column of the table in the query) for every column of the key.</param>
        private static Suggestion Item(
            CaretContext context,
            SuggestionOptions options,
            TableSource source,
            DbObject table,
            DbForeignKey fk,
            IReadOnlyList<KeyValuePair<string, string>> pairs,
            bool quote,
            IReadOnlyList<string> used)
        {
            if (table.IsSystem)
            {
                return null;
            }

            var alias = options.Aliases.AddAliases ? AliasGenerator.Generate(table.Name, used, options.Aliases) : null;
            var newReference = SqlIdentifier.Quote(alias ?? table.Name, quote);
            var existingReference = SqlIdentifier.Quote(source.ReferenceName, quote);
            var condition = string.Join(
                " AND ",
                pairs.Select(p => newReference + "." + SqlIdentifier.Quote(p.Key, quote) + " = " + existingReference + "." + SqlIdentifier.Quote(p.Value, quote)));
            var on = options.KeywordCase == KeywordCase.Lower ? "on" : "ON";
            var insert = (options.Aliases.QualifyObjectNames ? SqlIdentifier.Quote(table.Schema, quote) + "." : string.Empty) +
                SqlIdentifier.Quote(table.Name, quote) +
                (alias != null ? SuggestionBuilder.AliasDefinition(alias, options, quote) : string.Empty) +
                " " + on + " " + condition;
            var kind = table.Kind == DbObjectKind.View ? SuggestionKind.View : SuggestionKind.Table;
            return new Suggestion(kind, table.Name, insert, table.Name, fk.Name, "join by foreign key " + fk.Name + " " + on + " " + condition, Group, null, on + " " + condition);
        }
    }
}
