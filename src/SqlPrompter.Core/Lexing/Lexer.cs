using System.Collections.Generic;

namespace SqlPrompter.Core.Lexing
{
    /// <summary>
    /// Tolerantní lexer T-SQL. Nikdy nehází výjimku: nedokončený řetězec, komentář
    /// nebo [identifikátor] skončí koncem textu a má příznak IsUnterminated.
    /// </summary>
    public static class Lexer
    {
        public static IReadOnlyList<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var i = 0;
            var n = text.Length;
            while (i < n)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                var start = i;
                var unterminated = false;
                TokenKind kind;

                if (c == '-' && Peek(text, i + 1) == '-')
                {
                    i += 2;
                    while (i < n && text[i] != '\n' && text[i] != '\r')
                    {
                        i++;
                    }

                    kind = TokenKind.Comment;
                }
                else if (c == '/' && Peek(text, i + 1) == '*')
                {
                    // T-SQL podporuje vnořené blokové komentáře.
                    i += 2;
                    var depth = 1;
                    while (i < n && depth > 0)
                    {
                        if (text[i] == '/' && Peek(text, i + 1) == '*')
                        {
                            depth++;
                            i += 2;
                        }
                        else if (text[i] == '*' && Peek(text, i + 1) == '/')
                        {
                            depth--;
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }
                    }

                    kind = TokenKind.Comment;
                    unterminated = depth > 0;
                }
                else if (c == '\'' || ((c == 'N' || c == 'n') && Peek(text, i + 1) == '\''))
                {
                    i += c == '\'' ? 1 : 2;
                    unterminated = !ReadDelimited(text, ref i, '\'');
                    kind = TokenKind.String;
                }
                else if (c == '[')
                {
                    i++;
                    unterminated = !ReadDelimited(text, ref i, ']');
                    kind = TokenKind.QuotedIdentifier;
                }
                else if (c == '"')
                {
                    i++;
                    unterminated = !ReadDelimited(text, ref i, '"');
                    kind = TokenKind.QuotedIdentifier;
                }
                else if (c == '@')
                {
                    i++;
                    while (i < n && (IsWordChar(text[i]) || text[i] == '@'))
                    {
                        i++;
                    }

                    kind = TokenKind.Variable;
                }
                else if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(text, i + 1))))
                {
                    ReadNumber(text, ref i);
                    kind = TokenKind.Number;
                }
                else if (IsWordStart(c))
                {
                    i++;
                    while (i < n && IsWordChar(text[i]))
                    {
                        i++;
                    }

                    kind = TokenKind.Word;
                }
                else
                {
                    i++;
                    switch (c)
                    {
                        case '.': kind = TokenKind.Dot; break;
                        case ',': kind = TokenKind.Comma; break;
                        case '(': kind = TokenKind.OpenParen; break;
                        case ')': kind = TokenKind.CloseParen; break;
                        case ';': kind = TokenKind.Semicolon; break;
                        default: kind = TokenKind.Operator; break;
                    }
                }

                tokens.Add(new Token(kind, start, i, text.Substring(start, i - start), unterminated));
            }

            return tokens;
        }

        internal static bool IsWordStart(char c) => char.IsLetter(c) || c == '_' || c == '#';

        internal static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '$' || c == '@';

        private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

        /// <summary>Čte do uzavíracího znaku; zdvojený znak je escapovaný. Vrací false, pokud text skončil dřív.</summary>
        private static bool ReadDelimited(string text, ref int i, char close)
        {
            while (i < text.Length)
            {
                if (text[i] == close)
                {
                    if (Peek(text, i + 1) == close)
                    {
                        i += 2;
                        continue;
                    }

                    i++;
                    return true;
                }

                i++;
            }

            return false;
        }

        private static void ReadNumber(string text, ref int i)
        {
            if (text[i] == '0' && (Peek(text, i + 1) == 'x' || Peek(text, i + 1) == 'X'))
            {
                i += 2;
                while (i < text.Length && IsHexDigit(text[i]))
                {
                    i++;
                }

                return;
            }

            while (i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }

            if (i < text.Length && text[i] == '.')
            {
                i++;
                while (i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }
            }

            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                var j = i + 1;
                if (j < text.Length && (text[j] == '+' || text[j] == '-'))
                {
                    j++;
                }

                if (j < text.Length && char.IsDigit(text[j]))
                {
                    i = j;
                    while (i < text.Length && char.IsDigit(text[i]))
                    {
                        i++;
                    }
                }
            }
        }

        private static bool IsHexDigit(char c) => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
