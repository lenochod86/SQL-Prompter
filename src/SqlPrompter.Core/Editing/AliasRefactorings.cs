using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Editing
{
    /// <summary>Formatting actions that change a few characters: the <c>N</c> prefix of strings and the <c>AS</c> before aliases.</summary>
    public static class AliasRefactorings
    {
        private static readonly HashSet<string> EndsSelectList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FROM", "INTO", "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "FOR", "OPTION", "GO",
        };

        /// <summary>Puts <c>N</c> in front of every string literal that has none (<c>'a'</c> becomes <c>N'a'</c>).</summary>
        public static string AddNPrefix(string text)
        {
            var edits = new List<Change>();
            foreach (var token in Lexer.Tokenize(text ?? string.Empty))
            {
                if (token.Kind == TokenKind.String && token.Text[0] == '\'')
                {
                    edits.Add(new Change(token.Start, 0, "N"));
                }
            }

            return Apply(text, edits);
        }

        /// <summary>Writes <c>AS</c> before the aliases of columns in a select list and of tables after FROM and JOIN.</summary>
        public static string AddAs(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            var inserts = new SortedSet<int>();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].IsWord("SELECT"))
                {
                    ColumnAliases(tokens, i, inserts);
                }
                else if (tokens[i].IsWord("FROM") || tokens[i].IsWord("JOIN") || tokens[i].IsWord("APPLY"))
                {
                    TableAliases(tokens, i + 1, tokens[i].IsWord("FROM"), inserts);
                }
            }

            return Apply(text, inserts.Select(p => new Change(p, 0, "AS ")).ToList());
        }

        /// <summary>Removes the <c>AS</c> before an alias (<c>a.Id AS i</c> becomes <c>a.Id i</c>); not the AS of CAST, CTEs, procedures or DECLARE.</summary>
        public static string RemoveAs(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            var openers = new Stack<string>();
            var edits = new List<Change>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.OpenParen)
                {
                    openers.Push(i > 0 && tokens[i - 1].Kind == TokenKind.Word ? tokens[i - 1].Upper : string.Empty);
                    continue;
                }

                if (token.Kind == TokenKind.CloseParen)
                {
                    if (openers.Count > 0)
                    {
                        openers.Pop();
                    }

                    continue;
                }

                if (!token.IsWord("AS") || i == 0 || i + 1 >= tokens.Count)
                {
                    continue;
                }

                var previous = tokens[i - 1];
                var next = tokens[i + 1];
                var opener = openers.Count > 0 ? openers.Peek() : string.Empty;
                var aliasLike = next.Kind == TokenKind.QuotedIdentifier || (next.Kind == TokenKind.Word && !Keywords.IsReserved(next.Text) &&
                                                                          !Keywords.DataTypes.Contains(next.Upper, StringComparer.OrdinalIgnoreCase));
                if (!aliasLike || opener == "CAST" || opener == "TRY_CAST" || previous.Kind == TokenKind.Variable ||
                    previous.Kind == TokenKind.Operator || previous.Kind == TokenKind.Comma || previous.Kind == TokenKind.OpenParen)
                {
                    continue;
                }

                // Only plain white space between AS and the alias; a comment there stays.
                if (text.Substring(token.End, next.Start - token.End).Trim().Length != 0)
                {
                    continue;
                }

                edits.Add(new Change(token.Start, next.Start - token.Start, string.Empty));
            }

            return Apply(text, edits);
        }

        private static void ColumnAliases(List<Token> tokens, int select, SortedSet<int> inserts)
        {
            var i = select + 1;
            while (i < tokens.Count && (tokens[i].IsWord("DISTINCT") || tokens[i].IsWord("ALL")))
            {
                i++;
            }

            if (i < tokens.Count && tokens[i].IsWord("TOP"))
            {
                i++;
                if (i < tokens.Count && tokens[i].Kind == TokenKind.OpenParen)
                {
                    i = Close(tokens, i) + 1;
                }
                else
                {
                    i++;
                }

                if (i < tokens.Count && tokens[i].IsWord("PERCENT"))
                {
                    i++;
                }

                if (i + 1 < tokens.Count && tokens[i].IsWord("WITH") && tokens[i + 1].IsWord("TIES"))
                {
                    i += 2;
                }
            }

            var itemStart = i;
            var depth = 0;
            for (; i <= tokens.Count; i++)
            {
                var end = i == tokens.Count;
                var token = end ? null : tokens[i];
                if (!end && token.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (!end && token.Kind == TokenKind.CloseParen)
                {
                    depth--;
                }

                var stops = end || depth < 0 || (depth == 0 && (token.Kind == TokenKind.Semicolon || (token.Kind == TokenKind.Word && EndsSelectList.Contains(token.Text))));
                if (stops || (depth == 0 && token.Kind == TokenKind.Comma))
                {
                    Item(tokens, itemStart, i - 1, inserts);
                    itemStart = i + 1;
                }

                if (stops)
                {
                    return;
                }
            }
        }

        private static void Item(List<Token> tokens, int from, int to, SortedSet<int> inserts)
        {
            if (to - from < 1)
            {
                return;
            }

            var first = tokens[from];
            if ((first.Kind == TokenKind.Variable || first.IsNamePart || first.Kind == TokenKind.String) && tokens[from + 1].Kind == TokenKind.Operator && tokens[from + 1].Text == "=")
            {
                return; // alias = expression, @variable = expression
            }

            var last = tokens[to];
            var previous = tokens[to - 1];
            var isName = last.Kind == TokenKind.QuotedIdentifier || (last.Kind == TokenKind.Word && !Keywords.IsReserved(last.Text) && !last.Text.StartsWith("#", StringComparison.Ordinal));
            if (!isName)
            {
                return;
            }

            var after = previous.Kind == TokenKind.CloseParen || previous.Kind == TokenKind.QuotedIdentifier || previous.Kind == TokenKind.Variable ||
                        previous.Kind == TokenKind.Number || previous.Kind == TokenKind.String ||
                        (previous.Kind == TokenKind.Word && (previous.IsWord("END") || !Keywords.IsReserved(previous.Text)));
            if (after)
            {
                inserts.Add(last.Start);
            }
        }

        private static void TableAliases(List<Token> tokens, int i, bool allowList, SortedSet<int> inserts)
        {
            while (i < tokens.Count)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    var close = Close(tokens, i);
                    if (close < 0)
                    {
                        return;
                    }

                    i = close + 1;
                }
                else if (tokens[i].IsNamePart && !(tokens[i].Kind == TokenKind.Word && Keywords.IsReserved(tokens[i].Text)))
                {
                    i++;
                    while (i + 1 < tokens.Count && tokens[i].Kind == TokenKind.Dot && tokens[i + 1].IsNamePart)
                    {
                        i += 2;
                    }

                    if (i < tokens.Count && tokens[i].Kind == TokenKind.OpenParen)
                    {
                        var close = Close(tokens, i);
                        if (close < 0)
                        {
                            return;
                        }

                        i = close + 1;
                    }
                }
                else
                {
                    return;
                }

                if (i < tokens.Count && tokens[i].IsWord("AS"))
                {
                    i += 2;
                }
                else if (i < tokens.Count && tokens[i].IsIdentifier && !Keywords.CannotBeAlias(tokens[i].Text))
                {
                    inserts.Add(tokens[i].Start);
                    i++;
                }

                if (allowList && i + 1 < tokens.Count && tokens[i].Kind == TokenKind.Comma)
                {
                    i++;
                    continue;
                }

                return;
            }
        }

        private static int Close(List<Token> tokens, int open)
        {
            var depth = 0;
            for (var i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.CloseParen && --depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string Apply(string text, List<Change> edits)
        {
            if (edits.Count == 0)
            {
                return text;
            }

            var result = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.Start))
            {
                result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
            }

            return result.ToString();
        }

        private sealed class Change
        {
            public Change(int start, int length, string replacement)
            {
                Start = start;
                Length = length;
                Replacement = replacement;
            }

            public int Start { get; }

            public int Length { get; }

            public string Replacement { get; }
        }
    }
}
