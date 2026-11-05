using System.Collections.Generic;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Decides whether the list opens by itself after a comma (or the space that follows it): at the next item of a select list, GROUP BY,
    /// ORDER BY, SET, the FROM list, function arguments and the column list of INSERT. It stays closed where the user types literals or a
    /// name of their own: VALUES lists, IN lists of literals and DECLARE lists.
    /// </summary>
    public static class CommaTrigger
    {
        public static bool ShouldOpen(string text, int caret, CaretContext context)
        {
            if (text == null || context == null || caret < 0 || caret > text.Length)
            {
                return false;
            }

            if (context.Kind != ContextKind.Expression && context.Kind != ContextKind.ObjectName && context.Kind != ContextKind.InsertColumns)
            {
                return false;
            }

            // The token before the caret must be a comma with nothing but blanks between it and the caret.
            var before = new List<Token>();
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

                if (token.Kind == TokenKind.Comment)
                {
                    continue;
                }

                before.Add(token);
            }

            if (before.Count == 0 || before[before.Count - 1].Kind != TokenKind.Comma || !IsBlank(text, before[before.Count - 1].End, caret))
            {
                return false;
            }

            // Start of the statement: DECLARE lists name variables of the user's own.
            var statementStart = 0;
            for (var i = before.Count - 1; i >= 0; i--)
            {
                if (before[i].Kind == TokenKind.Semicolon)
                {
                    statementStart = i + 1;
                    break;
                }
            }

            if (statementStart < before.Count && before[statementStart].IsWord("DECLARE"))
            {
                return false;
            }

            // The innermost open parenthesis: VALUES (…) and IN (literals…) are lists of values.
            var depth = 0;
            for (var i = before.Count - 1; i >= statementStart; i--)
            {
                if (before[i].Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (before[i].Kind == TokenKind.OpenParen)
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    var owner = i > statementStart ? before[i - 1] : null;
                    if (owner != null && owner.IsWord("VALUES"))
                    {
                        return false;
                    }

                    if (owner != null && owner.IsWord("IN") && OnlyLiterals(before, i + 1))
                    {
                        return false;
                    }

                    return true;
                }
            }

            // Not inside parentheses: a VALUES (…), (…) list continues after a comma that follows a closing parenthesis.
            for (var i = statementStart; i < before.Count; i++)
            {
                if (before[i].IsWord("VALUES"))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool OnlyLiterals(List<Token> tokens, int from)
        {
            for (var i = from; i < tokens.Count; i++)
            {
                var kind = tokens[i].Kind;
                if (kind != TokenKind.Number && kind != TokenKind.String && kind != TokenKind.Comma && !(kind == TokenKind.Operator && tokens[i].Text == "-"))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsBlank(string text, int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
