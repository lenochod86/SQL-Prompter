using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Sql
{
    /// <summary>
    /// Words that are keywords only in a certain place (a SET option, a table or query hint, OUTPUT, TIES, a global function): they are not reserved, so
    /// the formatter changes their case by position and the suggestion list cases them like the other keywords (#526).
    /// </summary>
    public static class ContextKeywords
    {
        public static readonly IReadOnlyList<string> SetOptions = new[]
        {
            "NOCOUNT", "XACT_ABORT", "ANSI_NULLS", "QUOTED_IDENTIFIER", "ANSI_WARNINGS", "ANSI_PADDING", "ARITHABORT", "NOEXEC", "CONCAT_NULL_YIELDS_NULL",
            "NUMERIC_ROUNDABORT", "FMTONLY", "IMPLICIT_TRANSACTIONS", "PARSEONLY", "ANSI_DEFAULTS", "CURSOR_CLOSE_ON_COMMIT", "ROWCOUNT", "LOCK_TIMEOUT",
            "DEADLOCK_PRIORITY", "DATEFORMAT", "DATEFIRST", "LANGUAGE", "TEXTSIZE", "IDENTITY_INSERT", "STATISTICS", "SHOWPLAN_XML", "SHOWPLAN_TEXT",
            "SHOWPLAN_ALL", "ISOLATION", "LEVEL", "IO", "TIME", "PROFILE", "XML", "ON", "OFF",
        };

        public static readonly IReadOnlyList<string> TableHints = new[]
        {
            "NOLOCK", "READPAST", "UPDLOCK", "ROWLOCK", "HOLDLOCK", "TABLOCK", "TABLOCKX", "XLOCK", "PAGLOCK", "NOWAIT", "READUNCOMMITTED",
            "READCOMMITTED", "REPEATABLEREAD", "SERIALIZABLE", "FORCESEEK", "FORCESCAN", "NOEXPAND", "INDEX", "SNAPSHOT", "READCOMMITTEDLOCK", "NOWAIT",
        };

        public static readonly IReadOnlyList<string> QueryHints = new[]
        {
            "RECOMPILE", "MAXDOP", "OPTIMIZE", "UNKNOWN", "HASH", "ORDER", "GROUP", "LOOP", "MERGE", "CONCAT", "UNION", "JOIN", "FORCE", "USE", "HINT",
            "MAXRECURSION", "FAST", "KEEP", "KEEPFIXED", "PLAN", "EXPAND", "VIEWS", "ROBUST", "NO_PERFORMANCE_SPOOL", "QUERYTRACEON", "FORCE", "EXTERNALPUSHDOWN",
            "DISABLE_OPTIMIZED_PLAN_FORCING", "PARAMETERIZATION", "SIMPLE", "FORCED", "LABEL", "TABLE", "XML", "PARAMETER", "SNIFFING",
        };

        /// <summary>The global functions (<c>@@ROWCOUNT</c>), with the two at signs.</summary>
        public static readonly IReadOnlyList<string> GlobalFunctions = new[]
        {
            "@@ROWCOUNT", "@@ERROR", "@@TRANCOUNT", "@@IDENTITY", "@@VERSION", "@@SERVERNAME", "@@SPID", "@@FETCH_STATUS", "@@DBTS", "@@LANGUAGE",
            "@@LOCK_TIMEOUT", "@@MAX_CONNECTIONS", "@@NESTLEVEL", "@@OPTIONS", "@@PROCID", "@@SERVICENAME", "@@TEXTSIZE", "@@TIMETICKS", "@@CONNECTIONS",
            "@@CPU_BUSY", "@@CURSOR_ROWS", "@@DATEFIRST", "@@IDLE", "@@IO_BUSY", "@@LANGID", "@@MAX_PRECISION", "@@MICROSOFTVERSION", "@@PACKET_ERRORS",
            "@@PACK_RECEIVED", "@@PACK_SENT", "@@REMSERVER", "@@TOTAL_ERRORS", "@@TOTAL_READ", "@@TOTAL_WRITE",
        };

        /// <summary>The table-valued functions that are not reserved words; a parenthesis follows them.</summary>
        public static readonly IReadOnlyList<string> TableFunctions = new[] { "OPENJSON", "STRING_SPLIT", "GENERATE_SERIES", "OPENXML" };

        /// <summary>Words used as keywords next to a parameter or a clause: OUTPUT after a parameter, READONLY, TIES after TOP ... WITH.</summary>
        public static readonly IReadOnlyList<string> Singles = new[] { "OUTPUT", "OUT", "READONLY", "TIES" };

        private static readonly HashSet<string> SetOptionSet = new HashSet<string>(SetOptions, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> TableHintSet = new HashSet<string>(TableHints, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> QueryHintSet = new HashSet<string>(QueryHints, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> GlobalSet = new HashSet<string>(GlobalFunctions, StringComparer.OrdinalIgnoreCase);

        public static bool IsSetOption(string word) => word != null && SetOptionSet.Contains(word);

        public static bool IsTableHint(string word) => word != null && TableHintSet.Contains(word);

        public static bool IsQueryHint(string word) => word != null && QueryHintSet.Contains(word);

        public static bool IsGlobalFunction(string variable) => variable != null && GlobalSet.Contains(variable);

        /// <summary>All the words, for casing a keyword suggestion that contains one of them.</summary>
        public static IEnumerable<string> AllWords() => SetOptions.Concat(TableHints).Concat(QueryHints).Concat(Singles);
    }
}
