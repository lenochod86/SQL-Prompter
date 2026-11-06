using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>The keywords that can follow at a position: the likeliest first, then the others that are possible there.</summary>
    internal sealed class KeywordPosition
    {
        public static readonly KeywordPosition None = new KeywordPosition(Array.Empty<string>(), Array.Empty<string>(), false);

        public KeywordPosition(IReadOnlyList<string> first, IReadOnlyList<string> then, bool exclusive)
        {
            First = first;
            Then = then;
            Exclusive = exclusive;
        }

        /// <summary>The keywords offered before everything else (for a list of one, the keyword can be preselected).</summary>
        public IReadOnlyList<string> First { get; }

        /// <summary>Keywords that are possible here, offered after <see cref="First"/>.</summary>
        public IReadOnlyList<string> Then { get; }

        /// <summary>Only these keywords can follow: nothing else is offered (table hints, a CTE).</summary>
        public bool Exclusive { get; }

        public bool IsEmpty => First.Count == 0 && Then.Count == 0;
    }

    /// <summary>
    /// The map of positions of T-SQL and the keywords that can follow there (the grammar as it is usually written): after a select item
    /// FROM, after a table source WHERE or JOIN, after a CTE name AS and so on. The position is read from the tokens of the current block
    /// before the word being typed.
    /// </summary>
    internal static class KeywordMap
    {
        public static readonly string[] StatementFirst = { "SELECT", "INSERT INTO", "UPDATE", "DELETE FROM", "WITH", "DECLARE", "SET", "EXEC", "IF", "BEGIN" };

        private static readonly string[] Joins = { "JOIN", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN", "CROSS APPLY", "OUTER APPLY" };
        private static readonly string[] MoreJoins = { "LEFT OUTER JOIN", "RIGHT OUTER JOIN", "FULL OUTER JOIN" };
        private static readonly string[] QueryTails = { "UNION", "UNION ALL", "EXCEPT", "INTERSECT", "OPTION", "FOR XML", "FOR JSON" };
        private static readonly string[] DmlTails = { "OPTION" };
        private static readonly string[] Comparison = { "=", "<>", "!=", "<", ">", "<=", ">=", "!<", "!>" };

        public static readonly string[] TableHints =
        {
            "NOLOCK", "READPAST", "UPDLOCK", "ROWLOCK", "HOLDLOCK", "TABLOCK", "TABLOCKX", "XLOCK", "PAGLOCK", "NOWAIT", "READUNCOMMITTED",
            "READCOMMITTED", "REPEATABLEREAD", "SERIALIZABLE", "FORCESEEK", "NOEXPAND", "INDEX",
        };

        private static KeywordPosition Of(string[] first, string[] then = null, bool exclusive = false) =>
            new KeywordPosition(first, then ?? Array.Empty<string>(), exclusive);

        /// <summary>The keywords that can follow the tokens before <paramref name="nameStart"/>; <see cref="KeywordPosition.None"/> where nothing is known.</summary>
        /// <param name="afterCaret">The index of the first token after the word being typed.</param>
        public static KeywordPosition At(ContextAnalyzer.Structure s, ContextAnalyzer.Scope block, int nameStart, int afterCaret)
        {
            var tokens = s.Tokens;
            var anchorIndex = nameStart - 1;
            if (anchorIndex < block.Start || s.Depth[anchorIndex] != block.Depth)
            {
                return KeywordPosition.None;
            }

            // The tokens of the block itself; a parenthesized group stands as its closing parenthesis.
            var top = new List<Token>();
            for (var i = block.Start; i < nameStart; i++)
            {
                if (s.Depth[i] != block.Depth)
                {
                    continue;
                }

                var close = tokens[i].Kind == TokenKind.OpenParen ? s.Match[i] : -1;
                if (close >= 0 && close < nameStart)
                {
                    top.Add(tokens[close]);
                    i = close;
                }
                else
                {
                    top.Add(tokens[i]);
                }
            }

            if (top.Count == 0)
            {
                return KeywordPosition.None;
            }

            var followingFrom = false;
            for (var i = afterCaret; i < block.End && i < s.StatementEnd; i++)
            {
                if (s.Depth[i] == block.Depth && tokens[i].IsWord("FROM"))
                {
                    followingFrom = true;
                    break;
                }
            }

            if (top.Count == 1 && top[0].IsWord("BEGIN") && anchorIndex > 0 && tokens[anchorIndex - 1].IsWord("TRY"))
            {
                // END TRY BEGIN CATCH: the statement splitter starts a statement at BEGIN.
                return Of(new[] { "CATCH" }, null, true);
            }

            return Resolve(top, followingFrom);
        }

        private static string W(IReadOnlyList<Token> top, int i) => i >= 0 && i < top.Count ? top[i].Upper : null;

        private static bool IsOperandToken(Token t) => ContextAnalyzer.IsOperand(t);

        private static KeywordPosition Resolve(List<Token> top, bool followingFrom)
        {
            var anchor = top[top.Count - 1];

            // CREATE, ALTER, DROP
            var head = W(top, 0);
            if (top.Count == 1 && (head == "CREATE" || head == "ALTER" || head == "DROP"))
            {
                var kinds = new[] { "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "INDEX", "TRIGGER" };
                return head == "CREATE"
                    ? Of(new[] { "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "INDEX", "TRIGGER", "OR ALTER" }, new[] { "SCHEMA", "DATABASE", "TYPE", "SEQUENCE", "UNIQUE INDEX", "CLUSTERED INDEX", "NONCLUSTERED INDEX" })
                    : Of(kinds, new[] { "SCHEMA", "DATABASE", "TYPE", "SEQUENCE" });
            }

            if (head == "CREATE" && top.Count == 2 && anchor.IsWord("OR"))
            {
                return Of(new[] { "ALTER" }, null, true);
            }

            if (head == "DROP" && top.Count == 2 && new[] { "TABLE", "VIEW", "PROCEDURE", "PROC", "FUNCTION", "TRIGGER", "INDEX", "SCHEMA", "DATABASE" }.Contains(W(top, 1)))
            {
                return Of(new[] { "IF EXISTS" });
            }

            // BEGIN
            if (anchor.IsWord("BEGIN"))
            {
                return Of(new[] { "TRANSACTION", "TRAN", "TRY" }, new[] { "CATCH", "DISTRIBUTED TRANSACTION" }.Concat(StatementFirst.Where(k => k != "BEGIN")).ToArray());
            }

            // WITH cte AS (query) ...: the part before the main statement.
            var start = 0;
            if (head == "WITH")
            {
                var cte = Cte(top, out start);
                if (cte != null)
                {
                    return cte;
                }
            }

            var part = start == 0 ? top : top.GetRange(start, top.Count - start);
            if (part.Count == 0)
            {
                return KeywordPosition.None;
            }

            // CASE ... END
            var caseState = CaseState(part, out var endedCase);
            anchor = part[part.Count - 1];
            var operand = IsOperandToken(anchor) || endedCase;
            if (caseState != null)
            {
                return InCase(part, caseState, anchor, operand);
            }

            return InStatement(part, anchor, operand, followingFrom);
        }

        /// <summary>The state of a CTE list at the end of the tokens; null once the main statement has started (<paramref name="mainStart"/> is its first token).</summary>
        private static KeywordPosition Cte(List<Token> top, out int mainStart)
        {
            mainStart = 0;
            var state = "name";
            var i = 1;
            for (; i < top.Count; i++)
            {
                var t = top[i];
                switch (state)
                {
                    case "name":
                        state = t.IsNamePart ? "afterName" : "?";
                        break;
                    case "afterName":
                        state = t.IsWord("AS") ? "afterAs" : t.Kind == TokenKind.CloseParen ? "afterColumns" : "?";
                        break;
                    case "afterColumns":
                        state = t.IsWord("AS") ? "afterAs" : "?";
                        break;
                    case "afterAs":
                        state = t.Kind == TokenKind.CloseParen ? "afterQuery" : t.Kind == TokenKind.OpenParen && i == top.Count - 1 ? "afterAs" : "?";
                        break;
                    case "afterQuery":
                        if (t.Kind == TokenKind.Comma)
                        {
                            state = "name";
                        }
                        else
                        {
                            mainStart = i;
                            return null;
                        }

                        break;
                }

                if (state == "?")
                {
                    return KeywordPosition.None;
                }
            }

            switch (state)
            {
                case "afterName":
                case "afterColumns":
                    return Of(new[] { "AS" }, null, true);
                case "afterAs":
                    return top[top.Count - 1].Kind == TokenKind.OpenParen ? Of(new[] { "SELECT" }, null, true) : KeywordPosition.None;
                case "afterQuery":
                    return Of(new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE" }, null, true);
                default:
                    return KeywordPosition.None;
            }
        }

        /// <summary>The innermost open CASE: "CASE", "WHEN", "THEN" or "ELSE"; null outside of CASE.</summary>
        private static string CaseState(List<Token> part, out bool endedCase)
        {
            endedCase = false;
            var stack = new Stack<string>();
            for (var i = 0; i < part.Count; i++)
            {
                var word = part[i].Upper;
                endedCase = false;
                switch (word)
                {
                    case "CASE":
                        stack.Push("CASE");
                        break;
                    case "WHEN":
                    case "THEN":
                    case "ELSE":
                        if (stack.Count > 0)
                        {
                            stack.Pop();
                            stack.Push(word);
                        }

                        break;
                    case "END":
                        if (stack.Count > 0)
                        {
                            stack.Pop();
                            endedCase = i == part.Count - 1;
                        }

                        break;
                }
            }

            return stack.Count > 0 ? stack.Peek() : null;
        }

        private static KeywordPosition InCase(List<Token> part, string state, Token anchor, bool operand)
        {
            if (anchor.IsWord("CASE"))
            {
                return Of(new[] { "WHEN" });
            }

            if (!operand)
            {
                return KeywordPosition.None;
            }

            switch (state)
            {
                case "CASE":
                    return Of(new[] { "WHEN" });
                case "WHEN":
                    var from = part.FindLastIndex(t => t.IsWord("WHEN"));
                    var condition = Condition(part, from + 1);
                    return condition == ConditionState.Complete ? Of(new[] { "THEN" }, new[] { "AND", "OR" })
                        : condition == ConditionState.Between ? Of(new[] { "AND" })
                        : KeywordPosition.None;
                case "THEN":
                    return Of(new[] { "WHEN", "ELSE", "END" });
                case "ELSE":
                    return Of(new[] { "END" });
                default:
                    return KeywordPosition.None;
            }
        }

        private enum ConditionState
        {
            /// <summary>An operand without an operator yet.</summary>
            Open,

            /// <summary>BETWEEN a, waiting for AND.</summary>
            Between,

            /// <summary>A complete comparison: what may follow is AND, OR or the next clause.</summary>
            Complete,
        }

        /// <summary>The state of the condition that ends the tokens: the part after the last AND or OR (a BETWEEN's own AND does not count).</summary>
        private static ConditionState Condition(List<Token> part, int from)
        {
            var hasOperator = false;
            var between = false;
            for (var i = from; i < part.Count; i++)
            {
                var t = part[i];
                var word = t.Upper;
                if (word == "AND" && between)
                {
                    between = false;
                    hasOperator = true;
                    continue;
                }

                if (word == "AND" || word == "OR")
                {
                    hasOperator = false;
                    between = false;
                    continue;
                }

                if (word == "BETWEEN")
                {
                    between = true;
                    continue;
                }

                if (word == "IN" || word == "LIKE" || word == "IS" || word == "EXISTS" || word == "ALL" || word == "ANY" || word == "SOME" ||
                    (t.Kind == TokenKind.Operator && Comparison.Contains(t.Text)))
                {
                    hasOperator = true;
                }
            }

            return between ? ConditionState.Between : hasOperator ? ConditionState.Complete : ConditionState.Open;
        }

        private static KeywordPosition InStatement(List<Token> part, Token anchor, bool operand, bool followingFrom)
        {
            // The clause the end of the tokens is in.
            string clause = null;
            var clauseIndex = -1;
            var head = W(part, 0);
            for (var i = 0; i < part.Count; i++)
            {
                var c = ClauseOf(part, i);
                if (c != null)
                {
                    clause = c;
                    clauseIndex = i;
                }
            }

            if (clause == null)
            {
                return KeywordPosition.None;
            }

            var query = head == "SELECT" || head == "WITH" || clause == "SELECT" || clause == "SETOP";
            var tails = query ? QueryTails : DmlTails;

            switch (clause)
            {
                case "SELECT":
                    if (clauseIndex == part.Count - 1)
                    {
                        return Of(new[] { "TOP", "DISTINCT" });
                    }

                    if (!operand || part[part.Count - 2].IsWord("TOP") || part[part.Count - 2].IsWord("DISTINCT") || part[part.Count - 2].IsWord("ALL"))
                    {
                        return KeywordPosition.None;
                    }

                    if (anchor.Kind == TokenKind.CloseParen && part.Count >= 3 && part[part.Count - 2].IsWord("TOP"))
                    {
                        return KeywordPosition.None;
                    }

                    var aliased = (anchor.Kind == TokenKind.Word || anchor.Kind == TokenKind.QuotedIdentifier) && !anchor.IsWord("END") && part.Count >= 2 &&
                        (part[part.Count - 2].IsWord("AS") || (IsOperandToken(part[part.Count - 2]) && part[part.Count - 2].Kind != TokenKind.Dot));
                    if (followingFrom)
                    {
                        return aliased ? KeywordPosition.None : Of(new[] { "AS" });
                    }

                    return aliased ? Of(new[] { "FROM", "INTO" }) : Of(new[] { "FROM", "AS", "INTO" });

                case "INTO":
                    if (!operand)
                    {
                        return KeywordPosition.None;
                    }

                    return head == "INSERT" || head == "MERGE"
                        ? Of(new[] { "VALUES", "SELECT", "EXEC" }, new[] { "DEFAULT VALUES", "OUTPUT" })
                        : Of(new[] { "FROM" });

                case "FROM":
                    return InFrom(part, clauseIndex, anchor, operand, head, tails);

                case "ON":
                case "WHERE":
                case "HAVING":
                    if (!operand)
                    {
                        return KeywordPosition.None;
                    }

                    var condition = Condition(part, clauseIndex + 1);
                    if (condition == ConditionState.Between)
                    {
                        return Of(new[] { "AND" });
                    }

                    if (condition != ConditionState.Complete)
                    {
                        return KeywordPosition.None;
                    }

                    if (clause == "ON")
                    {
                        return Of(new[] { "AND", "OR" }, Joins.Concat(new[] { "WHERE" }).Concat(tails).ToArray());
                    }

                    if (clause == "HAVING")
                    {
                        return Of(new[] { "AND", "OR", "ORDER BY" }, tails);
                    }

                    return query
                        ? Of(new[] { "AND", "OR", "GROUP BY", "ORDER BY" }, tails)
                        : Of(new[] { "AND", "OR" }, new[] { "OUTPUT", "OPTION" });

                case "GROUP":
                    return operand ? Of(new[] { "HAVING", "ORDER BY" }, tails) : KeywordPosition.None;

                case "ORDER":
                    if (anchor.IsWord("ASC") || anchor.IsWord("DESC"))
                    {
                        return Of(new[] { "OFFSET" }, tails);
                    }

                    return operand ? Of(new[] { "ASC", "DESC", "OFFSET" }, tails) : KeywordPosition.None;

                case "SETOP":
                    if (anchor.IsWord("UNION"))
                    {
                        return Of(new[] { "ALL", "SELECT" });
                    }

                    return anchor.IsWord("ALL") || anchor.IsWord("EXCEPT") || anchor.IsWord("INTERSECT") ? Of(new[] { "SELECT" }) : KeywordPosition.None;

                case "UPDATE":
                    return operand ? Of(new[] { "SET" }) : KeywordPosition.None;

                case "SET":
                    if (!operand || head != "UPDATE")
                    {
                        return KeywordPosition.None;
                    }

                    var assigned = false;
                    for (var i = part.Count - 1; i > clauseIndex; i--)
                    {
                        if (part[i].Kind == TokenKind.Comma)
                        {
                            break;
                        }

                        if (part[i].Kind == TokenKind.Operator && part[i].Text.EndsWith("=", StringComparison.Ordinal))
                        {
                            assigned = true;
                        }
                    }

                    return assigned ? Of(new[] { "FROM", "WHERE" }, new[] { "OUTPUT", "OPTION" }) : KeywordPosition.None;

                case "DELETE":
                    return operand && clauseIndex == 0 && part.Count == 2 ? Of(new[] { "FROM", "WHERE" }, new[] { "OUTPUT", "OPTION" }) : KeywordPosition.None;

                default:
                    return KeywordPosition.None;
            }
        }

        private static KeywordPosition InFrom(List<Token> part, int clauseIndex, Token anchor, bool operand, string head, string[] tails)
        {
            // The table source the end of the tokens belongs to: after the last FROM, JOIN, APPLY or comma.
            var segmentStart = clauseIndex;
            for (var i = clauseIndex; i < part.Count; i++)
            {
                if (part[i].IsWord("FROM") || part[i].IsWord("JOIN") || part[i].IsWord("APPLY") || part[i].Kind == TokenKind.Comma)
                {
                    segmentStart = i;
                }
            }

            var segment = part.GetRange(segmentStart + 1, part.Count - segmentStart - 1);
            if (segment.Count == 0)
            {
                return KeywordPosition.None;
            }

            var starter = part[segmentStart];
            var hintsOpen = anchor.Kind == TokenKind.OpenParen && segment.Count >= 2 && segment[segment.Count - 2].IsWord("WITH");
            if (hintsOpen)
            {
                return Of(TableHints, null, true);
            }

            // WITH ( hints ) – a group that follows WITH.
            var afterHints = anchor.Kind == TokenKind.CloseParen && segment.Count >= 2 && segment.Any(t => t.IsWord("WITH"));
            var aliased = !afterHints && (anchor.Kind == TokenKind.Word || anchor.Kind == TokenKind.QuotedIdentifier) && segment.Count >= 2 &&
                (segment[segment.Count - 2].IsWord("AS") ||
                 (IsOperandToken(segment[segment.Count - 2]) && segment[segment.Count - 2].Kind != TokenKind.Dot));
            if (!afterHints && !operand)
            {
                return KeywordPosition.None;
            }

            if (starter.IsWord("FROM") && segmentStart == 1 && head == "DELETE")
            {
                // DELETE FROM table: no joins here.
                return Of(new[] { "WHERE" }, new[] { "OUTPUT", "OPTION", "AS", "WITH" });
            }

            var crossJoin = starter.IsWord("JOIN") && segmentStart >= 1 && part[segmentStart - 1].IsWord("CROSS");
            var needsOn = starter.IsWord("JOIN") && !crossJoin;
            var joins = Joins;

            if (needsOn)
            {
                if (afterHints || aliased)
                {
                    return Of(new[] { "ON" });
                }

                return Of(new[] { "ON", "AS" }, new[] { "WITH" });
            }

            var then = MoreJoins.Concat(new[] { "GROUP BY", "ORDER BY" }.Where(k => tails == QueryTails)).Concat(tails).Distinct().ToArray();
            if (afterHints || aliased)
            {
                return Of(new[] { "WHERE" }.Concat(joins).Concat(afterHints ? Array.Empty<string>() : new[] { "WITH" }).ToArray(), then);
            }

            return Of(new[] { "AS", "WITH", "WHERE" }.Concat(joins).ToArray(), then);
        }

        /// <summary>The clause the token starts, or null if it does not start one at the block level.</summary>
        private static string ClauseOf(List<Token> part, int i)
        {
            var t = part[i];
            switch (t.Upper)
            {
                case "SELECT":
                    return "SELECT";
                case "FROM":
                case "JOIN":
                case "APPLY":
                case "USING":
                    return "FROM";
                case "ON":
                    return "ON";
                case "WHERE":
                    return "WHERE";
                case "GROUP":
                    return "GROUP";
                case "HAVING":
                    return "HAVING";
                case "ORDER":
                    return "ORDER";
                case "UNION":
                case "EXCEPT":
                case "INTERSECT":
                    return "SETOP";
                case "UPDATE":
                    return "UPDATE";
                case "SET":
                    return W(part, 0) == "UPDATE" ? "SET" : null;
                case "DELETE":
                    return "DELETE";
                case "INTO":
                    return "INTO";
                case "VALUES":
                case "OUTPUT":
                case "OPTION":
                case "EXEC":
                case "EXECUTE":
                    return "OTHER";
                default:
                    return null;
            }
        }
    }
}
