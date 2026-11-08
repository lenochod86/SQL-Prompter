using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Whole fragments of INSERT and MERGE offered from the metadata (#205): <c>VALUES (...)</c> and <c>SELECT ...</c> after the column list of an
    /// INSERT, the <c>ON</c> condition and the <c>WHEN</c> clauses of a MERGE. Identity, computed and rowversion columns are left out.
    /// </summary>
    internal static class StatementFragments
    {
        /// <summary>The group of these items: above the keywords (group -1), so that the fragments are not mixed with them alphabetically.</summary>
        private const int Group = -10;

        public static IEnumerable<Suggestion> Build(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            if (metadata == null)
            {
                yield break;
            }

            if (context.InsertTail != null)
            {
                foreach (var item in InsertTail(context, metadata, options))
                {
                    yield return item;
                }
            }

            if (context.Correlation != null)
            {
                foreach (var item in Correlation(context, metadata, options))
                {
                    yield return item;
                }
            }

            if (context.Merge != MergeStage.None)
            {
                foreach (var item in Merge(context, metadata, options))
                {
                    yield return item;
                }
            }
        }

        /// <summary>After <c>INSERT INTO t (a, b) |</c>: <c>VALUES (...)</c> with a value by type for each listed column, and <c>SELECT a, b FROM</c>.</summary>
        private static IEnumerable<Suggestion> InsertTail(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            var table = Readiness.Resolve(context.InsertTail, metadata);
            var all = table == null ? null : metadata.GetColumns(table);
            if (all == null || context.InsertTailColumns.Count == 0)
            {
                yield break;
            }

            var quote = context.QuoteIdentifiers;
            var editor = options.Editor;
            var listed = context.InsertTailColumns
                .Select(name => all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var values = listed.Select(c => c == null ? "NULL" : SqlDefaults.ValueFor(c.TypeName, c.IsNullable)).ToList();
            var valuesWord = options.Kw("VALUES");
            var valuesText = valuesWord + InsertBuilder.ParenthesizedList(values, options.Inserts.Layout, editor, editor.LineIndent.Length + valuesWord.Length);
            yield return new Suggestion(SuggestionKind.Keyword, valuesWord + " (...)", valuesText, "VALUES", null,
                "one value per listed column of " + table + " by its type", Group, isBlock: true);

            var names = context.InsertTailColumns.Select(n => SqlIdentifier.Quote(n, quote)).ToList();
            var selectWord = options.Kw("SELECT");
            var selectText = selectWord + " " + FormattedList.Join(names, editor.LinePrefix + selectWord + " ", editor) + editor.NewLine + editor.LineIndent + options.Kw("FROM") + " ";
            yield return new Suggestion(SuggestionKind.Keyword, selectWord + " ... " + options.Kw("FROM"), selectText, "SELECT", null,
                "the listed columns of " + table + " selected from a source", Group, isBlock: true);
        }

        /// <summary>After the first table of a subquery: <c>WHERE sub.IdOrder = o.IdOrder</c> from the foreign keys to the tables of the outer queries.</summary>
        private static IEnumerable<Suggestion> Correlation(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            var joined = new CaretContext(ContextKind.Expression, context.ReplaceStart, context.ReplaceLength, context.TypedPrefix)
            {
                Sources = context.Sources,
                JoinTarget = context.Correlation,
                QuoteIdentifiers = context.QuoteIdentifiers,
                Correlated = true,
            };
            foreach (var condition in JoinSuggestions.Conditions(joined, metadata, options))
            {
                var where = options.Kw("WHERE");
                yield return new Suggestion(SuggestionKind.JoinCondition, where + " " + condition.DisplayText, where + " " + condition.InsertText, "WHERE", condition.Suffix,
                    condition.Description, Group + condition.Group);
            }
        }

        private static IEnumerable<Suggestion> Merge(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            var objects = context.Sources
                .Where(s => s.ScopeLevel == 0 && s.Kind == TableSourceKind.Object)
                .OrderBy(s => s.TokenIndex)
                .ToList();
            if (objects.Count < 2)
            {
                yield break;
            }

            var target = objects[0];
            var source = objects[1];
            if (context.Merge == MergeStage.On)
            {
                // Conditions the same way as after JOIN: by foreign key, or the primary key of the target against the same column of the source.
                var joined = new CaretContext(ContextKind.Expression, context.ReplaceStart, context.ReplaceLength, context.TypedPrefix)
                {
                    Sources = objects,
                    JoinTarget = source,
                    QuoteIdentifiers = context.QuoteIdentifiers,
                };
                foreach (var condition in JoinSuggestions.Conditions(joined, metadata, options))
                {
                    yield return new Suggestion(SuggestionKind.JoinCondition, options.Kw("ON") + " " + condition.DisplayText, options.Kw("ON") + " " + condition.InsertText, "ON", condition.Suffix,
                        condition.Description, Group + condition.Group);
                }

                yield break;
            }

            var targetObject = Readiness.Resolve(target, metadata);
            var sourceObject = Readiness.Resolve(source, metadata);
            var targetColumns = targetObject == null ? null : metadata.GetColumns(targetObject);
            var sourceColumns = sourceObject == null ? null : metadata.GetColumns(sourceObject);
            if (targetColumns == null || sourceColumns == null)
            {
                yield break;
            }

            var quote = context.QuoteIdentifiers;
            var editor = options.Editor;
            var unit = editor.IndentUnit;

            // Lists inside the clauses continue one level deeper than the line the statement starts on.
            var inner = new EditorLayout { NewLine = editor.NewLine, LineIndent = editor.LineIndent + unit, LinePrefix = editor.LinePrefix, IndentUnit = unit, LineWidth = editor.LineWidth, Style = editor.Style };
            var targetRef = SqlIdentifier.Quote(target.ReferenceName, quote);
            var sourceRef = SqlIdentifier.Quote(source.ReferenceName, quote);
            var key = new HashSet<string>(metadata.GetPrimaryKey(targetObject), StringComparer.OrdinalIgnoreCase);
            var sourceNames = new HashSet<string>(sourceColumns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            var shared = targetColumns.Where(c => c.IsInsertable && sourceNames.Contains(c.Name)).ToList();
            var updatable = shared.Where(c => !key.Contains(c.Name)).ToList();

            if (updatable.Count > 0)
            {
                var assignments = updatable
                    .Select(c => targetRef + "." + SqlIdentifier.Quote(c.Name, quote) + " = " + sourceRef + "." + SqlIdentifier.Quote(c.Name, quote))
                    .ToList();
                var head = editor.LineIndent + unit + options.Kw("UPDATE SET") + " ";
                yield return new Suggestion(SuggestionKind.Keyword, options.Kw("WHEN MATCHED THEN UPDATE SET") + " ...",
                    options.Kw("WHEN MATCHED THEN") + editor.NewLine + head + FormattedList.Join(assignments, head, editor), "WHEN MATCHED", null,
                    "update the columns of " + targetObject + " that " + sourceObject + " has too, except the key", Group, isBlock: true);
            }

            if (shared.Count > 0)
            {
                var names = shared.Select(c => SqlIdentifier.Quote(c.Name, quote)).ToList();
                var values = shared.Select(c => sourceRef + "." + SqlIdentifier.Quote(c.Name, quote)).ToList();
                var insertHead = editor.LineIndent + unit + options.Kw("INSERT");
                var valuesHead = editor.LineIndent + unit + options.Kw("VALUES");
                var text = options.Kw("WHEN NOT MATCHED THEN") + editor.NewLine
                    + insertHead + InsertBuilder.ParenthesizedList(names, options.Inserts.Layout, inner, insertHead.Length) + editor.NewLine
                    + valuesHead + InsertBuilder.ParenthesizedList(values, options.Inserts.Layout, inner, valuesHead.Length);
                yield return new Suggestion(SuggestionKind.Keyword, options.Kw("WHEN NOT MATCHED THEN INSERT") + " ...", text, "WHEN NOT MATCHED", null,
                    "insert the rows of " + sourceObject + " that " + targetObject + " lacks", Group, isBlock: true);
            }

            yield return new Suggestion(SuggestionKind.Keyword, options.Kw("WHEN NOT MATCHED BY SOURCE THEN DELETE"),
                options.Kw("WHEN NOT MATCHED BY SOURCE THEN DELETE"), "WHEN NOT MATCHED BY SOURCE", null,
                "delete the rows of " + targetObject + " that " + sourceObject + " lacks", Group, isBlock: true);
        }
    }
}
