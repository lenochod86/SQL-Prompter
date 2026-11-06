using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>The positions of SET options and of the query hints of OPTION ( ... ) in <see cref="KeywordMap"/> (#218).</summary>
    internal static class SetAndHints
    {
        public static readonly string[] QueryHints =
        {
            "RECOMPILE", "MAXDOP", "OPTIMIZE FOR", "OPTIMIZE FOR UNKNOWN", "HASH JOIN", "LOOP JOIN", "MERGE JOIN", "FORCE ORDER", "USE HINT",
            "MAXRECURSION", "FAST", "KEEP PLAN", "KEEPFIXED PLAN", "EXPAND VIEWS", "ROBUST PLAN", "NO_PERFORMANCE_SPOOL", "QUERYTRACEON",
        };

        private static readonly string[] SetFirst =
        {
            "NOCOUNT ON", "XACT_ABORT ON", "TRANSACTION ISOLATION LEVEL", "ANSI_NULLS ON", "QUOTED_IDENTIFIER ON", "STATISTICS IO ON", "STATISTICS TIME ON",
            "IDENTITY_INSERT", "DATEFORMAT", "LANGUAGE",
        };

        private static readonly string[] SetThen =
        {
            "STATISTICS XML ON", "STATISTICS PROFILE ON", "ANSI_WARNINGS ON", "ANSI_PADDING ON", "ARITHABORT ON", "NOEXEC", "ROWCOUNT", "LOCK_TIMEOUT", "DEADLOCK_PRIORITY",
            "CONCAT_NULL_YIELDS_NULL ON", "NUMERIC_ROUNDABORT OFF", "FMTONLY",
        };

        private static readonly string[] Switches =
        {
            "NOCOUNT", "XACT_ABORT", "ANSI_NULLS", "QUOTED_IDENTIFIER", "ANSI_WARNINGS", "ANSI_PADDING", "ARITHABORT", "NOEXEC", "CONCAT_NULL_YIELDS_NULL",
            "NUMERIC_ROUNDABORT", "FMTONLY", "IMPLICIT_TRANSACTIONS", "PARSEONLY", "ANSI_DEFAULTS", "CURSOR_CLOSE_ON_COMMIT",
        };

        private static KeywordPosition Of(string[] first, string[] then = null, bool exclusive = false) =>
            new KeywordPosition(first, then ?? Array.Empty<string>(), exclusive);

        /// <summary>The keywords after the tokens of a SET statement (one level, a group stands as its closing parenthesis); null when not a SET option position.</summary>
        public static KeywordPosition AfterSet(List<Token> top)
        {
            if (top.Count == 0 || !top[0].IsWord("SET"))
            {
                return null;
            }

            if (top.Count == 1)
            {
                return Of(SetFirst, SetThen);
            }

            var option = top[1].Upper;
            if (option == null)
            {
                return null;
            }

            if (top.Count == 2)
            {
                if (Switches.Contains(option))
                {
                    return Of(new[] { "ON", "OFF" }, null, true);
                }

                switch (option)
                {
                    case "TRANSACTION":
                        return Of(new[] { "ISOLATION LEVEL" }, null, true);
                    case "STATISTICS":
                        return Of(new[] { "IO", "TIME", "XML", "PROFILE" }, null, true);
                    case "DATEFORMAT":
                        return Of(new[] { "dmy", "dym", "mdy", "myd", "ydm", "ymd" }, null, true);
                    default:
                        return null;
                }
            }

            if (option == "TRANSACTION" && top[2].IsWord("ISOLATION"))
            {
                if (top.Count == 3)
                {
                    return Of(new[] { "LEVEL" }, null, true);
                }

                return top.Count == 4 && top[3].IsWord("LEVEL")
                    ? Of(new[] { "READ UNCOMMITTED", "READ COMMITTED", "REPEATABLE READ", "SNAPSHOT", "SERIALIZABLE" }, null, true)
                    : null;
            }

            if (option == "STATISTICS" && top.Count == 3 && (top[2].IsWord("IO") || top[2].IsWord("TIME") || top[2].IsWord("XML") || top[2].IsWord("PROFILE")))
            {
                return Of(new[] { "ON", "OFF" }, null, true);
            }

            if (option == "IDENTITY_INSERT" && top.Count >= 3 && (top[top.Count - 1].Kind == TokenKind.Word || top[top.Count - 1].Kind == TokenKind.QuotedIdentifier) &&
                !top[top.Count - 1].IsWord("ON") && !top[top.Count - 1].IsWord("OFF"))
            {
                return Of(new[] { "ON", "OFF" }, null, true);
            }

            return null;
        }

        /// <summary>Inside OPTION ( ... ): the hints at the start and after a comma; nothing after a hint with a value.</summary>
        public static KeywordPosition InOption(List<Token> inner)
        {
            var last = inner.Count > 0 ? inner[inner.Count - 1] : null;
            return last == null || last.Kind == TokenKind.Comma ? Of(QueryHints, null, true) : null;
        }
    }
}
