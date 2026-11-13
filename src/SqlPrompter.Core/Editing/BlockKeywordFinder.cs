using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// The keywords that belong together with the keyword at the caret (#435): <c>BEGIN</c> with its <c>END</c>, <c>BEGIN TRY</c> / <c>END TRY</c> with
    /// <c>BEGIN CATCH</c> / <c>END CATCH</c>, <c>BEGIN TRAN</c> with its <c>COMMIT</c> / <c>ROLLBACK</c>, <c>CASE</c> with its <c>WHEN</c>, <c>THEN</c>,
    /// <c>ELSE</c> and <c>END</c>, <c>IF</c> with its <c>ELSE</c> and <c>WHILE</c> with the <c>BEGIN</c> / <c>END</c> of its body. Nesting is respected, strings,
    /// comments and identifiers never match, a <c>GO</c> line ends the search and unbalanced code finds only what is balanced. Not handled: <c>MERGE</c> with
    /// its <c>WHEN</c> clauses, <c>BEGIN DIALOG</c>, <c>BEGIN CONVERSATION TIMER</c> and brackets.
    /// </summary>
    public static class BlockKeywordFinder
    {
        private static readonly HashSet<string> StatementStarters = new HashSet<string>(StringComparer.Ordinal)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "SET", "EXEC", "EXECUTE", "PRINT", "RETURN", "BREAK", "CONTINUE", "DECLARE", "WITH", "MERGE", "THROW",
            "RAISERROR", "TRUNCATE", "CREATE", "ALTER", "DROP", "WAITFOR", "GOTO", "FETCH", "OPEN", "CLOSE", "DEALLOCATE", "IF", "WHILE", "COMMIT", "ROLLBACK",
        };

        /// <summary>The spans of every keyword of the group the keyword at the caret belongs to; empty when the caret is not on such a keyword or it has no partner.</summary>
        public static IReadOnlyList<TextSpan> Find(string text, int caret) => Find(text ?? string.Empty, Lexer.Tokenize(text ?? string.Empty), caret);

        public static IReadOnlyList<TextSpan> Find(string text, IReadOnlyList<Token> tokens, int caret)
        {
            var (first, last) = BatchRange(text, tokens, caret);
            if (first < 0)
            {
                return new TextSpan[0];
            }

            var words = new List<Token>();
            for (var i = first; i <= last; i++)
            {
                if (tokens[i].Kind != TokenKind.Comment)
                {
                    words.Add(tokens[i]);
                }
            }

            foreach (var group in Groups(words))
            {
                if (group.Count < 2)
                {
                    continue;
                }

                if (group.Any(t => t.Start < caret && caret < t.End) || group.Any(t => t.Start == caret || t.End == caret))
                {
                    return group.OrderBy(t => t.Start).Select(t => new TextSpan(t.Start, t.End - t.Start)).ToList();
                }
            }

            return new TextSpan[0];
        }

        /// <summary>The tokens of the batch that contains the caret (between the GO lines); (-1, -1) when there are none.</summary>
        private static (int First, int Last) BatchRange(string text, IReadOnlyList<Token> tokens, int caret)
        {
            var first = 0;
            var last = tokens.Count - 1;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!IsGo(text, tokens[i]))
                {
                    continue;
                }

                if (tokens[i].End < caret)
                {
                    first = i + 1;
                }
                else if (tokens[i].Start >= caret)
                {
                    last = i - 1;
                    break;
                }
                else
                {
                    // The caret is on the GO itself.
                    return (-1, -1);
                }
            }

            return first > last ? (-1, -1) : (first, last);
        }

        private static bool IsGo(string text, Token token)
        {
            if (!token.IsWord("GO"))
            {
                return false;
            }

            for (var i = token.Start - 1; i >= 0 && text[i] != '\n' && text[i] != '\r'; i--)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private enum Kind
        {
            Begin,
            Try,
            Catch,
            Case,
            If,
            While,
        }

        private sealed class Entry
        {
            public Kind Kind;
            public List<Token> Group;
            public int ParenDepth;
            public bool Pending;
        }

        /// <summary>Every complete (or partly found) group of keywords of the batch.</summary>
        private static List<List<Token>> Groups(List<Token> t)
        {
            var result = new List<List<Token>>();
            var stack = new List<Entry>();
            var transactions = new List<List<Token>>();
            var depth = 0;
            List<Token> lastTry = null;
            var lastTryEnd = -1;

            Token At(int index) => index >= 0 && index < t.Count ? t[index] : null;
            bool Is(int index, string word) => At(index) != null && At(index).IsWord(word);

            for (var i = 0; i < t.Count; i++)
            {
                var token = t[i];
                if (token.Kind == TokenKind.OpenParen)
                {
                    depth++;
                    continue;
                }

                if (token.Kind == TokenKind.CloseParen)
                {
                    depth = Math.Max(0, depth - 1);
                    continue;
                }

                if (token.Kind != TokenKind.Word)
                {
                    continue;
                }

                // A WHILE or IF waits for its body: the BEGIN of a block attaches to a WHILE; a statement means a body without a block.
                var waiting = stack.LastOrDefault(e => e.Pending && e.ParenDepth == depth);
                switch (token.Upper)
                {
                    case "BEGIN":
                        if (Is(i + 1, "TRY") || Is(i + 1, "CATCH"))
                        {
                            var isTry = Is(i + 1, "TRY");
                            List<Token> group;
                            if (!isTry && lastTry != null && lastTryEnd == i - 1)
                            {
                                group = lastTry;
                            }
                            else
                            {
                                group = new List<Token>();
                            }

                            group.Add(token);
                            group.Add(t[i + 1]);
                            stack.Add(new Entry { Kind = isTry ? Kind.Try : Kind.Catch, Group = group });
                            i++;
                        }
                        else if (Is(i + 1, "TRAN") || Is(i + 1, "TRANSACTION") || (Is(i + 1, "DISTRIBUTED") && (Is(i + 2, "TRAN") || Is(i + 2, "TRANSACTION"))))
                        {
                            var group = new List<Token> { token, t[i + 1] };
                            if (Is(i + 1, "DISTRIBUTED"))
                            {
                                group.Add(t[i + 2]);
                                i++;
                            }

                            transactions.Add(group);
                            i++;
                        }
                        else if (Is(i + 1, "DIALOG") || Is(i + 1, "CONVERSATION"))
                        {
                            i++;
                        }
                        else
                        {
                            var group = waiting != null && waiting.Kind == Kind.While ? waiting.Group : new List<Token>();
                            if (waiting != null)
                            {
                                waiting.Pending = false;
                            }

                            if (!result.Contains(group))
                            {
                                result.Add(group);
                            }

                            group.Add(token);
                            stack.Add(new Entry { Kind = Kind.Begin, Group = group });
                        }

                        break;
                    case "END":
                        if (Is(i + 1, "TRY") || Is(i + 1, "CATCH"))
                        {
                            var closing = Is(i + 1, "TRY") ? Kind.Try : Kind.Catch;
                            var index = stack.FindLastIndex(e => e.Kind == closing);
                            if (index >= 0)
                            {
                                stack[index].Group.Add(token);
                                stack[index].Group.Add(t[i + 1]);
                                if (!result.Contains(stack[index].Group))
                                {
                                    result.Add(stack[index].Group);
                                }

                                if (closing == Kind.Try)
                                {
                                    lastTry = stack[index].Group;
                                    lastTryEnd = i + 1;
                                    if (At(i + 2) != null && At(i + 2).Kind == TokenKind.Semicolon)
                                    {
                                        lastTryEnd = i + 2;
                                    }
                                }

                                stack.RemoveRange(index, stack.Count - index);
                            }

                            i++;
                        }
                        else
                        {
                            var index = stack.FindLastIndex(e => e.Kind == Kind.Begin || e.Kind == Kind.Case);
                            if (index >= 0)
                            {
                                stack[index].Group.Add(token);
                                if (!result.Contains(stack[index].Group))
                                {
                                    result.Add(stack[index].Group);
                                }

                                stack.RemoveRange(index, stack.Count - index);
                            }
                        }

                        break;
                    case "CASE":
                        {
                            var group = new List<Token> { token };
                            stack.Add(new Entry { Kind = Kind.Case, Group = group });
                            break;
                        }

                    case "WHEN":
                    case "THEN":
                        if (stack.Count > 0 && stack[stack.Count - 1].Kind == Kind.Case)
                        {
                            stack[stack.Count - 1].Group.Add(token);
                        }

                        break;
                    case "ELSE":
                        if (stack.Count > 0 && stack[stack.Count - 1].Kind == Kind.Case)
                        {
                            stack[stack.Count - 1].Group.Add(token);
                        }
                        else
                        {
                            var index = stack.FindLastIndex(e => e.Kind == Kind.If);
                            var barrier = stack.FindLastIndex(e => e.Kind != Kind.If && e.Kind != Kind.While);
                            if (index >= 0 && index > barrier)
                            {
                                stack[index].Group.Add(token);
                                stack.RemoveRange(index, stack.Count - index);
                            }
                        }

                        break;
                    case "IF":
                        {
                            var group = new List<Token> { token };
                            result.Add(group);
                            stack.Add(new Entry { Kind = Kind.If, Group = group });
                            break;
                        }

                    case "WHILE":
                        {
                            var group = new List<Token> { token };
                            result.Add(group);
                            stack.Add(new Entry { Kind = Kind.While, Group = group, ParenDepth = depth, Pending = true });
                            break;
                        }

                    case "COMMIT":
                    case "ROLLBACK":
                        if (transactions.Count > 0)
                        {
                            var group = transactions[transactions.Count - 1];
                            transactions.RemoveAt(transactions.Count - 1);
                            group.Add(token);
                            result.Add(group);
                            if (At(i + 1) != null && At(i + 1).Kind == TokenKind.Word && (Is(i + 1, "TRAN") || Is(i + 1, "TRANSACTION") || Is(i + 1, "WORK")))
                            {
                                group.Add(t[i + 1]);
                                i++;
                            }
                        }

                        break;
                    default:
                        if (waiting != null && StatementStarters.Contains(token.Upper))
                        {
                            // The body of the WHILE is a single statement, not a block.
                            waiting.Pending = false;
                        }

                        break;
                }
            }

            return result;
        }
    }
}
