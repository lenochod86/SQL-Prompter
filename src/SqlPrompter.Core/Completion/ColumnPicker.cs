using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Completion
{
    /// <summary>One column of the column picker: where it comes from, what it is and the text that is inserted for it.</summary>
    public sealed class PickerColumn
    {
        internal PickerColumn(string group, string owner, DbColumn column, KeyKind key, string insertText, int tableIndex)
        {
            Group = group;
            Owner = owner;
            Column = column;
            Key = key;
            InsertText = insertText;
            TableIndex = tableIndex;
        }

        /// <summary>The alias or name of the source, the heading of the group.</summary>
        public string Group { get; }

        /// <summary>The table or view the column belongs to (shown on the right).</summary>
        public string Owner { get; }

        public DbColumn Column { get; }

        public KeyKind Key { get; }

        /// <summary>The column as it is inserted, qualified with the alias when the query has several sources.</summary>
        public string InsertText { get; }

        /// <summary>The position in the column order of the table.</summary>
        public int TableIndex { get; }

        public string Name => Column.Name;
    }

    /// <summary>
    /// The columns that the column picker offers at the caret: the columns of all sources of the select list, GROUP BY or ORDER BY (grouped by
    /// source), or the insertable columns of the target of INSERT INTO t (...) that are not in the list yet. Also the items of the select list
    /// that a GROUP BY has to repeat. The text to insert for a choice is laid out by the formatting style.
    /// </summary>
    public sealed class ColumnPicker
    {
        private ColumnPicker(ColumnClause clause, IReadOnlyList<PickerColumn> columns, IReadOnlyList<string> nonAggregated)
        {
            Clause = clause;
            Columns = columns;
            NonAggregated = nonAggregated;
        }

        public ColumnClause Clause { get; }

        /// <summary>The columns in the order of the sources and of the columns of each table.</summary>
        public IReadOnlyList<PickerColumn> Columns { get; }

        /// <summary>For GROUP BY: the select-list items that are not aggregates, as written.</summary>
        public IReadOnlyList<string> NonAggregated { get; }

        /// <summary>The columns in display order: by source, and within a source in table order or alphabetically.</summary>
        public IReadOnlyList<PickerColumn> Ordered(bool tableOrder) =>
            tableOrder
                ? Columns
                : Columns.GroupBy(c => c.Group, StringComparer.OrdinalIgnoreCase).SelectMany(g => g.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        /// <summary>The picker for the caret, or null when the caret is not at a place where columns are picked or the columns are not known (yet).</summary>
        public static ColumnPicker TryBuild(string batch, int caret, CaretContext context, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options)
        {
            if (batch == null || context == null || metadata == null || (context.Kind != ContextKind.Expression && context.Kind != ContextKind.InsertColumns) || context.Qualifier.Count > 0)
            {
                return null;
            }

            options = options ?? SuggestionOptions.Default;
            script = script ?? ScriptModel.Empty;
            var clause = context.Kind == ContextKind.InsertColumns ? ColumnClause.InsertColumns : ColumnClauses.At(batch, caret);
            if (clause == ColumnClause.UpdateSet && context.Kind != ContextKind.Expression)
            {
                return null;
            }
            if (clause == ColumnClause.None)
            {
                return null;
            }

            var quote = context.QuoteIdentifiers;
            var columns = new List<PickerColumn>();
            if (clause == ColumnClause.InsertColumns)
            {
                var source = context.Sources.FirstOrDefault();
                var dbObject = source == null ? null : Readiness.Resolve(source, metadata);
                var all = dbObject == null ? null : metadata.GetColumns(dbObject);
                if (all == null)
                {
                    return null;
                }

                var present = new HashSet<string>(context.IndexColumns ? context.ExcludedColumns : ColumnClauses.ExistingInsertColumns(batch, caret), StringComparer.OrdinalIgnoreCase);
                var insertable = context.IndexColumns ? all : InsertBuilder.InsertableColumns(all);
                for (var i = 0; i < insertable.Count; i++)
                {
                    if (!present.Contains(insertable[i].Name))
                    {
                        columns.Add(new PickerColumn(dbObject.Name, dbObject.ToString(), insertable[i], KeyKind.None, SqlIdentifier.Quote(insertable[i].Name, quote), i));
                    }
                }
            }
            else if (clause == ColumnClause.UpdateSet)
            {
                var source = context.Sources.FirstOrDefault();
                var x = source == null ? null : SuggestionBuilder.ResolveColumns(source, metadata, script, options);
                if (x == null)
                {
                    return null;
                }

                var assigned = new HashSet<string>(ColumnClauses.ExistingAssignments(batch, caret), StringComparer.OrdinalIgnoreCase);
                var reference = x.Source.ReferenceName ?? string.Empty;
                for (var i = 0; i < x.Columns.Count; i++)
                {
                    var column = x.Columns[i];
                    if (!column.IsInsertable || assigned.Contains(column.Name))
                    {
                        continue;
                    }

                    var value = SqlDefaults.ValueFor(column.TypeName, column.IsNullable);
                    columns.Add(new PickerColumn(reference, x.Owner, column, x.KeyOf(column), SqlIdentifier.Quote(column.Name, quote) + " = " + value, i));
                }
            }
            else
            {
                var resolved = context.Sources
                    .Where(s => s.ScopeLevel == 0)
                    .Select(s => SuggestionBuilder.ResolveColumns(s, metadata, script, options))
                    .Where(x => x != null)
                    .ToList();
                var qualifyAll = resolved.Count > 1 || options.Aliases.QualifyColumnsWithAliases;
                foreach (var x in resolved)
                {
                    var reference = x.Source.ReferenceName ?? string.Empty;
                    for (var i = 0; i < x.Columns.Count; i++)
                    {
                        var name = SqlIdentifier.Quote(x.Columns[i].Name, quote);
                        var insert = qualifyAll && reference.Length > 0 ? SqlIdentifier.Quote(reference, quote) + "." + name : name;
                        columns.Add(new PickerColumn(reference, x.Owner, x.Columns[i], x.KeyOf(x.Columns[i]), insert, i));
                    }
                }
            }

            if (columns.Count == 0)
            {
                return null;
            }

            return new ColumnPicker(clause, columns, clause == ColumnClause.GroupBy ? ColumnClauses.NonAggregatedItems(batch, caret) : new string[0]);
        }

        /// <summary>The text to insert for the chosen columns: a list laid out by the formatting style (the same layout as an expanded star).</summary>
        public string InsertText(IEnumerable<PickerColumn> chosen, bool tableOrder, SuggestionOptions options)
        {
            var ordered = Ordered(tableOrder);
            var picked = new HashSet<PickerColumn>(chosen);
            var texts = ordered.Where(picked.Contains).Select(c => c.InsertText).ToList();
            return Join(texts, options);
        }

        /// <summary>The text for the item "All non-aggregated columns".</summary>
        public string NonAggregatedText(SuggestionOptions options) => Join(NonAggregated, options);

        private static string Join(IReadOnlyList<string> texts, SuggestionOptions options)
        {
            options = options ?? SuggestionOptions.Default;
            return texts.Count == 0 ? string.Empty : FormattedList.Join(texts, options.Editor.LinePrefix, options.Editor);
        }
    }
}
