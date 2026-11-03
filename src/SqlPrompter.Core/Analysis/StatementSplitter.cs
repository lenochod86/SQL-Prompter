using System;
using System.Collections.Generic;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>
    /// Finds statement starts in tokens without comments. T-SQL does not require semicolons,
    /// so a keyword at the top parenthesis level also starts a statement
    /// unless it continues the previous one (INSERT … SELECT, UPDATE … SET, CASE … END …).
    /// </summary>
    internal static class StatementSplitter
    {
        private static readonly HashSet<string> Starters = new HashSet<string>(StringComparer.Ordinal)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "DECLARE", "SET", "IF", "ELSE", "WHILE",
            "BEGIN", "END", "EXEC", "EXECUTE", "PRINT", "RETURN", "USE", "TRUNCATE", "CREATE", "ALTER", "DROP",
            "GRANT", "DENY", "REVOKE", "COMMIT", "ROLLBACK", "SAVE", "RAISERROR", "THROW", "GOTO", "BREAK",
            "CONTINUE", "WAITFOR", "OPEN", "CLOSE", "FETCH", "DEALLOCATE", "GO",
        };

        private static readonly HashSet<string> Dml = new HashSet<string>(StringComparer.Ordinal)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
        };

        /// <summary>Objects whose body after AS is a separate statement or block of statements.</summary>
        private static readonly HashSet<string> ObjectsWithBody = new HashSet<string>(StringComparer.Ordinal)
        {
            "PROC", "PROCEDURE", "FUNCTION", "TRIGGER", "VIEW",
        };

        /// <summary>Returns the indexes of the first tokens of statements in ascending order; an empty list for empty input.</summary>
        public static List<int> Split(IReadOnlyList<Token> tokens)
        {
            var bounds = new List<int>();
            if (tokens.Count == 0)
            {
                return bounds;
            }

            bounds.Add(0);
            var start = 0;
            var state = new State();
            var depth = 0;
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (i > start && depth == 0 && IsBoundary(tokens, i, state))
                {
                    bounds.Add(i);
                    start = i;
                    state = new State();
                }

                if (i == start)
                {
                    state.Head = token.Upper;
                }

                switch (token.Kind)
                {
                    case TokenKind.OpenParen:
                        depth++;
                        break;
                    case TokenKind.CloseParen:
                        depth = Math.Max(0, depth - 1);
                        break;
                    case TokenKind.Semicolon:
                        if (depth == 0 && i + 1 < tokens.Count)
                        {
                            bounds.Add(i + 1);
                            start = i + 1;
                            state = new State();
                        }

                        break;
                    case TokenKind.Word:
                        var word = token.Upper;
                        if (word == "CASE")
                        {
                            state.CaseDepth++;
                        }
                        else if (word == "END" && state.CaseDepth > 0)
                        {
                            state.CaseDepth--;
                        }

                        if (depth != 0 || i == start)
                        {
                            break;
                        }

                        if (word == "SELECT")
                        {
                            state.SawSelect = true;
                        }

                        if (state.Head == "WITH" && !state.CteMainSeen && Dml.Contains(word))
                        {
                            // The main statement after a CTE decides how the rest behaves (UPDATE … SET).
                            state.CteMainSeen = true;
                            state.Head = word;
                        }

                        if ((state.Head == "CREATE" || state.Head == "ALTER") && state.ObjectType == null &&
                            word != "OR" && word != "ALTER" && word != "UNIQUE" && word != "CLUSTERED" && word != "NONCLUSTERED")
                        {
                            state.ObjectType = word;
                        }
                        else if (word == "AS" && (state.Head == "CREATE" || state.Head == "ALTER") &&
                                 state.ObjectType != null && ObjectsWithBody.Contains(state.ObjectType) && i + 1 < tokens.Count)
                        {
                            bounds.Add(i + 1);
                            start = i + 1;
                            state = new State();
                        }

                        break;
                }
            }

            return bounds;
        }

        private static bool IsBoundary(IReadOnlyList<Token> tokens, int i, State state)
        {
            var token = tokens[i];
            var word = token.Upper;
            if (word == null || !Starters.Contains(word))
            {
                return false;
            }

            var prev = tokens[i - 1];
            var prevWord = prev.Upper;
            var head = state.Head;
            switch (word)
            {
                case "GO":
                    return true;

                case "SELECT":
                    if (prevWord == "UNION" || prevWord == "ALL" || prevWord == "EXCEPT" || prevWord == "INTERSECT" ||
                        prevWord == "FOR" || prevWord == "RETURN")
                    {
                        return false;
                    }

                    if (head == "INSERT" && !state.SawSelect)
                    {
                        return false;
                    }

                    return !(head == "WITH" && !state.CteMainSeen);

                case "INSERT":
                case "UPDATE":
                case "DELETE":
                case "MERGE":
                    if (prev.Kind == TokenKind.Comma || prevWord == "ON" || prevWord == "FOR" || prevWord == "AFTER" ||
                        prevWord == "OF" || prevWord == "INSTEAD")
                    {
                        return false;
                    }

                    if (head == "MERGE" || head == "GRANT" || head == "DENY" || head == "REVOKE")
                    {
                        return false;
                    }

                    return !(head == "WITH" && !state.CteMainSeen);

                case "WITH":
                    if (head == "CREATE" || head == "ALTER" || i + 2 >= tokens.Count)
                    {
                        return false;
                    }

                    // CTE: WITH name AS ( … or WITH name (columns) AS ( …; otherwise WITH (NOLOCK), WITH TIES etc.
                    var next = tokens[i + 1];
                    var afterNext = tokens[i + 2];
                    return next.IsIdentifier && (afterNext.IsWord("AS") || afterNext.Kind == TokenKind.OpenParen);

                case "SET":
                    if (head == "UPDATE" || head == "MERGE" || head == "ALTER" || head == "CREATE")
                    {
                        return false;
                    }

                    return prevWord != "ON" && prevWord != "DELETE" && prevWord != "UPDATE";

                case "EXEC":
                case "EXECUTE":
                    if (head == "INSERT" && !state.SawSelect)
                    {
                        return false;
                    }

                    return head != "GRANT" && head != "DENY" && head != "REVOKE" && head != "CREATE" && head != "ALTER";

                case "END":
                case "ELSE":
                    return state.CaseDepth == 0;

                case "CREATE":
                case "ALTER":
                case "DROP":
                    return head != "CREATE" && head != "ALTER" && prevWord != "OR";

                case "IF":
                    return head != "DROP" && head != "ALTER" && head != "CREATE";

                case "FETCH":
                    return prevWord != "ROWS" && prevWord != "ROW";

                case "OPEN":
                case "CLOSE":
                case "RETURN":
                    return head != "CREATE" && head != "ALTER";

                case "GRANT":
                case "DENY":
                case "REVOKE":
                    return head != "GRANT" && head != "DENY" && head != "REVOKE";

                default:
                    return true;
            }
        }

        private sealed class State
        {
            /// <summary>First word of the statement; for a CTE the main statement after the definitions.</summary>
            public string Head;

            public string ObjectType;

            public bool SawSelect;

            public bool CteMainSeen;

            public int CaseDepth;
        }
    }
}
