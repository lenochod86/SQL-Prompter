using System;
using System.Collections.Generic;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Completion
{
    /// <summary>Reads the default values of the parameters from the beginning of a procedure definition; SQL Server does not store them anywhere else.</summary>
    public static class ProcedureHeader
    {
        /// <summary>
        /// Parameter name (with <c>@</c>, case-insensitive) → default value as written. Parameters without a default are missing.
        /// The text may be cut off anywhere; whatever was read before that is returned.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ReadDefaults(string definition)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(definition))
            {
                return result;
            }

            var tokens = Lexer.Tokenize(definition);
            var i = 0;
            while (i < tokens.Count && !(tokens[i].IsWord("PROC") || tokens[i].IsWord("PROCEDURE")))
            {
                i++;
            }

            // The name of the procedure, then the parameters (optionally in parentheses).
            for (i++; i < tokens.Count && (tokens[i].IsNamePart || tokens[i].Kind == TokenKind.Dot || tokens[i].Kind == TokenKind.Comment); i++)
            {
            }

            if (i < tokens.Count && tokens[i].Kind == TokenKind.Number)
            {
                // ;1 (the number of the procedure in a group) is not handled.
                i++;
            }

            var wrapped = i < tokens.Count && tokens[i].Kind == TokenKind.OpenParen;
            if (wrapped)
            {
                i++;
            }

            while (i < tokens.Count)
            {
                if (tokens[i].Kind == TokenKind.Comment || tokens[i].Kind == TokenKind.Comma)
                {
                    i++;
                    continue;
                }

                if (tokens[i].Kind != TokenKind.Variable)
                {
                    break;
                }

                var name = tokens[i].Text;
                i++;
                var depth = 0;
                var equals = -1;
                var end = i;
                for (; i < tokens.Count; i++)
                {
                    var token = tokens[i];
                    if (token.Kind == TokenKind.Comment)
                    {
                        continue;
                    }

                    if (token.Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (token.Kind == TokenKind.CloseParen)
                    {
                        if (depth == 0)
                        {
                            break;
                        }

                        depth--;
                    }
                    else if (depth == 0 && token.Kind == TokenKind.Comma)
                    {
                        break;
                    }
                    else if (depth == 0 && token.Kind == TokenKind.Operator && token.Text == "=" && equals < 0)
                    {
                        equals = i;
                        end = i + 1;
                        continue;
                    }
                    else if (depth == 0 && token.Kind == TokenKind.Word &&
                             (token.IsWord("OUT") || token.IsWord("OUTPUT") || token.IsWord("READONLY") || token.IsWord("AS") ||
                              token.IsWord("WITH") || token.IsWord("FOR")))
                    {
                        break;
                    }

                    if (equals >= 0)
                    {
                        end = i + 1;
                    }
                }

                if (equals >= 0 && end > equals + 1)
                {
                    var text = definition.Substring(tokens[equals + 1].Start, tokens[end - 1].End - tokens[equals + 1].Start).Trim();
                    if (text.Length > 0)
                    {
                        result[name] = text;
                    }
                }

                // Skip OUTPUT / READONLY and stop at AS.
                while (i < tokens.Count && tokens[i].Kind == TokenKind.Word && (tokens[i].IsWord("OUT") || tokens[i].IsWord("OUTPUT") || tokens[i].IsWord("READONLY")))
                {
                    i++;
                }

                if (i < tokens.Count && (tokens[i].IsWord("AS") || tokens[i].IsWord("WITH") || tokens[i].IsWord("FOR") || tokens[i].Kind == TokenKind.CloseParen))
                {
                    break;
                }
            }

            return result;
        }
    }
}
