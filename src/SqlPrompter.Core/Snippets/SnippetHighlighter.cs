using System.Text.RegularExpressions;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Snippets
{
    /// <summary>What a character of a snippet body is, for coloring: SQL tokens and the fields of the snippet.</summary>
    public static class SnippetHighlighter
    {
        public const int Plain = 0;
        public const int Keyword = 1;
        public const int String = 2;
        public const int Comment = 3;
        public const int Variable = 4;
        public const int Field = 5;

        /// <summary>A built-in function followed by a parenthesis (COUNT, SUM, ISNULL).</summary>
        public const int Function = 6;

        public const int Number = 7;

        private static readonly Regex FieldPattern = new Regex(@"\$[A-Za-z_][A-Za-z0-9_]*(\([^)\r\n]*\))?\$|\$\$", RegexOptions.Compiled);

        /// <summary>The kind of every character of the text; a field (<c>$CURSOR$</c>, <c>$name$</c>, <c>$DATE(format)$</c>) wins over the SQL inside it.</summary>
        public static int[] Classify(string text)
        {
            text = text ?? string.Empty;
            var kinds = new int[text.Length];
            var tokens = Lexer.Tokenize(text);
            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                int kind;
                switch (token.Kind)
                {
                    case TokenKind.String: kind = String; break;
                    case TokenKind.Comment: kind = Comment; break;
                    case TokenKind.Variable: kind = Variable; break;
                    case TokenKind.Number: kind = Number; break;
                    case TokenKind.Word:
                        if (Keywords.IsReserved(token.Text) || IsDataType(token.Text))
                        {
                            kind = Keyword;
                        }
                        else
                        {
                            kind = index + 1 < tokens.Count && tokens[index + 1].Kind == TokenKind.OpenParen && IsFunction(token.Text) ? Function : Plain;
                        }

                        break;
                    default: kind = Plain; break;
                }

                for (var i = token.Start; i < token.End && i < kinds.Length; i++)
                {
                    kinds[i] = kind;
                }
            }

            foreach (Match match in FieldPattern.Matches(text))
            {
                for (var i = match.Index; i < match.Index + match.Length; i++)
                {
                    kinds[i] = Field;
                }
            }

            return kinds;
        }

        private static bool IsFunction(string word) => System.Linq.Enumerable.Contains(Keywords.BuiltInFunctions, word, System.StringComparer.OrdinalIgnoreCase);

        private static bool IsDataType(string word) => System.Linq.Enumerable.Contains(Keywords.DataTypes, word, System.StringComparer.OrdinalIgnoreCase);
    }
}
