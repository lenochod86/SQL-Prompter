using System.Collections.Generic;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Decides whether the list opens by itself after a space that follows a keyword (or an operator) that expects a column or an expression:
    /// <c>WHERE |</c>, <c>AND |</c>, <c>GROUP BY |</c>, <c>HAVING |</c>, <c>ORDER BY |</c>, <c>ON |</c>, <c>SET |</c>, <c>SELECT |</c>, <c>WHEN |</c>, <c>a = |</c> (#382).
    /// The context analyzer says what is expected at the caret; this only checks that the user has just typed the space after such a word, so the list
    /// does not open after a complete name or alias (<c>FROM dbo.Orders |</c>), after a literal (<c>x = 1 |</c>) or inside a string or a comment.
    /// </summary>
    public static class KeywordSpaceTrigger
    {
        private static readonly HashSet<string> NotExpecting = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "NULL", "END", "ASC", "DESC", "GO", "TRUE", "FALSE", "CURRENT_TIMESTAMP", "CURRENT_USER", "SESSION_USER", "SYSTEM_USER", "USER",
        };

        private static readonly HashSet<string> JoinModifiers = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "LEFT", "RIGHT", "FULL", "INNER", "CROSS", "OUTER" };

        /// <param name="text">The text of the batch.</param>
        /// <param name="caret">The caret, right after the typed space.</param>
        /// <param name="context">The context at the caret (<see cref="ContextAnalyzer"/>).</param>
        public static bool ShouldOpen(string text, int caret, CaretContext context)
        {
            if (text == null || context == null || caret <= 0 || caret > text.Length || (context.Kind != ContextKind.Expression && context.Kind != ContextKind.Keyword) || context.TypedPrefix.Length > 0 || context.Qualifier.Count > 0)
            {
                return false;
            }

            if (!char.IsWhiteSpace(text[caret - 1]))
            {
                return false;
            }

            Token previous = null;
            foreach (var token in Lexer.Tokenize(text))
            {
                if (token.Start >= caret)
                {
                    break;
                }

                if (token.End > caret || token.IsUnterminated)
                {
                    // The caret is inside a string, a comment or a quoted name.
                    return false;
                }

                if (token.Kind != TokenKind.Comment)
                {
                    previous = token;
                }
            }

            if (previous == null)
            {
                return false;
            }

            // Only blanks between the word and the caret: the space was typed right after it.
            for (var i = previous.End; i < caret; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return false;
                }
            }

            if (previous.Kind == TokenKind.Word)
            {
                // The keyword context (after a table source) is only for the words that start a join: LEFT |, INNER |.
                if (context.Kind == ContextKind.Keyword)
                {
                    return JoinModifiers.Contains(previous.Text);
                }

                return Keywords.IsReserved(previous.Text) && !NotExpecting.Contains(previous.Text);
            }

            if (context.Kind == ContextKind.Keyword)
            {
                return false;
            }

            // After an operator that continues an expression (a = |, a + |); not after * (SELECT * |).
            return previous.Kind == TokenKind.Operator && (previous.Text == "=" || previous.Text == "<" || previous.Text == ">" || previous.Text == "+" || previous.Text == "-" || previous.Text == "/");
        }
    }
}
