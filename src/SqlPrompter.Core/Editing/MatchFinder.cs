using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Editing
{
    /// <summary>A stretch of the text (start and length).</summary>
    public sealed class TextSpan
    {
        public TextSpan(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }

        public int Length { get; }

        public int End => Start + Length;
    }

    /// <summary>
    /// The other places of the script where the name under the caret appears (a table, an alias, a column, a variable). The comparison is by
    /// name, ignoring case and brackets, so <c>Orders</c>, <c>[orders]</c> and <c>dbo.Orders</c> match; strings and comments never do.
    /// </summary>
    public static class MatchFinder
    {
        /// <summary>The spans of every appearance of the name at the caret (the one under the caret included); empty when the caret is not on a name or the name is a keyword.</summary>
        public static IReadOnlyList<TextSpan> Find(string text, int caret) => Find(Lexer.Tokenize(text ?? string.Empty), caret);

        public static IReadOnlyList<TextSpan> Find(IReadOnlyList<Token> tokens, int caret)
        {
            var under = tokens.FirstOrDefault(t => IsName(t) && t.Start <= caret && caret <= t.End);
            if (under == null)
            {
                return new TextSpan[0];
            }

            var name = NameOf(under);
            return tokens
                .Where(t => IsName(t) && string.Equals(NameOf(t), name, StringComparison.OrdinalIgnoreCase))
                .Select(t => new TextSpan(t.Start, t.End - t.Start))
                .ToList();
        }

        private static bool IsName(Token token)
        {
            switch (token.Kind)
            {
                case TokenKind.QuotedIdentifier:
                case TokenKind.Variable:
                    return true;
                case TokenKind.Word:
                    return !Keywords.CannotBeAlias(token.Text);
                default:
                    return false;
            }
        }

        private static string NameOf(Token token)
        {
            if (token.Kind != TokenKind.QuotedIdentifier)
            {
                return token.Text;
            }

            var text = token.Text;
            var inner = text.Length >= 2 ? text.Substring(1, text.Length - (token.IsUnterminated ? 1 : 2)) : string.Empty;
            return text[0] == '[' ? inner.Replace("]]", "]") : inner.Replace("\"\"", "\"");
        }
    }
}
