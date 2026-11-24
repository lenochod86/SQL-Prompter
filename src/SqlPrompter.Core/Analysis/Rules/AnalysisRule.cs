using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>The groups of the rule catalog (the settings page lists the rules by them, #403).</summary>
    public enum RuleCategory
    {
        BestPractice,
        Deprecated,
        Execution,
        Miscellaneous,
        Performance,
        Script,
        Style,
    }

    /// <summary>One rule of the code analysis: its code, group, text, default and the switch in the settings. Declared once here; the analyzer, the settings window and the tests read it.</summary>
    public sealed class AnalysisRule
    {
        internal AnalysisRule(string code, CodeIssueKind kind, RuleCategory category, string title, string description, bool defaultOn, CodeIssueSeverity severity,
            Func<AnalysisSettings, bool> get = null, Action<AnalysisSettings, bool> set = null)
        {
            Code = code;
            Kind = kind;
            Category = category;
            Title = title;
            Description = description;
            DefaultOn = defaultOn;
            Severity = severity;
            LegacyGet = get;
            LegacySet = set;
        }

        /// <summary>Stable code: the code of the reference catalog (BP, DEP, EI, MI, PE, SC, ST) or SP for the rules of our own.</summary>
        public string Code { get; }

        public CodeIssueKind Kind { get; }

        public RuleCategory Category { get; }

        public string Title { get; }

        public string Description { get; }

        public bool DefaultOn { get; }

        public CodeIssueSeverity Severity { get; }

        /// <summary>The switch of a rule that had its own property in the settings file before the catalog existed; null for the others (they are kept in the list of overrides).</summary>
        internal Func<AnalysisSettings, bool> LegacyGet { get; }

        internal Action<AnalysisSettings, bool> LegacySet { get; }

        public override string ToString() => Code + " " + Title;
    }

    /// <summary>The catalog of the code analysis rules.</summary>
    public static class AnalysisRules
    {
        private static AnalysisRule R(string code, CodeIssueKind kind, RuleCategory category, string title, string description, bool on = false, CodeIssueSeverity severity = CodeIssueSeverity.Hint) =>
            new AnalysisRule(code, kind, category, title, description, on, severity);

        private static AnalysisRule L(string code, CodeIssueKind kind, RuleCategory category, string title, string description, bool on, CodeIssueSeverity severity,
            Func<AnalysisSettings, bool> get, Action<AnalysisSettings, bool> set) =>
            new AnalysisRule(code, kind, category, title, description, on, severity, get, set);

        private const RuleCategory BP = RuleCategory.BestPractice;
        private const RuleCategory DEP = RuleCategory.Deprecated;
        private const RuleCategory EI = RuleCategory.Execution;
        private const RuleCategory MI = RuleCategory.Miscellaneous;
        private const RuleCategory PE = RuleCategory.Performance;
        private const RuleCategory SC = RuleCategory.Script;
        private const RuleCategory ST = RuleCategory.Style;
        private const CodeIssueSeverity Hint = CodeIssueSeverity.Hint;
        private const CodeIssueSeverity Warn = CodeIssueSeverity.Warning;

        public static IReadOnlyList<AnalysisRule> All { get; } = new List<AnalysisRule>
        {
            // Best practice
            R("BP001", CodeIssueKind.BP001, BP, "Index type is not specified", "CREATE INDEX without CLUSTERED or NONCLUSTERED relies on the default."),
            R("BP002", CodeIssueKind.BP002, BP, "ORDER BY with a constant", "ORDER BY 1 or ORDER BY 'x' breaks when the select list changes.", true),
            R("BP003", CodeIssueKind.BP003, BP, "SELECT in a trigger", "A SELECT that returns a result set inside a trigger breaks clients; assign to variables or insert into a table.", true, Warn),
            L("BP004", CodeIssueKind.InsertWithoutColumns, BP, "INSERT without a column list", "INSERT INTO t VALUES / SELECT breaks when the columns of t change.", true, Hint, s => s.InsertWithoutColumns, (s, v) => s.InsertWithoutColumns = v),
            L("BP005", CodeIssueKind.SelectStar, BP, "Asterisk in the select list", "SELECT * returns every column, also those added later.", false, Hint, s => s.SelectStar, (s, v) => s.SelectStar = v),
            L("BP006", CodeIssueKind.TopWithoutOrderBy, BP, "TOP without ORDER BY", "SELECT TOP n without ORDER BY returns arbitrary rows.", true, Warn, s => s.TopWithoutOrderBy, (s, v) => s.TopWithoutOrderBy = v),
            L("BP007", CodeIssueKind.TypeWithoutLength, BP, "Type without an explicit length", "varchar, nvarchar, char ... without a length is 1 character in a declaration and 30 in CAST and CONVERT (also BP008).", true, Warn, s => s.TypeWithoutLength, (s, v) => s.TypeWithoutLength = v),
            R("BP009", CodeIssueKind.BP009, BP, "Variable-size type of length 1 or 2", "varchar(1) or nvarchar(2) costs more than char(1) or nchar(2)."),
            L("BP010", CodeIssueKind.AtAtIdentity, BP, "Usage of @@IDENTITY", "@@IDENTITY also returns values generated by triggers; use SCOPE_IDENTITY().", true, Warn, s => s.AtAtIdentity, (s, v) => s.AtAtIdentity = v),
            L("BP011", CodeIssueKind.EqualsNull, BP, "NULL literal in a comparison or expression", "= NULL, <> NULL, a < NULL, x + NULL and IN (..., NULL) never give the intended result; use IS [NOT] NULL.", true, Warn, s => s.EqualsNull, (s, v) => s.EqualsNull = v),
            R("BP012", CodeIssueKind.BP012, BP, "CASE without ELSE", "A CASE expression without ELSE returns NULL silently when no branch matches."),
            R("BP013", CodeIssueKind.BP013, BP, "EXECUTE('script') is used", "Dynamic SQL in a string; prefer sp_executesql with parameters."),
            R("BP014", CodeIssueKind.BP014, BP, "NULL / NOT NULL is not specified", "A column without NULL or NOT NULL depends on the session settings."),
            R("BP015", CodeIssueKind.BP015, BP, "Cursor scope is not specified", "DECLARE c CURSOR without LOCAL or GLOBAL uses the database default (GLOBAL)."),
            R("BP016", CodeIssueKind.BP016, BP, "RETURN without a result code", "A RETURN in a procedure without a value returns 0 implicitly."),
            R("BP017", CodeIssueKind.BP017, BP, "DELETE without WHERE or JOIN", "DELETE without a WHERE clause removes every row.", true, Warn),
            R("BP018", CodeIssueKind.BP018, BP, "UPDATE without WHERE or JOIN", "UPDATE without a WHERE clause changes every row.", true, Warn),
            R("BP022", CodeIssueKind.BP022, BP, "MONEY or SMALLMONEY is used", "money rounds intermediate results; prefer decimal."),
            R("BP023", CodeIssueKind.BP023, BP, "FLOAT or REAL is used", "float and real are approximate numbers; use decimal where exact values matter."),
            R("BP024", CodeIssueKind.BP024, BP, "SQL_VARIANT is used", "sql_variant limits indexing, comparisons and client support."),
            L("SP001", CodeIssueKind.JoinWithoutOn, BP, "JOIN without ON", "A JOIN (other than CROSS JOIN) without an ON condition combines every row with every row.", true, Warn, s => s.JoinWithoutOn, (s, v) => s.JoinWithoutOn = v),

            // Deprecated syntax
            R("DEP001", CodeIssueKind.DEP001, DEP, "Table hint without WITH", "FROM t (NOLOCK) without the WITH keyword is deprecated.", true),
            R("DEP002", CodeIssueKind.DEP002, DEP, "WRITETEXT, UPDATETEXT, READTEXT", "These statements are deprecated; use the max types and UPDATE ... .WRITE.", true),
            R("DEP003", CodeIssueKind.DEP003, DEP, "GROUP BY ALL", "GROUP BY ALL is deprecated.", true),
            R("DEP006", CodeIssueKind.DEP006, DEP, "SETUSER statement", "SETUSER is deprecated; use EXECUTE AS.", true),
            R("DEP007", CodeIssueKind.DEP007, DEP, "TAPE as a backup device", "Backup to tape is deprecated.", true),
            R("DEP009", CodeIssueKind.DEP009, DEP, "DBCC DBREINDEX", "Deprecated; use ALTER INDEX ... REBUILD.", true),
            R("DEP011", CodeIssueKind.DEP011, DEP, "DBCC INDEXDEFRAG", "Deprecated; use ALTER INDEX ... REORGANIZE.", true),
            R("DEP012", CodeIssueKind.DEP012, DEP, "DBCC SHOWCONTIG", "Deprecated; use sys.dm_db_index_physical_stats.", true),
            R("DEP013", CodeIssueKind.DEP013, DEP, "Deprecated SET options", "SET ANSI_NULLS OFF, ANSI_PADDING OFF, CONCAT_NULL_YIELDS_NULL OFF and SET OFFSETS are deprecated.", true),
            R("DEP014", CodeIssueKind.DEP014, DEP, "SET ROWCOUNT", "SET ROWCOUNT for INSERT, UPDATE and DELETE is deprecated; use TOP.", true),
            R("DEP015", CodeIssueKind.DEP015, DEP, "READONLY and READWRITE options", "The READONLY and READWRITE database options are deprecated; use READ_ONLY and READ_WRITE.", true),
            R("DEP016", CodeIssueKind.DEP016, DEP, "TORN_PAGE_DETECTION option", "Deprecated; use PAGE_VERIFY.", true),
            R("DEP018", CodeIssueKind.DEP018, DEP, "ALL in GRANT, DENY and REVOKE", "The ALL permission is deprecated; name the permissions.", true),
            R("DEP019", CodeIssueKind.DEP019, DEP, "Deprecated system table or view", "sysobjects, syscolumns, sysindexes ... are compatibility views; the message names the catalog view that replaces them.", true),
            R("DEP020", CodeIssueKind.DEP020, DEP, "Numbered procedures", "CREATE PROC p;1 is deprecated.", true),
            R("DEP021", CodeIssueKind.DEP021, DEP, "String literal as a column alias", "SELECT a 'name' and 'name' = a are deprecated; use AS [name].", true),
            R("DEP022", CodeIssueKind.DEP022, DEP, "DROP INDEX with table.index", "Use DROP INDEX index ON table.", true),
            R("DEP025", CodeIssueKind.DEP025, DEP, "Deprecated system stored procedure", "sp_dboption, sp_addtype, sp_addlogin ... are deprecated; the message names the replacement.", true),
            R("DEP026", CodeIssueKind.DEP026, DEP, "Three- or four-part column reference in the select list", "schema.table.column in a select list is deprecated; use the table or an alias.", true),
            R("DEP027", CodeIssueKind.DEP027, DEP, "Deprecated system function", "fn_virtualfilestats, fn_get_sql ... are deprecated; the message names the replacement.", true),

            // Execution
            R("EI003", CodeIssueKind.EI003, EI, "Non-scalar subquery used as a scalar", "A subquery that can return several rows in a comparison or in the select list fails at run time."),
            R("EI011", CodeIssueKind.EI011, EI, "FETCH from an undefined cursor", "No DECLARE ... CURSOR with this name in the batch.", true, Warn),
            R("EI012", CodeIssueKind.EI012, EI, "CLOSE of an undefined cursor", "No DECLARE ... CURSOR with this name in the batch.", true, Warn),
            R("EI013", CodeIssueKind.EI013, EI, "DEALLOCATE of an undefined cursor", "No DECLARE ... CURSOR with this name in the batch.", true, Warn),
            R("EI015", CodeIssueKind.EI015, EI, "Wrong number of fetch variables", "FETCH ... INTO has a different number of variables than the select list of the cursor.", true, Warn),
            R("EI016", CodeIssueKind.EI016, EI, "Procedure in another database", "EXEC OtherDb.dbo.proc is a hard dependency on the other database."),
            R("EI021", CodeIssueKind.EI021, EI, "CLOSE of an unopened cursor", "No OPEN of this cursor before the CLOSE.", true, Warn),
            R("EI023", CodeIssueKind.EI023, EI, "WHERE CURRENT OF on a read-only cursor", "The cursor is declared FOR READ ONLY.", true, Warn),
            R("EI024", CodeIssueKind.EI024, EI, "Procedure name starts with sp_", "SQL Server looks for sp_ procedures in master first and a later system procedure can take the name."),
            R("EI028", CodeIssueKind.EI028, EI, "NOT NULL column added without a default", "ALTER TABLE ... ADD c int NOT NULL fails when the table has rows.", true, Warn),
            R("EI029", CodeIssueKind.EI029, EI, "ISNUMERIC() is used", "ISNUMERIC returns 1 for strings that cannot be converted (e.g. '$', ','); use TRY_CONVERT."),
            R("EI030", CodeIssueKind.EI030, EI, "ORDER BY in a view or an inline function", "Without TOP or OFFSET it is not allowed or has no effect.", true, Warn),
            L("SP003", CodeIssueKind.UnexpectedEnd, EI, "Unexpected end of the script", "The script stops in the middle of a construct (unclosed parenthesis, CTE without a statement, FROM without a table ...).", true, Warn, s => s.UnexpectedEnd, (s, v) => s.UnexpectedEnd = v),
            L("SP008", CodeIssueKind.AmbiguousColumn, EI, "Ambiguous column", "A column name without an alias that two tables of the query have (needs the metadata).", true, Warn, s => s.AmbiguousColumn, (s, v) => s.AmbiguousColumn = v),
            L("SP009", CodeIssueKind.UnknownColumn, EI, "Unknown column of a temporary table", "alias.column where the alias is a temporary table or table variable of the script that has no such column.", true, Warn, s => s.UnknownColumn, (s, v) => s.UnknownColumn = v),

            // Miscellaneous
            R("MI001", CodeIssueKind.MI001, MI, "Table variable is declared but never used", "DECLARE @t TABLE (...) that is never referenced.", true),
            R("MI002", CodeIssueKind.MI002, MI, "Temporary table is created but never used", "CREATE TABLE #t or SELECT INTO #t that is never read.", true),
            R("MI003", CodeIssueKind.MI003, MI, "Unqualified column name", "A column without an alias or table name in a query with more than one source (needs the metadata)."),
            L("MI005", CodeIssueKind.UnusedVariable, MI, "Variable is declared but never used", "DECLARE @v that is never referenced.", true, Hint, s => s.UnusedVariable, (s, v) => s.UnusedVariable = v),
            R("MI006", CodeIssueKind.MI006, MI, "Parameter is declared but never used", "A parameter of a procedure or function that the body never mentions.", true),
            R("MI007", CodeIssueKind.MI007, MI, "WAITFOR is used", "WAITFOR DELAY and WAITFOR TIME hold a connection and a session."),
            R("MI008", CodeIssueKind.MI008, MI, "QUOTED_IDENTIFIER inside a module", "SET QUOTED_IDENTIFIER inside a procedure, trigger or function has no effect.", true),

            // Performance
            R("PE001", CodeIssueKind.PE001, PE, "Schema of a procedure is not specified", "EXEC proc without a schema (a repair needs the metadata)."),
            R("PE002", CodeIssueKind.PE002, PE, "Schema of a table or view is not specified", "FROM Orders without a schema (a repair needs the metadata)."),
            R("PE003", CodeIssueKind.PE003, PE, "Table created by SELECT INTO", "SELECT INTO locks system tables and hides the column definitions."),
            R("PE004", CodeIssueKind.PE004, PE, "INDEX hint is used", "WITH (INDEX(...)) fixes the plan to an index that may disappear."),
            R("PE005", CodeIssueKind.PE005, PE, "JOIN hint is used", "INNER LOOP JOIN, HASH or MERGE override the optimizer."),
            R("PE006", CodeIssueKind.PE006, PE, "Table hint is used", "TABLOCK, HOLDLOCK, UPDLOCK ... override the locking of SQL Server."),
            R("PE007", CodeIssueKind.PE007, PE, "Query hint is used", "OPTION (...) overrides the optimizer."),
            R("PE008", CodeIssueKind.PE008, PE, "SET NOCOUNT OFF", "The row counts are sent to the client for every statement."),
            R("PE010", CodeIssueKind.PE010, PE, "DDL after DML in a procedure or trigger", "CREATE TABLE #t after data statements causes recompiles."),
            R("PE011", CodeIssueKind.PE011, PE, "PRINT in a trigger", "PRINT in a trigger sends messages to every client that fires it."),
            R("PE012", CodeIssueKind.PE012, PE, "SET option that recompiles the procedure", "SET ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT ... inside a module causes a recompile."),
            R("PE013", CodeIssueKind.PE013, PE, "COUNT used instead of EXISTS", "IF (SELECT COUNT(*) ...) > 0 counts every row; EXISTS stops at the first.", true),
            R("PE014", CodeIssueKind.PE014, PE, "SET FORCEPLAN is used", "SET FORCEPLAN forces the join order."),
            R("PE015", CodeIssueKind.PE015, PE, "Cursor is not forward-only", "No FETCH FIRST, LAST, PRIOR, ABSOLUTE or RELATIVE, but the cursor is not FORWARD_ONLY or FAST_FORWARD."),
            R("PE016", CodeIssueKind.PE016, PE, "Cursor is opened but not deallocated", "No DEALLOCATE of the cursor in the batch."),
            R("PE017", CodeIssueKind.PE017, PE, "Constant user function in a condition", "A scalar user function called with constants only is evaluated for every row."),
            R("PE018", CodeIssueKind.PE018, PE, "Cursor is not read-only", "FOR READ_ONLY (or STATIC, FAST_FORWARD) is cheaper."),
            R("PE019", CodeIssueKind.PE019, PE, "IN (subquery)", "Consider EXISTS instead of IN with a large subquery."),
            R("PE020", CodeIssueKind.PE020, PE, "INSERT with ORDER BY", "The order of the rows is not guaranteed in the target table."),
            R("PE021", CodeIssueKind.PE021, PE, "WITH RECOMPILE is used", "The plan is compiled at every execution."),
            R("PE023", CodeIssueKind.PE023, PE, "Schema of a DDL statement is not specified", "CREATE TABLE t without a schema (a repair needs the metadata)."),
            L("SP002", CodeIssueKind.LockHint, PE, "NOLOCK hint", "NOLOCK reads uncommitted data and can skip or repeat rows.", false, Hint, s => s.LockHint, (s, v) => s.LockHint = v),
            L("SP004", CodeIssueKind.FunctionOnColumn, PE, "Function on a column in a condition", "WHERE YEAR(d) = 2025 or UPPER(c) = 'X': the index on the column cannot be used.", true, Hint, s => s.FunctionOnColumn, (s, v) => s.FunctionOnColumn = v),
            L("SP005", CodeIssueKind.LeadingWildcard, PE, "LIKE with a leading %", "LIKE '%text' cannot use an index.", true, Hint, s => s.LeadingWildcard, (s, v) => s.LeadingWildcard = v),
            L("SP006", CodeIssueKind.NotInSubquery, PE, "NOT IN with a subquery", "NOT IN (SELECT ...) returns no rows when the subquery returns a NULL; NOT EXISTS does not.", true, Warn, s => s.NotInSubquery, (s, v) => s.NotInSubquery = v),
            L("SP007", CodeIssueKind.ImplicitConversion, PE, "Implicit conversion", "A column compared with a literal of another type: the index cannot be used (needs the metadata).", true, Hint, s => s.ImplicitConversion, (s, v) => s.ImplicitConversion = v),

            // Script
            R("SC004", CodeIssueKind.SC004, SC, "TODO, WARNING, BUG or FIX comment", "A comment that marks unfinished work."),
            R("SC005", CodeIssueKind.SC005, SC, "The procedure grants itself permissions", "GRANT ... ON the procedure that is being created.", true, Warn),
            R("SC006", CodeIssueKind.SC006, SC, "Line endings are not CR LF", "A line break that is not CR LF (the repair normalizes the rest of the script)."),

            // Style
            L("ST001", CodeIssueKind.OldStyleJoin, ST, "Old-style join", "FROM a, b WHERE a.x = b.x; use JOIN ... ON.", true, Hint, s => s.OldStyleJoin, (s, v) => s.OldStyleJoin = v),
            R("ST002", CodeIssueKind.ST002, ST, "Old-style column alias with =", "SELECT total = SUM(x); use SUM(x) AS total."),
            R("ST003", CodeIssueKind.ST003, ST, "Procedure body not enclosed in BEGIN...END", "The body of the procedure starts without BEGIN."),
            R("ST006", CodeIssueKind.ST006, ST, "Old-style TOP clause", "DELETE TOP 10 without parentheses; use TOP (10)."),
            R("ST007", CodeIssueKind.ST007, ST, "Cursor name is reused", "The same cursor name is declared twice in a batch."),
            R("ST008", CodeIssueKind.ST008, ST, "Non-named parameter style", "EXEC proc 1, 2 instead of EXEC proc @a = 1, @b = 2."),
            R("ST010", CodeIssueKind.ST010, ST, "Table source without an alias", "FROM dbo.Orders without an alias in a query with more than one source."),
            R("ST013", CodeIssueKind.ST013, ST, "Non-ANSI != operator", "Use <> (the standard operator)."),
        };

        private static readonly Dictionary<string, AnalysisRule> ByCode = All.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<CodeIssueKind, AnalysisRule> ByKind = All.ToDictionary(r => r.Kind);

        public static AnalysisRule Find(string code) => code != null && ByCode.TryGetValue(code, out var rule) ? rule : null;

        public static AnalysisRule Of(CodeIssueKind kind) => ByKind.TryGetValue(kind, out var rule) ? rule : null;

        public static string CategoryName(RuleCategory category)
        {
            switch (category)
            {
                case RuleCategory.BestPractice: return "Best practice";
                case RuleCategory.Deprecated: return "Deprecated syntax";
                case RuleCategory.Execution: return "Execution";
                case RuleCategory.Miscellaneous: return "Miscellaneous";
                case RuleCategory.Performance: return "Performance";
                case RuleCategory.Script: return "Script";
                default: return "Style";
            }
        }
    }
}
