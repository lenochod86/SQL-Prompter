using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// Pairs the column list of an <c>INSERT</c> with its <c>VALUES</c> rows or with the list of its <c>SELECT</c> by position (#554): for a caret on a
    /// column the matching values, for a caret on a value the matching column. Only the text is used. Not paired: an INSERT without a column list,
    /// <c>EXEC</c>, <c>DEFAULT VALUES</c>, the INSERT of a <c>MERGE</c>, later branches of a <c>UNION</c> and the items of a SELECT list from a <c>*</c> on.
    /// </summary>
    public static class InsertValuePairFinder
    {
        /// <summary>Words that cannot be part of an item at the level of its list, so the list ends in front of them (an unfinished script).</summary>
        private static readonly HashSet<string> ListEnd = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "IF", "WHILE", "BEGIN", "EXEC", "EXECUTE", "PRINT", "RETURN", "WITH",
            "FROM", "WHERE", "GROUP", "HAVING", "ORDER", "UNION", "EXCEPT", "INTERSECT", "OPTION", "INTO", "VALUES", "CREATE", "ALTER", "DROP",
            "TRUNCATE", "USE", "COMMIT", "ROLLBACK", "THROW", "RAISERROR",
        };

        /// <summary>The spans of the partners of the item at the caret; empty when the caret is not on an item of an INSERT or it has no partner.</summary>
        public static IReadOnlyList<TextSpan> Find(string text, int caret) => Find(text ?? string.Empty, Lexer.Tokenize(text ?? string.Empty), caret);

        public static IReadOnlyList<TextSpan> Find(string text, IReadOnlyList<Token> tokens, int caret)
        {
            var code = tokens.Where(t => t.Kind != TokenKind.Comment).ToList();
            for (var i = 0; i < code.Count; i++)
            {
                // INSERT after THEN belongs to a MERGE.
                if (!code[i].IsWord("INSERT") || (i > 0 && code[i - 1].IsWord("THEN")))
                {
                    continue;
                }

                // The statement starts after the caret, so neither it nor a later one can hold the item at the caret.
                if (code[i].Start > caret)
                {
                    break;
                }

                var found = Pair(text, code, i + 1, caret);
                if (found.Count > 0)
                {
                    return found;
                }
            }

            return new TextSpan[0];
        }

        private static IReadOnlyList<TextSpan> Pair(string text, List<Token> code, int i, int caret)
        {
            var none = new TextSpan[0];
            if (IsWord(code, i, "TOP"))
            {
                i = SkipParentheses(code, i + 1);
                if (IsWord(code, i, "PERCENT"))
                {
                    i++;
                }
            }

            if (IsWord(code, i, "INTO"))
            {
                i++;
            }

            // The name of the target: a table variable or a name of up to four parts (db..table included).
            if (Is(code, i, TokenKind.Variable))
            {
                i++;
            }
            else
            {
                if (i >= code.Count || !code[i].IsNamePart)
                {
                    return none;
                }

                i++;
                while (Is(code, i, TokenKind.Dot))
                {
                    i++;
                    if (i < code.Count && code[i].IsNamePart)
                    {
                        i++;
                    }
                }
            }

            if (IsWord(code, i, "WITH"))
            {
                i = SkipParentheses(code, i + 1);
            }

            if (!Is(code, i, TokenKind.OpenParen) || IsWord(code, i + 1, "SELECT"))
            {
                return none;
            }

            i++;
            var columns = ReadList(text, code, ref i);
            if (!Is(code, i, TokenKind.CloseParen))
            {
                return none;
            }

            i++;
            if (IsWord(code, i, "OUTPUT"))
            {
                i = SkipOutput(code, i + 1);
            }

            var rows = new List<List<TextSpan>>();
            if (IsWord(code, i, "VALUES"))
            {
                i++;
                while (Is(code, i, TokenKind.OpenParen))
                {
                    i++;
                    rows.Add(ReadList(text, code, ref i));
                    if (!Is(code, i, TokenKind.CloseParen) || !Is(code, i + 1, TokenKind.Comma))
                    {
                        break;
                    }

                    i += 2;
                }
            }
            else if (IsWord(code, i, "SELECT"))
            {
                rows.Add(ReadSelectList(text, code, i + 1));
            }

            var column = IndexAt(columns, caret);
            if (column >= 0)
            {
                return rows.Where(r => column < r.Count && r[column] != null).Select(r => r[column]).ToList();
            }

            foreach (var row in rows)
            {
                var value = IndexAt(row, caret);
                if (value >= 0)
                {
                    return value < columns.Count && columns[value] != null ? new[] { columns[value] } : none;
                }
            }

            return none;
        }

        private static List<TextSpan> ReadSelectList(string text, List<Token> code, int i)
        {
            if (IsWord(code, i, "ALL") || IsWord(code, i, "DISTINCT"))
            {
                i++;
            }

            if (IsWord(code, i, "TOP"))
            {
                i = Is(code, i + 1, TokenKind.OpenParen) ? SkipParentheses(code, i + 1) : i + 2;
                if (IsWord(code, i, "PERCENT"))
                {
                    i++;
                }

                if (IsWord(code, i, "WITH") && IsWord(code, i + 1, "TIES"))
                {
                    i += 2;
                }
            }

            var items = ReadList(text, code, ref i);

            // A star stands for an unknown number of columns, so the positions from it on cannot be told.
            for (var n = 0; n < items.Count; n++)
            {
                var item = items[n];
                if (item != null && text[item.End - 1] == '*' && (item.Length == 1 || text.Substring(item.Start, item.Length - 1).TrimEnd().EndsWith(".", StringComparison.Ordinal)))
                {
                    return items.Take(n).ToList();
                }
            }

            return items;
        }

        /// <summary>
        /// The items separated by the commas of the current level, from <paramref name="i"/> to the closing parenthesis or the word that ends the list
        /// (<paramref name="i"/> is left on it). An item that is empty is null, so that the positions of the others stay.
        /// </summary>
        private static List<TextSpan> ReadList(string text, List<Token> code, ref int i)
        {
            var items = new List<TextSpan>();
            var depth = 0;
            var cases = 0;
            var first = -1;
            var last = -1;
            for (; i < code.Count; i++)
            {
                var token = code[i];
                if (depth == 0)
                {
                    if (token.Kind == TokenKind.CloseParen || token.Kind == TokenKind.Semicolon || IsGo(text, token))
                    {
                        break;
                    }

                    if (token.Kind == TokenKind.Word)
                    {
                        if (token.IsWord("CASE"))
                        {
                            cases++;
                        }
                        else if (token.IsWord("END") || token.IsWord("ELSE"))
                        {
                            // Without an open CASE they belong to the block or the IF around the statement.
                            if (cases == 0)
                            {
                                break;
                            }

                            if (token.IsWord("END"))
                            {
                                cases--;
                            }
                        }
                        else if (ListEnd.Contains(token.Text))
                        {
                            break;
                        }
                    }

                    if (token.Kind == TokenKind.Comma && cases == 0)
                    {
                        items.Add(Span(code, first, last));
                        first = -1;
                        continue;
                    }
                }

                if (token.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (token.Kind == TokenKind.CloseParen)
                {
                    depth--;
                }

                if (first < 0)
                {
                    first = i;
                }

                last = i;
            }

            items.Add(Span(code, first, last));
            return items;
        }

        private static TextSpan Span(List<Token> code, int first, int last)
        {
            return first < 0 ? null : new TextSpan(code[first].Start, code[last].End - code[first].Start);
        }

        private static int IndexAt(List<TextSpan> items, int caret) => items.FindIndex(s => s != null && s.Start <= caret && caret <= s.End);

        /// <summary>The position after an OUTPUT clause (with its INTO table and column list): the VALUES or SELECT of the INSERT.</summary>
        private static int SkipOutput(List<Token> code, int i)
        {
            var depth = 0;
            for (; i < code.Count; i++)
            {
                if (code[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (code[i].Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
                else if (depth == 0 && (code[i].IsWord("VALUES") || code[i].IsWord("SELECT") || code[i].Kind == TokenKind.Semicolon))
                {
                    break;
                }
            }

            return i;
        }

        /// <summary>The position after the parentheses that start at <paramref name="i"/>; <paramref name="i"/> itself when there are none.</summary>
        private static int SkipParentheses(List<Token> code, int i)
        {
            if (!Is(code, i, TokenKind.OpenParen))
            {
                return i;
            }

            var depth = 0;
            for (; i < code.Count; i++)
            {
                if (code[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (code[i].Kind == TokenKind.CloseParen && --depth == 0)
                {
                    return i + 1;
                }
            }

            return i;
        }

        /// <summary>GO at the start of a line separates batches.</summary>
        private static bool IsGo(string text, Token token)
        {
            if (!token.IsWord("GO"))
            {
                return false;
            }

            var i = token.Start - 1;
            while (i >= 0 && (text[i] == ' ' || text[i] == '\t'))
            {
                i--;
            }

            return i < 0 || text[i] == '\n' || text[i] == '\r';
        }

        private static bool Is(List<Token> code, int i, TokenKind kind) => i < code.Count && code[i].Kind == kind;

        private static bool IsWord(List<Token> code, int i, string word) => i < code.Count && code[i].IsWord(word);
    }
}
