using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Completion
{
    /// <summary>What replaces the <c>*</c>: the span of the text to replace and the new text.</summary>
    public sealed class StarExpansion
    {
        public StarExpansion(int start, int length, string text)
        {
            Start = start;
            Length = length;
            Text = text;
        }

        public int Start { get; }

        public int Length { get; }

        public string Text { get; }
    }

    /// <summary>Expands <c>*</c> and <c>alias.*</c> in a select list into the column list, one column per line laid out by the formatting style (<see cref="FormattedList"/>).</summary>
    public static class StarExpander
    {
        /// <summary>
        /// The expansion for a <c>*</c> that ends right at the caret, or null when the caret is not after a select list <c>*</c> or the columns
        /// of a source are not known (yet). All columns of all sources of the block in source order; <c>a.*</c> only those of <c>a</c>.
        /// Columns are qualified with the alias, or with the table name when the query has several tables and the table has no alias.
        /// </summary>
        public static StarExpansion TryExpand(string text, int caret, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options)
        {
            if (text == null || caret < 1 || caret > text.Length || metadata == null)
            {
                return null;
            }

            options = options ?? SuggestionOptions.Default;
            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            var star = tokens.FindIndex(t => t.Kind == TokenKind.Operator && t.Text == "*" && t.End == caret);
            if (star < 0)
            {
                return null;
            }

            var start = tokens[star].Start;
            var qualifier = new List<string>();
            var qualifierText = string.Empty;
            if (star > 0 && tokens[star - 1].Kind == TokenKind.Dot)
            {
                // alias.* or schema.table.*
                var k = star - 1;
                while (k >= 1 && tokens[k].Kind == TokenKind.Dot && tokens[k - 1].IsNamePart)
                {
                    qualifier.Insert(0, tokens[k - 1].IdentifierValue);
                    start = tokens[k - 1].Start;
                    k -= 2;
                }

                if (qualifier.Count == 0 || qualifier.Count > 2)
                {
                    return null;
                }

                qualifierText = text.Substring(start, tokens[star - 1].Start - start);
            }
            else if (!StartsSelectItem(tokens, star))
            {
                return null;
            }

            var block = ContextAnalyzer.SourcesAt(text, caret).Where(s => s.ScopeLevel == 0).ToList();
            if (block.Count == 0)
            {
                return null;
            }

            var chosen = qualifier.Count > 0 ? block.Where(s => Matches(s, qualifier)).ToList() : block;
            if (chosen.Count == 0)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (var source in chosen)
            {
                var columns = SuggestionBuilder.ColumnsOf(source, metadata, script, options);
                if (columns == null)
                {
                    return null;
                }

                string reference;
                if (qualifier.Count > 0)
                {
                    reference = qualifierText;
                }
                else if (source.Alias != null)
                {
                    reference = SqlIdentifier.Quote(source.Alias);
                }
                else
                {
                    reference = block.Count > 1 && source.ObjectName != null ? SqlIdentifier.Quote(source.ObjectName) : null;
                }

                parts.AddRange(columns.Select(c => (reference != null ? reference + "." : string.Empty) + SqlIdentifier.Quote(c.Name)));
            }

            if (parts.Count == 0)
            {
                return null;
            }

            var result = FormattedList.Join(parts, options.Editor.LinePrefix, options.Editor);
            return new StarExpansion(start, caret - start, result);
        }

        /// <summary>True when the <c>*</c> ending at <paramref name="caret"/> is a select-list star (<c>*</c> or <c>alias.*</c>), not <c>COUNT(*)</c> or a multiplication.</summary>
        internal static bool IsSelectListStar(string text, int caret)
        {
            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            var star = tokens.FindIndex(t => t.Kind == TokenKind.Operator && t.Text == "*" && t.End == caret);
            return star > 0 && (tokens[star - 1].Kind == TokenKind.Dot || StartsSelectItem(tokens, star));
        }

        /// <summary>The <c>*</c> starts an item of a select list: after SELECT, DISTINCT, ALL, TOP n, or a comma.</summary>
        private static bool StartsSelectItem(List<Token> tokens, int star)
        {
            if (star == 0)
            {
                return false;
            }

            var previous = tokens[star - 1];
            if (previous.Kind == TokenKind.Comma)
            {
                return true;
            }

            if (previous.IsWord("SELECT") || previous.IsWord("DISTINCT") || previous.IsWord("ALL"))
            {
                return true;
            }

            // TOP 10 *, TOP (10) *, TOP 10 PERCENT *, TOP (10) WITH TIES *
            for (var i = star - 1; i >= 0 && star - i <= 8; i--)
            {
                if (tokens[i].IsWord("TOP"))
                {
                    return true;
                }

                if (tokens[i].IsWord("SELECT") || tokens[i].Kind == TokenKind.Comma)
                {
                    return false;
                }
            }

            return false;
        }

        private static bool Matches(TableSource source, IReadOnlyList<string> qualifier)
        {
            if (qualifier.Count == 1)
            {
                return string.Equals(source.Alias, qualifier[0], StringComparison.OrdinalIgnoreCase) ||
                       (source.Alias == null && string.Equals(source.ObjectName, qualifier[0], StringComparison.OrdinalIgnoreCase));
            }

            return source.Alias == null &&
                   string.Equals(source.Schema, qualifier[0], StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(source.ObjectName, qualifier[1], StringComparison.OrdinalIgnoreCase);
        }
    }
}
