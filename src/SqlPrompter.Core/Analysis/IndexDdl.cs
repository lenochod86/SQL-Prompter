using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>
    /// The positions of index DDL in <see cref="KeywordMap"/>: CREATE [UNIQUE] [CLUSTERED | NONCLUSTERED] [COLUMNSTORE] INDEX, ALTER INDEX
    /// and DROP INDEX (#197). Other DDL statements (CREATE TABLE column definitions and constraints, ALTER TABLE ADD / ALTER COLUMN,
    /// CREATE STATISTICS) still fall back to the generic keywords; they are listed in a follow-up issue of #197.
    /// </summary>
    internal static class IndexDdl
    {
        public static readonly string[] Options =
        {
            "PAD_INDEX", "FILLFACTOR", "SORT_IN_TEMPDB", "IGNORE_DUP_KEY", "STATISTICS_NORECOMPUTE", "STATISTICS_INCREMENTAL", "DROP_EXISTING",
            "ONLINE", "RESUMABLE", "MAX_DURATION", "ALLOW_ROW_LOCKS", "ALLOW_PAGE_LOCKS", "OPTIMIZE_FOR_SEQUENTIAL_KEY", "MAXDOP",
            "DATA_COMPRESSION", "XML_COMPRESSION",
        };

        private static readonly string[] Compression = { "NONE", "ROW", "PAGE", "COLUMNSTORE", "COLUMNSTORE_ARCHIVE" };
        private static readonly string[] Numeric = { "FILLFACTOR", "MAXDOP", "MAX_DURATION" };
        private static readonly string[] Modifiers = { "UNIQUE", "CLUSTERED", "NONCLUSTERED", "COLUMNSTORE" };

        private static KeywordPosition Of(string[] first, string[] then = null, bool exclusive = false) =>
            new KeywordPosition(first, then ?? Array.Empty<string>(), exclusive);

        /// <summary>The index of INDEX in CREATE [UNIQUE] [CLUSTERED | NONCLUSTERED] [COLUMNSTORE] INDEX, ALTER INDEX or DROP INDEX; -1 when the statement is no index DDL.</summary>
        public static int IndexKeyword(IReadOnlyList<Token> tokens, int statementStart)
        {
            if (statementStart < 0 || statementStart >= tokens.Count)
            {
                return -1;
            }

            var head = tokens[statementStart].Upper;
            if (head != "CREATE" && head != "ALTER" && head != "DROP")
            {
                return -1;
            }

            for (var i = statementStart + 1; i < tokens.Count && i <= statementStart + 4; i++)
            {
                if (tokens[i].IsWord("INDEX"))
                {
                    return i;
                }

                if (head != "CREATE" || !Modifiers.Contains(tokens[i].Upper))
                {
                    return -1;
                }
            }

            return -1;
        }

        /// <summary>The keywords after CREATE and some of its modifiers (no INDEX yet); null when the tokens are no start of an index statement.</summary>
        public static KeywordPosition Modifier(List<Token> top)
        {
            if (top.Count < 2 || !top[0].IsWord("CREATE"))
            {
                return null;
            }

            var seen = top.Skip(1).Select(t => t.Upper).ToList();
            if (seen.Any(w => !Modifiers.Contains(w)) || seen.Distinct().Count() != seen.Count)
            {
                return null;
            }

            if (seen[seen.Count - 1] == "UNIQUE")
            {
                return Of(new[] { "CLUSTERED", "NONCLUSTERED", "INDEX" });
            }

            return seen.Contains("COLUMNSTORE") ? Of(new[] { "INDEX" }, null, true) : Of(new[] { "INDEX", "COLUMNSTORE" });
        }

        /// <summary>The keywords after the tokens of an index statement (a parenthesized group stands as its closing parenthesis); null when not known.</summary>
        public static KeywordPosition After(List<Token> top)
        {
            var ix = IndexKeyword(top, 0);
            if (ix < 0)
            {
                return null;
            }

            var head = top[0].Upper;
            var rest = top.GetRange(ix + 1, top.Count - ix - 1);
            var i = 0;
            if (head == "DROP" && rest.Count >= 2 && rest[0].IsWord("IF") && rest[1].IsWord("EXISTS"))
            {
                i = 2;
            }

            if (rest.Count <= i)
            {
                return head == "ALTER" ? Of(Array.Empty<string>(), new[] { "ALL" }) : null;
            }

            if (rest.Count == i + 1)
            {
                return rest[i].Kind == TokenKind.Dot || rest[i].IsWord("IF") ? null : Of(new[] { "ON" }, null, true);
            }

            if (!rest[i + 1].IsWord("ON"))
            {
                return null;
            }

            // The table: a name of up to four parts.
            var q = i + 2;
            while (q < rest.Count && (rest[q].Kind == TokenKind.Word || rest[q].Kind == TokenKind.QuotedIdentifier))
            {
                q++;
                if (q < rest.Count && rest[q].Kind == TokenKind.Dot)
                {
                    q++;
                }
                else
                {
                    break;
                }
            }

            if (q == i + 2 || rest[q - 1].Kind == TokenKind.Dot)
            {
                return null;
            }

            var tail = rest.GetRange(q, rest.Count - q);
            if (tail.Count == 0)
            {
                return AfterTable(head, top);
            }

            switch (head)
            {
                case "CREATE":
                    return AfterColumnList(tail);
                case "ALTER":
                    if (tail.Count == 1 && tail[0].IsWord("REBUILD"))
                    {
                        return Of(new[] { "WITH", "PARTITION" });
                    }

                    return tail.Count == 1 && tail[0].IsWord("REORGANIZE") ? Of(new[] { "PARTITION", "WITH" }) : null;
                default:
                    return null;
            }
        }

        private static KeywordPosition AfterTable(string head, List<Token> top)
        {
            switch (head)
            {
                case "ALTER":
                    return Of(new[] { "REBUILD", "REORGANIZE", "DISABLE", "SET" }, new[] { "RESUME", "PAUSE", "ABORT" });
                case "DROP":
                    return Of(new[] { "WITH" });
                default:
                    // A clustered columnstore index has no column list; the others need a parenthesis, which no keyword can give.
                    return top.Any(t => t.IsWord("COLUMNSTORE")) && top.Any(t => t.IsWord("CLUSTERED")) ? Of(new[] { "WITH", "ON" }) : null;
            }
        }

        /// <summary>After the key column list of CREATE INDEX: INCLUDE (...), WHERE ..., WITH (...), ON ...</summary>
        private static KeywordPosition AfterColumnList(List<Token> tail)
        {
            if (tail[0].Kind != TokenKind.CloseParen)
            {
                return null;
            }

            var r = 1;
            if (tail.Count == r)
            {
                return Of(new[] { "INCLUDE", "WHERE", "WITH", "ON" });
            }

            if (tail[r].IsWord("INCLUDE"))
            {
                if (tail.Count == r + 1 || tail[r + 1].Kind != TokenKind.CloseParen)
                {
                    return null;
                }

                r += 2;
                if (tail.Count == r)
                {
                    return Of(new[] { "WHERE", "WITH", "ON" });
                }
            }

            // After WITH (...) only the filegroup ON can follow. After a filter predicate the end is not known, so nothing is decided there.
            return tail[r].IsWord("WITH") && tail.Count == r + 2 && tail[r + 1].Kind == TokenKind.CloseParen ? Of(new[] { "ON" }) : null;
        }

        /// <summary>The caret is inside parentheses of an index statement: the key column list (ASC, DESC) or the options after WITH or SET.</summary>
        public static KeywordPosition Inside(List<Token> statement, List<Token> inner)
        {
            if (IndexKeyword(statement, 0) < 0)
            {
                return null;
            }

            var before = statement[statement.Count - 1];
            if (before.IsWord("WITH") || before.IsWord("SET"))
            {
                return Option(inner);
            }

            if (statement[0].IsWord("CREATE") && !before.IsWord("INCLUDE") && inner.Count >= 1)
            {
                var last = inner[inner.Count - 1];
                var previous = inner.Count >= 2 ? inner[inner.Count - 2] : null;
                if ((last.Kind == TokenKind.Word || last.Kind == TokenKind.QuotedIdentifier) && !last.IsWord("ASC") && !last.IsWord("DESC") &&
                    (previous == null || previous.Kind == TokenKind.Comma))
                {
                    return Of(new[] { "ASC", "DESC" });
                }
            }

            return null;
        }

        private static KeywordPosition Option(List<Token> inner)
        {
            var last = inner.Count > 0 ? inner[inner.Count - 1] : null;
            if (last == null || last.Kind == TokenKind.Comma)
            {
                return Of(Options, null, true);
            }

            if (last.Kind == TokenKind.Operator && last.Text == "=" && inner.Count >= 2)
            {
                var option = inner[inner.Count - 2].Upper;
                if (option == "DATA_COMPRESSION")
                {
                    return Of(Compression, null, true);
                }

                return Numeric.Contains(option) ? null : Of(new[] { "ON", "OFF" }, null, true);
            }

            return null;
        }
    }
}
