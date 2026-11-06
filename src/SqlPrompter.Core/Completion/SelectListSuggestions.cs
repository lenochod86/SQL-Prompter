using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// What HAVING and ORDER BY offer first (#214): the aggregates of the select list after HAVING; the aliases and expressions of the select list after
    /// ORDER BY, where a GROUP BY or DISTINCT also narrows the columns to those that make sense.
    /// </summary>
    internal static class SelectListSuggestions
    {
        /// <summary>The group of the first item; every item has a group of its own so that the order of the select list is kept (a group below <see cref="Limit"/> is a leading item; the sort text of a group is (group + 100) as three digits, so it must not go below -100).</summary>
        private const int Group = -100;

        private const int Limit = -50;

        private static bool InOrderBy(CaretContext context) =>
            context.Text != null && ColumnClauses.At(context.Text, context.Caret) == ColumnClause.OrderBy;

        /// <summary>The items that go first, or none where HAVING and ORDER BY are not at an item start.</summary>
        public static IEnumerable<Suggestion> Leading(CaretContext context)
        {
            if (context.Text == null || context.Qualifier.Count > 0)
            {
                yield break;
            }

            if (context.InHaving)
            {
                var order = 0;
                foreach (var entry in ColumnClauses.ShapeOf(context.Text, context.Caret).Entries.Where(e => e.IsAggregate))
                {
                    // Not the kind Function: the text of the item must not be converted by the keyword case.
                    yield return new Suggestion(SuggestionKind.Column, entry.Expression, entry.Expression, entry.Expression, null,
                        "aggregate of the select list" + (entry.Alias != null ? " (" + entry.Alias + ")" : string.Empty), Group + order++);
                }

                yield break;
            }

            if (InOrderBy(context))
            {
                var position = 0;
                foreach (var entry in ColumnClauses.ShapeOf(context.Text, context.Caret).Entries.Where(e => !e.IsStar))
                {
                    // An alias can be sorted by; an expression only as written.
                    var text = entry.Alias ?? entry.Expression;
                    yield return new Suggestion(entry.Alias != null ? SuggestionKind.Alias : SuggestionKind.Column, text, text, text, entry.Alias != null ? entry.Expression : null,
                        "item of the select list" + (entry.Alias != null ? ": " + entry.Expression : string.Empty), Group + position++);
                }
            }
        }

        /// <summary>
        /// After ORDER BY with a GROUP BY only the grouped columns, with DISTINCT only the columns of the select list make sense; null for no restriction.
        /// The test is for the column items of the list.
        /// </summary>
        public static Func<Suggestion, bool> ColumnFilter(CaretContext context)
        {
            if (context.Text == null || context.Qualifier.Count > 0 || !InOrderBy(context))
            {
                return null;
            }

            var shape = ColumnClauses.ShapeOf(context.Text, context.Caret);
            if (!shape.HasGroupBy && !shape.Distinct)
            {
                return null;
            }

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in shape.HasGroupBy ? shape.GroupBy : shape.Entries.Select(e => e.Expression))
            {
                allowed.Add(name);
                var dot = name.LastIndexOf('.');
                if (dot >= 0)
                {
                    allowed.Add(name.Substring(dot + 1));
                }
            }

            return suggestion =>
            {
                if (suggestion.Kind != SuggestionKind.Column || suggestion.Group < Limit)
                {
                    return true;
                }

                var name = suggestion.InsertText;
                var dot = name.LastIndexOf('.');
                return allowed.Contains(name) || (dot >= 0 && allowed.Contains(name.Substring(dot + 1))) || allowed.Contains(suggestion.DisplayText);
            };
        }
    }
}
