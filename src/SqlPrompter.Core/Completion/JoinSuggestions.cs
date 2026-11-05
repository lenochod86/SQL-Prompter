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
        /// <summary>The group of these items: before everything else (negative groups sort first).</summary>
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

        /// <summary>Groups of the condition items: before every other suggestion, whole keys first.</summary>
        private const int GroupKey = -4;
        private const int GroupPartialKey = -3;
        private const int GroupKeyNamed = -2;
        private const int GroupNamed = -1;

        /// <param name="pairs">(column of the new table, column of the other table).</param>
        private static string Condition(IEnumerable<KeyValuePair<string, string>> pairs, string newReference, string otherReference, bool newTableOnLeft, bool quote) =>
            string.Join(
                " AND ",
                pairs.Select(p =>
                {
                    var left = newReference + "." + SqlIdentifier.Quote(p.Key, quote);
                    var right = otherReference + "." + SqlIdentifier.Quote(p.Value, quote);
                    return newTableOnLeft ? left + " = " + right : right + " = " + left;
                }));

        /// <summary>
        /// At the start of a condition after ON: conditions that link the table of the JOIN to every table before it in the query,
        /// by foreign key (the whole key, and with the option every single column of a multi-column key), then by identical column names.
        /// </summary>
        public static IEnumerable<Suggestion> Conditions(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            var target = context.JoinTarget;
            if (target == null || metadata == null)
            {
                yield break;
            }

            var targetObject = Readiness.Resolve(target, metadata);
            if (targetObject == null || targetObject.Kind == DbObjectKind.Synonym)
            {
                yield break;
            }

            var quote = context.QuoteIdentifiers;
            var targetReference = SqlIdentifier.Quote(target.ReferenceName, quote);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetColumns = metadata.GetColumns(targetObject);
            var targetPrimary = new HashSet<string>(metadata.GetPrimaryKey(targetObject), StringComparer.OrdinalIgnoreCase);
            var targetForeign = new HashSet<string>(
                metadata.ForeignKeys.Where(k => k.ChildObjectId == targetObject.Id).SelectMany(k => k.Columns.Select(c => c.Column)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var other in context.Sources.Where(s => s.ScopeLevel == 0 && s.Kind == TableSourceKind.Object && s.TokenIndex >= 0 && s.TokenIndex < target.TokenIndex))
            {
                var otherObject = Readiness.Resolve(other, metadata);
                if (otherObject == null || otherObject.Kind == DbObjectKind.Synonym)
                {
                    continue;
                }

                var otherReference = SqlIdentifier.Quote(other.ReferenceName, quote);
                var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var fk in metadata.ForeignKeys)
                {
                    // A key of a table to itself (an employee and the manager) fits in both directions; any other key in one.
                    var directions = new List<IReadOnlyList<KeyValuePair<string, string>>>();
                    if (fk.ChildObjectId == targetObject.Id && fk.ReferencedObjectId == otherObject.Id)
                    {
                        directions.Add(fk.Columns.Select(c => new KeyValuePair<string, string>(c.Column, c.ReferencedColumn)).ToList());
                    }

                    if (fk.ChildObjectId == otherObject.Id && fk.ReferencedObjectId == targetObject.Id)
                    {
                        directions.Add(fk.Columns.Select(c => new KeyValuePair<string, string>(c.ReferencedColumn, c.Column)).ToList());
                    }

                    foreach (var pairs in directions)
                    {
                        foreach (var p in pairs)
                        {
                            covered.Add(p.Key + "|" + p.Value);
                        }

                        var whole = Condition(pairs, targetReference, otherReference, options.Joins.NewTableOnLeft, quote);
                        if (seen.Add(whole))
                        {
                            yield return ConditionItem(whole, fk.Name, "join by foreign key " + fk.Name, GroupKey);
                        }

                        if (options.Joins.IncludePartialKeyConditions && pairs.Count > 1)
                        {
                            foreach (var p in pairs)
                            {
                                var single = Condition(new[] { p }, targetReference, otherReference, options.Joins.NewTableOnLeft, quote);
                                if (seen.Add(single))
                                {
                                    yield return ConditionItem(single, fk.Name, "one column of the foreign key " + fk.Name, GroupPartialKey);
                                }
                            }
                        }
                    }
                }

                var otherColumns = metadata.GetColumns(otherObject);
                if (!options.Joins.NameMatchedConditions || targetColumns == null || otherColumns == null)
                {
                    continue;
                }

                var otherPrimary = new HashSet<string>(metadata.GetPrimaryKey(otherObject), StringComparer.OrdinalIgnoreCase);
                var otherForeign = new HashSet<string>(
                    metadata.ForeignKeys.Where(k => k.ChildObjectId == otherObject.Id).SelectMany(k => k.Columns.Select(c => c.Column)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var column in targetColumns)
                {
                    var match = otherColumns.FirstOrDefault(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase));
                    if (match == null || covered.Contains(column.Name + "|" + match.Name))
                    {
                        continue;
                    }

                    if (options.Joins.RequireSameTypeForNameMatches && !string.Equals(column.TypeName, match.TypeName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var keyColumn = targetPrimary.Contains(column.Name) || targetForeign.Contains(column.Name) ||
                        otherPrimary.Contains(match.Name) || otherForeign.Contains(match.Name);
                    var text = Condition(new[] { new KeyValuePair<string, string>(column.Name, match.Name) }, targetReference, otherReference, options.Joins.NewTableOnLeft, quote);
                    if (seen.Add(text))
                    {
                        yield return ConditionItem(text, "same name", "columns with the same name", keyColumn ? GroupKeyNamed : GroupNamed);
                    }
                }
            }
        }

        private static Suggestion ConditionItem(string condition, string suffix, string description, int group) =>
            new Suggestion(SuggestionKind.JoinCondition, condition, condition, condition, suffix, description + ": " + condition, group);

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
            var condition = Condition(pairs, newReference, existingReference, options.Joins.NewTableOnLeft, quote);
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
