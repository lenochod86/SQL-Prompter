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

        private static readonly Regex FieldPattern = new Regex(@"\$[A-Za-z_][A-Za-z0-9_]*(\([^)\r\n]*\))?\$|\$\$", RegexOptions.Compiled);

        /// <summary>The kind of every character of the text; a field (<c>$CURSOR$</c>, <c>$name$</c>, <c>$DATE(format)$</c>) wins over the SQL inside it.</summary>
        public static int[] Classify(string text)
        {
            text = text ?? string.Empty;
            var kinds = new int[text.Length];
            foreach (var token in Lexer.Tokenize(text))
            {
                int kind;
                switch (token.Kind)
                {
                    case TokenKind.String: kind = String; break;
                    case TokenKind.Comment: kind = Comment; break;
                    case TokenKind.Variable: kind = Variable; break;
                    case TokenKind.Word: kind = Keywords.IsReserved(token.Text) ? Keyword : Plain; break;
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
    }
}
