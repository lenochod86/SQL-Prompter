using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Completion
{
    /// <summary>The clauses where the user picks columns (the column picker and "All non-aggregated columns" are offered there).</summary>
    public enum ColumnClause
    {
        None,
        SelectList,
        InsertColumns,
        GroupBy,
        OrderBy,
    }

    /// <summary>
    /// Finds out whether the caret stands where a new item of a select list, a <c>GROUP BY</c> or an <c>ORDER BY</c> starts (after <c>SELECT</c>,
    /// <c>DISTINCT</c>, <c>TOP n</c>, <c>BY</c> or a comma of such a list), and lists the items of the select list that a <c>GROUP BY</c> has to repeat.
    /// </summary>
    public static class ColumnClauses
    {
        private static readonly HashSet<string> Aggregates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STRING_AGG", "STDEV", "STDEVP", "VAR", "VARP", "GROUPING", "GROUPING_ID", "CHECKSUM_AGG",
            "APPROX_COUNT_DISTINCT", "APPROX_PERCENTILE_CONT", "APPROX_PERCENTILE_DISC",
        };

        /// <summary>The clause of a new item at the caret (the word being typed is not counted), or <see cref="ColumnClause.None"/>.</summary>
        public static ColumnClause At(string text, int caret)
        {
            var tokens = TokensBefore(text, caret, out var typedWord);
            return Classify(tokens, tokens.Count - 1);
        }

        /// <summary>The expressions of the select list that must appear in a <c>GROUP BY</c> at the caret, as written (aliases and constants left out, aggregates skipped).</summary>
        public static IReadOnlyList<string> NonAggregatedItems(string text, int caret)
        {
            var all = Lexer.Tokenize(text ?? string.Empty).Where(t => t.Kind != TokenKind.Comment).ToList();
            var tokens = all.Where(t => t.Start < caret).ToList();
            if (At(text, caret) != ColumnClause.GroupBy)
            {
                return new string[0];
            }

            // Back from the caret to the SELECT of the same block.
            var depth = 0;
            var select = -1;
            for (var i = tokens.Count - 1; i >= 0; i--)
            {
                var t = tokens[i];
                if (t.Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (t.Kind == TokenKind.OpenParen)
                {
                    if (--depth < 0)
                    {
                        return new string[0];
                    }
                }
                else if (depth == 0 && (t.Kind == TokenKind.Semicolon || t.IsWord("UNION") || t.IsWord("EXCEPT") || t.IsWord("INTERSECT")))
                {
                    return new string[0];
                }
                else if (depth == 0 && t.IsWord("SELECT"))
                {
                    select = i;
                    break;
                }
            }

            if (select < 0)
            {
                return new string[0];
            }

            // After SELECT [ALL | DISTINCT] [TOP n | TOP (n)] [PERCENT] [WITH TIES].
            var start = select + 1;
            while (start < tokens.Count && (tokens[start].IsWord("ALL") || tokens[start].IsWord("DISTINCT")))
            {
                start++;
            }

            if (start < tokens.Count && tokens[start].IsWord("TOP"))
            {
                start++;
                if (start < tokens.Count && tokens[start].Kind == TokenKind.OpenParen)
                {
                    start = SkipParens(tokens, start);
                }
                else
                {
                    start++;
                }

                if (start < tokens.Count && tokens[start].IsWord("PERCENT"))
                {
                    start++;
                }

                if (start + 1 < tokens.Count && tokens[start].IsWord("WITH") && tokens[start + 1].IsWord("TIES"))
                {
                    start += 2;
                }
            }

            // Up to the FROM of the same block.
            var end = start;
            depth = 0;
            for (; end < tokens.Count; end++)
            {
                var t = tokens[end];
                if (t.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (t.Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
                else if (depth == 0 && (t.IsWord("FROM") || t.IsWord("INTO")))
                {
                    break;
                }
            }

            var items = new List<string>();
            var itemStart = start;
            depth = 0;
            for (var i = start; i <= end; i++)
            {
                var atEnd = i == end;
                if (!atEnd)
                {
                    if (tokens[i].Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (tokens[i].Kind == TokenKind.CloseParen)
                    {
                        depth--;
                    }
                }

                if (atEnd || (depth == 0 && tokens[i].Kind == TokenKind.Comma))
                {
                    var expression = ExpressionOf(tokens.GetRange(itemStart, i - itemStart), text);
                    if (expression != null && !items.Contains(expression, StringComparer.OrdinalIgnoreCase))
                    {
                        items.Add(expression);
                    }

                    itemStart = i + 1;
                }
            }

            return items;
        }

        /// <summary>The column names already written in the parentheses of an <c>INSERT INTO t (...)</c> around the caret.</summary>
        public static IReadOnlyList<string> ExistingInsertColumns(string text, int caret)
        {
            var tokens = Lexer.Tokenize(text ?? string.Empty).Where(t => t.Kind != TokenKind.Comment).ToList();
            var open = -1;
            var depth = 0;
            for (var i = tokens.Count - 1; i >= 0; i--)
            {
                if (tokens[i].Start >= caret)
                {
                    continue;
                }

                if (tokens[i].Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.OpenParen && depth-- == 0)
                {
                    open = i;
                    break;
                }
            }

            var names = new List<string>();
            if (open < 0)
            {
                return names;
            }

            for (var i = open + 1; i < tokens.Count && tokens[i].Kind != TokenKind.CloseParen; i++)
            {
                var beingTyped = tokens[i].Start < caret && tokens[i].End >= caret;
                if (tokens[i].IsNamePart && !beingTyped && (tokens[i - 1].Kind == TokenKind.OpenParen || tokens[i - 1].Kind == TokenKind.Comma))
                {
                    names.Add(tokens[i].IdentifierValue);
                }
            }

            return names;
        }
        private static List<Token> TokensBefore(string text, int caret, out Token typedWord)
        {
            var tokens = Lexer.Tokenize(text ?? string.Empty).Where(t => t.Kind != TokenKind.Comment && t.Start < caret).ToList();
            typedWord = null;
            if (tokens.Count > 0)
            {
                var last = tokens[tokens.Count - 1];
                if ((last.Kind == TokenKind.Word || last.Kind == TokenKind.QuotedIdentifier) && last.End >= caret && !last.IsUnterminated)
                {
                    typedWord = last;
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }

            return tokens;
        }

        private static ColumnClause Classify(List<Token> tokens, int last)
        {
            if (last < 0)
            {
                return ColumnClause.None;
            }

            var previous = tokens[last];
            if (previous.IsWord("SELECT") || previous.IsWord("DISTINCT") || previous.IsWord("ALL"))
            {
                return previous.IsWord("ALL") && last > 0 && (tokens[last - 1].IsWord("UNION") || tokens[last - 1].IsWord("EXCEPT") || tokens[last - 1].IsWord("INTERSECT"))
                    ? ColumnClause.None
                    : ColumnClause.SelectList;
            }

            if (previous.IsWord("BY") && last > 0)
            {
                return tokens[last - 1].IsWord("GROUP") ? ColumnClause.GroupBy : tokens[last - 1].IsWord("ORDER") ? ColumnClause.OrderBy : ColumnClause.None;
            }

            if (previous.Kind == TokenKind.Number && last > 0 && tokens[last - 1].IsWord("TOP"))
            {
                return ColumnClause.SelectList;
            }

            if (previous.IsWord("PERCENT") || (previous.IsWord("TIES") && last > 0 && tokens[last - 1].IsWord("WITH")))
            {
                return ColumnClause.SelectList;
            }

            if (previous.Kind == TokenKind.CloseParen)
            {
                // SELECT TOP (10) | : the parenthesis directly follows TOP.
                var open = MatchingOpen(tokens, last);
                return open > 0 && tokens[open - 1].IsWord("TOP") ? ColumnClause.SelectList : ColumnClause.None;
            }

            if (previous.Kind == TokenKind.Comma)
            {
                var depth = 0;
                for (var i = last - 1; i >= 0; i--)
                {
                    var t = tokens[i];
                    if (t.Kind == TokenKind.CloseParen)
                    {
                        depth++;
                    }
                    else if (t.Kind == TokenKind.OpenParen)
                    {
                        if (--depth < 0)
                        {
                            return ColumnClause.None;
                        }
                    }
                    else if (depth == 0)
                    {
                        if (t.IsWord("SELECT"))
                        {
                            return ColumnClause.SelectList;
                        }

                        if (t.IsWord("BY") && i > 0)
                        {
                            return tokens[i - 1].IsWord("GROUP") ? ColumnClause.GroupBy : tokens[i - 1].IsWord("ORDER") ? ColumnClause.OrderBy : ColumnClause.None;
                        }

                        if (t.Kind == TokenKind.Semicolon || t.IsWord("FROM") || t.IsWord("WHERE") || t.IsWord("HAVING") || t.IsWord("SET") || t.IsWord("VALUES") || t.IsWord("ON") || t.IsWord("JOIN"))
                        {
                            return ColumnClause.None;
                        }
                    }
                }
            }

            return ColumnClause.None;
        }

        private static int MatchingOpen(List<Token> tokens, int close)
        {
            var depth = 0;
            for (var i = close; i >= 0; i--)
            {
                if (tokens[i].Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.OpenParen && --depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int SkipParens(List<Token> tokens, int open)
        {
            var depth = 0;
            for (var i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.CloseParen && --depth == 0)
                {
                    return i + 1;
                }
            }

            return tokens.Count;
        }

        /// <summary>The text of a select item without its alias, or null for an item a <c>GROUP BY</c> does not repeat.</summary>
        private static string ExpressionOf(List<Token> item, string text)
        {
            if (item.Count == 0)
            {
                return null;
            }

            // alias = expression
            if (item.Count > 2 && item[0].IsNamePart && item[1].Kind == TokenKind.Operator && item[1].Text == "=")
            {
                item = item.GetRange(2, item.Count - 2);
            }

            // expression AS alias, or expression alias
            var asIndex = -1;
            var depth = 0;
            for (var i = 0; i < item.Count; i++)
            {
                if (item[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (item[i].Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
                else if (depth == 0 && item[i].IsWord("AS") && i > 0)
                {
                    asIndex = i;
                }
            }

            if (asIndex > 0)
            {
                item = item.GetRange(0, asIndex);
            }
            else if (item.Count > 1)
            {
                var last = item[item.Count - 1];
                var before = item[item.Count - 2];
                if (last.IsNamePart && !last.IsWord("END") && (before.IsNamePart || before.Kind == TokenKind.CloseParen || before.Kind == TokenKind.Number || before.Kind == TokenKind.String) &&
                    !(before.Kind == TokenKind.Word && IsOperatorWord(before.Upper)))
                {
                    item = item.GetRange(0, item.Count - 1);
                }
            }

            if (item.Count == 0 || item.Any(t => t.IsWord("SELECT")))
            {
                return null;
            }

            for (var i = 0; i < item.Count - 1; i++)
            {
                if (item[i].Kind == TokenKind.Word && Aggregates.Contains(item[i].Text) && item[i + 1].Kind == TokenKind.OpenParen)
                {
                    return null;
                }
            }

            if (item.Any(t => t.IsWord("OVER")))
            {
                return null;
            }

            // Constants and * do not belong to a GROUP BY.
            if (item.All(t => t.Kind == TokenKind.Number || t.Kind == TokenKind.String || t.Kind == TokenKind.Variable || t.Kind == TokenKind.Operator ||
                              t.Kind == TokenKind.OpenParen || t.Kind == TokenKind.CloseParen || t.IsWord("NULL")))
            {
                return null;
            }

            var written = text.Substring(item[0].Start, item[item.Count - 1].End - item[0].Start);
            return string.Join(" ", written.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())).Trim();
        }

        private static bool IsOperatorWord(string upper) => upper == "AND" || upper == "OR" || upper == "NOT" || upper == "IN" || upper == "LIKE" || upper == "IS" || upper == "THEN" || upper == "ELSE" || upper == "WHEN" || upper == "CASE" || upper == "BETWEEN";
    }
}
