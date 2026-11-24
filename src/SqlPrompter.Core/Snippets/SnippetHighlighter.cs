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

        /// <summary>Punctuation and operators: <c>( ) , ; . = + *</c> (the editor has an own, gray color for them).</summary>
        public const int Operator = 8;

        /// <summary>A system stored procedure by its prefix (<c>sp_help</c>, <c>xp_cmdshell</c>).</summary>
        public const int SystemProcedure = 9;

        /// <summary>A system table or view: a name in the <c>sys</c> schema (<c>sys.objects</c>) or a legacy one (<c>sysobjects</c>).</summary>
        public const int SystemTable = 10;

        private static readonly string[] LegacySystemTables =
        {
            "sysobjects", "syscolumns", "sysindexes", "sysusers", "sysdatabases", "syscomments", "sysdepends", "sysforeignkeys", "sysprocesses",
            "syslogins", "sysfiles", "sysfilegroups", "sysconstraints", "sysreferences", "systypes", "sysmessages", "sysservers",
        };

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
                    case TokenKind.Variable: kind = token.Text.StartsWith("@@", System.StringComparison.Ordinal) ? Function : Variable; break;
                    case TokenKind.Dot:
                    case TokenKind.Comma:
                    case TokenKind.OpenParen:
                    case TokenKind.CloseParen:
                    case TokenKind.Semicolon:
                    case TokenKind.Operator: kind = Operator; break;
                    case TokenKind.Number: kind = Number; break;
                    case TokenKind.Word:
                        if (Keywords.IsReserved(token.Text) || IsDataType(token.Text))
                        {
                            kind = Keyword;
                        }
                        else if (IsSystemTable(tokens, index))
                        {
                            kind = SystemTable;
                        }
                        else if (IsSystemProcedure(tokens, index))
                        {
                            kind = SystemProcedure;
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

        private static bool IsSystemTable(System.Collections.Generic.IReadOnlyList<Token> tokens, int index)
        {
            var text = tokens[index].Text;
            if (System.Linq.Enumerable.Contains(LegacySystemTables, text, System.StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            // sys.objects: both the schema and the name.
            var followsSys = index >= 2 && tokens[index - 1].Kind == TokenKind.Dot && tokens[index - 2].Kind == TokenKind.Word && tokens[index - 2].Text.Equals("sys", System.StringComparison.OrdinalIgnoreCase);
            var precedesName = text.Equals("sys", System.StringComparison.OrdinalIgnoreCase) && index + 2 < tokens.Count && tokens[index + 1].Kind == TokenKind.Dot && tokens[index + 2].Kind == TokenKind.Word;
            return followsSys || precedesName;
        }

        private static bool IsSystemProcedure(System.Collections.Generic.IReadOnlyList<Token> tokens, int index)
        {
            var text = tokens[index].Text;
            var prefixed = text.StartsWith("sp_", System.StringComparison.OrdinalIgnoreCase) || text.StartsWith("xp_", System.StringComparison.OrdinalIgnoreCase);
            return prefixed && !(index > 0 && tokens[index - 1].Kind == TokenKind.Dot);
        }

        private static bool IsFunction(string word) => System.Linq.Enumerable.Contains(Keywords.BuiltInFunctions, word, System.StringComparer.OrdinalIgnoreCase);

        private static bool IsDataType(string word) => System.Linq.Enumerable.Contains(Keywords.DataTypes, word, System.StringComparer.OrdinalIgnoreCase);
    }
}
