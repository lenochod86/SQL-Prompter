using System;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Editing;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The rule catalog of the code analysis (#395): every rule is tested alone (positive, negative, strings and comments, incomplete code).</summary>
    public class CodeAnalysisRulesTests
    {
        private static AnalysisSettings Only(string code)
        {
            var settings = new AnalysisSettings();
            foreach (var rule in AnalysisRules.All)
            {
                settings.SetOn(rule, false);
            }

            settings.SetOn(AnalysisRules.Find(code), true);
            return settings;
        }

        private static string[] Flagged(string code, string sql) =>
            CodeAnalyzer.Analyze(sql, Only(code)).Where(i => i.Code == code).Select(i => sql.Substring(i.Start, i.Length)).ToArray();

        private static void AssertFlags(string code, string sql, string expected) =>
            Assert.Equal(expected.Length == 0 ? new string[0] : expected.Split('|'), Flagged(code, sql));

        [Fact]
        public void CatalogCodesAreUniqueAndEveryKindIsInIt()
        {
            Assert.Equal(AnalysisRules.All.Count, AnalysisRules.All.Select(r => r.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (CodeIssueKind kind in Enum.GetValues(typeof(CodeIssueKind)))
            {
                Assert.True(AnalysisRules.Of(kind) != null, kind + " is not in the catalog");
            }
        }

        [Fact]
        public void OldSettingsLoadWithTheSameBehaviour()
        {
            var settings = SettingsJson.Deserialize("{\"analysis\":{\"selectStar\":true,\"equalsNull\":false}}").Analysis;

            Assert.True(settings.SelectStar);
            Assert.False(settings.EqualsNull);
            Assert.True(settings.IsOn(CodeIssueKind.JoinWithoutOn));
            Assert.True(settings.IsOn(CodeIssueKind.BP002));
            Assert.False(settings.IsOn(CodeIssueKind.BP012));
        }

        [Fact]
        public void RuleSwitchesAreStoredAndRestored()
        {
            var settings = new AnalysisSettings();
            settings.SetOn(AnalysisRules.Find("BP012"), true);
            settings.SetOn(AnalysisRules.Find("BP002"), false);

            var loaded = SettingsJson.Deserialize(SettingsJson.Serialize(new PrompterSettings { Analysis = settings })).Analysis;

            Assert.True(loaded.IsOn(CodeIssueKind.BP012));
            Assert.False(loaded.IsOn(CodeIssueKind.BP002));
            Assert.True(loaded.IsOn(CodeIssueKind.BP003));
            Assert.Equal(2, loaded.RuleOverrides.Count);
        }

        [Fact]
        public void ARuleThatIsOffReportsNothing()
        {
            var settings = Only("BP012");
            settings.SetOn(AnalysisRules.Find("BP012"), false);

            Assert.Empty(CodeAnalyzer.Analyze("SELECT CASE WHEN a = 1 THEN 2 END", settings));
        }

        // ---- Best practice ----
        [Theory]
        [InlineData("CREATE INDEX ix ON t (a)", "INDEX")]
        [InlineData("CREATE UNIQUE INDEX ix ON t (a)", "INDEX")]
        [InlineData("CREATE NONCLUSTERED INDEX ix ON t (a)", "")]
        [InlineData("CREATE UNIQUE CLUSTERED INDEX ix ON t (a)", "")]
        [InlineData("CREATE CLUSTERED COLUMNSTORE INDEX ix ON t", "")]
        [InlineData("DROP INDEX ix ON t", "")]
        [InlineData("SELECT 1 -- CREATE INDEX x", "")]
        public void BP001(string sql, string expected) => AssertFlags("BP001", sql, expected);

        [Fact]
        public void BP001HasARepair() =>
            Assert.Equal("CREATE NONCLUSTERED INDEX ix ON t (a)", CodeFixes.Apply("CREATE INDEX ix ON t (a)", Only("BP001")));

        [Theory]
        [InlineData("SELECT a FROM t ORDER BY 1", "1")]
        [InlineData("SELECT a, b FROM t ORDER BY a, 2 DESC", "2")]
        [InlineData("SELECT a FROM t ORDER BY 'x'", "'x'")]
        [InlineData("SELECT a FROM t ORDER BY a", "")]
        [InlineData("SELECT a FROM t ORDER BY (SELECT NULL)", "")]
        [InlineData("SELECT a FROM t ORDER BY a OFFSET 1 ROWS", "")]
        [InlineData("SELECT a FROM t -- ORDER BY 1", "")]
        public void BP002(string sql, string expected) => AssertFlags("BP002", sql, expected);

        [Theory]
        [InlineData("CREATE TRIGGER tr ON t AFTER INSERT AS SELECT * FROM inserted", "SELECT")]
        [InlineData("CREATE TRIGGER tr ON t AFTER INSERT AS BEGIN SELECT 1 END", "SELECT")]
        [InlineData("CREATE TRIGGER tr ON t AFTER INSERT AS BEGIN DECLARE @a int SELECT @a = id FROM inserted END", "")]
        [InlineData("CREATE TRIGGER tr ON t AFTER INSERT AS BEGIN INSERT INTO l SELECT id FROM inserted END", "")]
        [InlineData("CREATE PROCEDURE p AS SELECT 1", "")]
        public void BP003(string sql, string expected) => AssertFlags("BP003", sql, expected);

        [Theory]
        [InlineData("DECLARE @a varchar(1)", "varchar(1)")]
        [InlineData("DECLARE @a nvarchar(2)", "nvarchar(2)")]
        [InlineData("CREATE TABLE t (a varchar(1) NULL)", "varchar(1)")]
        [InlineData("DECLARE @a varchar(3)", "")]
        [InlineData("DECLARE @a char(1)", "")]
        [InlineData("SELECT CAST(a AS varchar(1)) FROM t", "")]
        public void BP009(string sql, string expected) => AssertFlags("BP009", sql, expected);

        [Theory]
        [InlineData("SELECT a + NULL FROM t", "NULL")]
        [InlineData("SELECT 'a' + NULL", "NULL")]
        [InlineData("SELECT * FROM t WHERE a IN (1, NULL)", "NULL")]
        [InlineData("SELECT * FROM t WHERE a < NULL", "NULL")]
        [InlineData("SELECT * FROM t WHERE a >= NULL", "NULL")]
        [InlineData("SELECT * FROM t WHERE a IS NULL", "")]
        [InlineData("SELECT * FROM t WHERE a IS NOT NULL", "")]
        [InlineData("UPDATE t SET a = NULL", "")]
        [InlineData("SELECT CASE WHEN a = 1 THEN NULL ELSE 2 END", "")]
        [InlineData("SELECT 'a + NULL'", "")]
        public void BP011ExtendedChecks(string sql, string expected)
        {
            var settings = Only("BP011");
            var found = CodeAnalyzer.Analyze(sql, settings).Where(i => i.Code == "BP011" && sql.Substring(i.Start, i.Length) == "NULL").Select(i => sql.Substring(i.Start, i.Length)).ToArray();
            Assert.Equal(expected.Length == 0 ? new string[0] : expected.Split('|'), found);
        }

        [Theory]
        [InlineData("SELECT CASE WHEN a = 1 THEN 2 END", "CASE")]
        [InlineData("SELECT CASE a WHEN 1 THEN 2 ELSE 3 END", "")]
        [InlineData("SELECT CASE WHEN a = 1 THEN CASE WHEN b = 1 THEN 1 END ELSE 3 END", "CASE")]
        [InlineData("SELECT CASE WHEN a = 1 THEN 2", "")]
        public void BP012(string sql, string expected) => AssertFlags("BP012", sql, expected);

        [Theory]
        [InlineData("EXEC('SELECT 1')", "EXEC")]
        [InlineData("EXECUTE (@sql)", "EXECUTE")]
        [InlineData("EXEC sp_executesql @sql", "")]
        [InlineData("EXEC dbo.p", "")]
        [InlineData("SELECT 'EXEC(1)'", "")]
        public void BP013(string sql, string expected) => AssertFlags("BP013", sql, expected);

        [Theory]
        [InlineData("CREATE TABLE t (a int, b varchar(10) NOT NULL)", "a")]
        [InlineData("DECLARE @t TABLE (a int NULL, b int)", "b")]
        [InlineData("CREATE TABLE t (a int NOT NULL PRIMARY KEY, b int IDENTITY(1,1), c AS (a + 1), CONSTRAINT pk PRIMARY KEY (a))", "")]
        [InlineData("CREATE TABLE t (a int NULL, b int NOT NULL)", "")]
        public void BP014(string sql, string expected) => AssertFlags("BP014", sql, expected);

        [Theory]
        [InlineData("DECLARE c CURSOR FOR SELECT 1", "CURSOR")]
        [InlineData("DECLARE c CURSOR FAST_FORWARD FOR SELECT 1", "CURSOR")]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT 1", "")]
        [InlineData("DECLARE c CURSOR GLOBAL FOR SELECT 1", "")]
        [InlineData("DECLARE c INSENSITIVE CURSOR LOCAL FOR SELECT 1", "")]
        public void BP015(string sql, string expected) => AssertFlags("BP015", sql, expected);

        [Theory]
        [InlineData("CREATE PROCEDURE p AS BEGIN RETURN END", "RETURN")]
        [InlineData("CREATE PROCEDURE p AS BEGIN RETURN; END", "RETURN")]
        [InlineData("CREATE PROCEDURE p AS BEGIN RETURN 0 END", "")]
        [InlineData("CREATE PROCEDURE p AS BEGIN RETURN @x END", "")]
        [InlineData("CREATE FUNCTION f() RETURNS int AS BEGIN RETURN 1 END", "")]
        public void BP016(string sql, string expected) => AssertFlags("BP016", sql, expected);

        [Fact]
        public void BP016HasARepair() =>
            Assert.Equal("CREATE PROCEDURE p AS BEGIN RETURN 0 END", CodeFixes.Apply("CREATE PROCEDURE p AS BEGIN RETURN END", Only("BP016")));

        [Theory]
        [InlineData("DELETE FROM t", "DELETE")]
        [InlineData("DELETE t", "DELETE")]
        [InlineData("DELETE FROM t WHERE a = 1", "")]
        [InlineData("DELETE x FROM t x JOIN u ON x.a = u.a", "")]
        [InlineData("WITH c AS (SELECT 1 a) DELETE FROM c", "DELETE")]
        [InlineData("MERGE t USING u ON t.a = u.a WHEN MATCHED THEN DELETE;", "")]
        [InlineData("CREATE TABLE t (a int, FOREIGN KEY (a) REFERENCES u ON DELETE CASCADE)", "")]
        [InlineData("SELECT 'DELETE FROM t'", "")]
        public void BP017(string sql, string expected) => AssertFlags("BP017", sql, expected);

        [Theory]
        [InlineData("UPDATE t SET a = 1", "UPDATE")]
        [InlineData("UPDATE t SET a = 1 WHERE b = 2", "")]
        [InlineData("UPDATE t SET a = u.a FROM t JOIN u ON t.b = u.b", "")]
        [InlineData("UPDATE STATISTICS t", "")]
        [InlineData("CREATE TRIGGER tr ON t AFTER UPDATE AS SELECT 1", "")]
        public void BP018(string sql, string expected) => AssertFlags("BP018", sql, expected);

        [Theory]
        [InlineData("DECLARE @a money", "money")]
        [InlineData("CREATE TABLE t (a smallmoney NULL)", "smallmoney")]
        [InlineData("SELECT money FROM t", "")]
        public void BP022(string sql, string expected) => AssertFlags("BP022", sql, expected);

        [Theory]
        [InlineData("DECLARE @a float", "float")]
        [InlineData("SELECT CAST(a AS real)", "real")]
        [InlineData("SELECT real FROM t", "")]
        public void BP023(string sql, string expected) => AssertFlags("BP023", sql, expected);

        [Theory]
        [InlineData("DECLARE @a sql_variant", "sql_variant")]
        [InlineData("DECLARE @a int", "")]
        public void BP024(string sql, string expected) => AssertFlags("BP024", sql, expected);

        // ---- Deprecated syntax ----
        [Theory]
        [InlineData("SELECT * FROM t (NOLOCK)", "(")]
        [InlineData("SELECT * FROM dbo.t AS x (NOLOCK)", "(")]
        [InlineData("SELECT * FROM t WITH (NOLOCK)", "")]
        [InlineData("SELECT * FROM t JOIN u (READPAST) ON 1 = 1", "(")]
        [InlineData("SELECT COUNT(1) FROM t", "")]
        [InlineData("CREATE INDEX ix ON t (a)", "")]
        public void DEP001(string sql, string expected) => AssertFlags("DEP001", sql, expected);

        [Fact]
        public void DEP001HasARepair() =>
            Assert.Equal("SELECT * FROM t WITH (NOLOCK)", CodeFixes.Apply("SELECT * FROM t (NOLOCK)", Only("DEP001")));

        [Theory]
        [InlineData("WRITETEXT t.c @p 'x'", "WRITETEXT")]
        [InlineData("READTEXT t.c @p 0 10", "READTEXT")]
        [InlineData("SELECT 'READTEXT'", "")]
        public void DEP002(string sql, string expected) => AssertFlags("DEP002", sql, expected);

        [Theory]
        [InlineData("SELECT a FROM t GROUP BY ALL a", "ALL")]
        [InlineData("SELECT a FROM t GROUP BY a", "")]
        public void DEP003(string sql, string expected) => AssertFlags("DEP003", sql, expected);

        [Theory]
        [InlineData("SETUSER 'x'", "SETUSER")]
        [InlineData("SELECT 'SETUSER'", "")]
        public void DEP006(string sql, string expected) => AssertFlags("DEP006", sql, expected);

        [Theory]
        [InlineData("BACKUP DATABASE d TO TAPE = '\\\\.\\tape0'", "TAPE")]
        [InlineData("BACKUP DATABASE d TO DISK = 'a.bak'", "")]
        public void DEP007(string sql, string expected) => AssertFlags("DEP007", sql, expected);

        [Theory]
        [InlineData("DEP009", "DBCC DBREINDEX ('t')", "DBREINDEX")]
        [InlineData("DEP011", "DBCC INDEXDEFRAG (0, 't')", "INDEXDEFRAG")]
        [InlineData("DEP012", "DBCC SHOWCONTIG ('t')", "SHOWCONTIG")]
        [InlineData("DEP009", "DBCC CHECKDB", "")]
        public void DbccCommands(string code, string sql, string expected) => AssertFlags(code, sql, expected);

        [Theory]
        [InlineData("SET ANSI_NULLS OFF", "ANSI_NULLS OFF")]
        [InlineData("SET ANSI_PADDING OFF", "ANSI_PADDING OFF")]
        [InlineData("SET CONCAT_NULL_YIELDS_NULL OFF", "CONCAT_NULL_YIELDS_NULL OFF")]
        [InlineData("SET OFFSETS SELECT ON", "OFFSETS")]
        [InlineData("SET ANSI_NULLS ON", "")]
        public void DEP013(string sql, string expected) => AssertFlags("DEP013", sql, expected);

        [Theory]
        [InlineData("SET ROWCOUNT 10", "ROWCOUNT")]
        [InlineData("SET ROWCOUNT @n", "ROWCOUNT")]
        [InlineData("SET ROWCOUNT 0", "")]
        public void DEP014(string sql, string expected) => AssertFlags("DEP014", sql, expected);

        [Theory]
        [InlineData("ALTER DATABASE d MODIFY FILEGROUP g READONLY", "READONLY")]
        [InlineData("ALTER DATABASE d MODIFY FILEGROUP g READWRITE", "READWRITE")]
        [InlineData("CREATE PROCEDURE p @t dbo.tt READONLY AS SELECT 1", "")]
        public void DEP015(string sql, string expected) => AssertFlags("DEP015", sql, expected);

        [Theory]
        [InlineData("ALTER DATABASE d SET TORN_PAGE_DETECTION ON", "TORN_PAGE_DETECTION")]
        [InlineData("ALTER DATABASE d SET PAGE_VERIFY CHECKSUM", "")]
        public void DEP016(string sql, string expected) => AssertFlags("DEP016", sql, expected);

        [Theory]
        [InlineData("GRANT ALL TO u", "ALL")]
        [InlineData("DENY ALL ON t TO u", "ALL")]
        [InlineData("GRANT SELECT ON t TO u", "")]
        public void DEP018(string sql, string expected) => AssertFlags("DEP018", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM sysobjects", "sysobjects")]
        [InlineData("SELECT * FROM dbo.syscolumns", "syscolumns")]
        [InlineData("SELECT * FROM master..sysdatabases", "sysdatabases")]
        [InlineData("SELECT * FROM sys.objects", "")]
        public void DEP019(string sql, string expected) => AssertFlags("DEP019", sql, expected);

        [Theory]
        [InlineData("CREATE PROCEDURE p;1 AS SELECT 1", ";1")]
        [InlineData("CREATE PROCEDURE p AS SELECT 1", "")]
        public void DEP020(string sql, string expected) => AssertFlags("DEP020", sql, expected);

        [Theory]
        [InlineData("SELECT a 'name' FROM t", "'name'")]
        [InlineData("SELECT 'name' = a FROM t", "'name'")]
        [InlineData("SELECT a AS 'name' FROM t", "")]
        [InlineData("SELECT 'x' FROM t", "")]
        [InlineData("SELECT CASE WHEN a = 1 THEN 'x' ELSE 'y' END FROM t", "")]
        [InlineData("SELECT a + 'x' FROM t", "")]
        [InlineData("SELECT a FROM t WHERE b = 'x'", "")]
        public void DEP021(string sql, string expected) => AssertFlags("DEP021", sql, expected);

        [Theory]
        [InlineData("DROP INDEX t.ix", "t.ix")]
        [InlineData("DROP INDEX dbo.t.ix", "dbo.t.ix")]
        [InlineData("DROP INDEX ix ON t", "")]
        [InlineData("DROP INDEX ix ON dbo.t", "")]
        public void DEP022(string sql, string expected) => AssertFlags("DEP022", sql, expected);

        [Theory]
        [InlineData("EXEC sp_dboption 'd', 'trunc. log', 'true'", "sp_dboption")]
        [InlineData("EXEC sp_addlogin 'u'", "sp_addlogin")]
        [InlineData("EXEC sp_help 't'", "")]
        public void DEP025(string sql, string expected) => AssertFlags("DEP025", sql, expected);

        [Theory]
        [InlineData("SELECT dbo.t.a FROM dbo.t", "dbo.t.a")]
        [InlineData("SELECT db.dbo.t.a FROM db.dbo.t", "db.dbo.t.a")]
        [InlineData("SELECT t.a FROM dbo.t", "")]
        [InlineData("SELECT dbo.fn(1) FROM t", "")]
        public void DEP026(string sql, string expected) => AssertFlags("DEP026", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM fn_virtualfilestats(1, 1)", "fn_virtualfilestats")]
        [InlineData("SELECT fn_get_sql(@h)", "fn_get_sql")]
        [InlineData("SELECT * FROM sys.dm_io_virtual_file_stats(1, 1)", "")]
        public void DEP027(string sql, string expected) => AssertFlags("DEP027", sql, expected);

        // ---- Execution ----
        [Theory]
        [InlineData("SELECT * FROM t WHERE a = (SELECT b FROM u)", "SELECT")]
        [InlineData("SELECT (SELECT b FROM u WHERE u.a = t.a) FROM t", "SELECT")]
        [InlineData("SELECT * FROM t WHERE a = (SELECT MAX(b) FROM u)", "")]
        [InlineData("SELECT * FROM t WHERE a = (SELECT TOP (1) b FROM u ORDER BY b)", "")]
        [InlineData("SELECT * FROM t WHERE a IN (SELECT b FROM u)", "")]
        [InlineData("SELECT * FROM t WHERE EXISTS (SELECT 1 FROM u)", "")]
        public void EI003(string sql, string expected) => AssertFlags("EI003", sql, expected);

        private const string Cursor = "DECLARE c CURSOR LOCAL FOR SELECT a, b FROM t\n";

        [Theory]
        [InlineData("FETCH NEXT FROM c INTO @a", "c")]
        [InlineData(Cursor + "OPEN c\nFETCH NEXT FROM c INTO @a, @b", "")]
        [InlineData(Cursor + "GO\nFETCH NEXT FROM c INTO @a, @b", "c")]
        [InlineData("DECLARE @c CURSOR\nFETCH NEXT FROM @c", "")]
        public void EI011(string sql, string expected) => AssertFlags("EI011", sql, expected);

        [Theory]
        [InlineData("CLOSE c", "c")]
        [InlineData(Cursor + "OPEN c\nCLOSE c", "")]
        public void EI012(string sql, string expected) => AssertFlags("EI012", sql, expected);

        [Theory]
        [InlineData("DEALLOCATE c", "c")]
        [InlineData(Cursor + "DEALLOCATE c", "")]
        public void EI013(string sql, string expected) => AssertFlags("EI013", sql, expected);

        [Theory]
        [InlineData(Cursor + "OPEN c\nFETCH NEXT FROM c INTO @a", "INTO")]
        [InlineData(Cursor + "OPEN c\nFETCH NEXT FROM c INTO @a, @b, @c", "INTO")]
        [InlineData(Cursor + "OPEN c\nFETCH NEXT FROM c INTO @a, @b", "")]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT * FROM t\nOPEN c\nFETCH NEXT FROM c INTO @a", "")]
        public void EI015(string sql, string expected) => AssertFlags("EI015", sql, expected);

        [Theory]
        [InlineData("EXEC OtherDb.dbo.p", "OtherDb.dbo.p")]
        [InlineData("EXEC dbo.p", "")]
        [InlineData("EXEC master.dbo.sp_who", "")]
        [InlineData("EXEC [Srv].db.dbo.p", "")]
        public void EI016(string sql, string expected) => AssertFlags("EI016", sql, expected);

        [Theory]
        [InlineData("CLOSE c", "")]
        [InlineData(Cursor + "CLOSE c", "c")]
        [InlineData(Cursor + "OPEN c\nCLOSE c", "")]
        public void EI021(string sql, string expected) => AssertFlags("EI021", sql, expected);

        [Theory]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT a FROM t FOR READ ONLY\nOPEN c\nUPDATE t SET a = 1 WHERE CURRENT OF c", "c")]
        [InlineData("DECLARE c CURSOR LOCAL READ_ONLY FOR SELECT a FROM t\nOPEN c\nUPDATE t SET a = 1 WHERE CURRENT OF c", "c")]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT a FROM t FOR UPDATE\nOPEN c\nUPDATE t SET a = 1 WHERE CURRENT OF c", "")]
        public void EI023(string sql, string expected) => AssertFlags("EI023", sql, expected);

        [Theory]
        [InlineData("CREATE PROCEDURE sp_doit AS SELECT 1", "sp_doit")]
        [InlineData("CREATE PROCEDURE dbo.sp_doit AS SELECT 1", "sp_doit")]
        [InlineData("CREATE PROCEDURE dbo.usp_doit AS SELECT 1", "")]
        public void EI024(string sql, string expected) => AssertFlags("EI024", sql, expected);

        [Theory]
        [InlineData("ALTER TABLE t ADD c int NOT NULL", "c")]
        [InlineData("ALTER TABLE t ADD c int NOT NULL DEFAULT 0", "")]
        [InlineData("ALTER TABLE t ADD c int NULL", "")]
        [InlineData("ALTER TABLE t ADD c int IDENTITY(1,1) NOT NULL", "")]
        [InlineData("ALTER TABLE t ADD CONSTRAINT pk PRIMARY KEY (a)", "")]
        public void EI028(string sql, string expected) => AssertFlags("EI028", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM t WHERE ISNUMERIC(a) = 1", "ISNUMERIC")]
        [InlineData("SELECT TRY_CONVERT(int, a)", "")]
        [InlineData("SELECT 'ISNUMERIC(1)'", "")]
        public void EI029(string sql, string expected) => AssertFlags("EI029", sql, expected);

        [Theory]
        [InlineData("CREATE VIEW v AS SELECT a FROM t ORDER BY a", "ORDER")]
        [InlineData("CREATE VIEW v AS SELECT TOP 10 a FROM t ORDER BY a", "")]
        [InlineData("CREATE VIEW v AS SELECT a FROM t", "")]
        [InlineData("CREATE FUNCTION f() RETURNS TABLE AS RETURN (SELECT a FROM t ORDER BY a)", "ORDER")]
        [InlineData("CREATE FUNCTION f() RETURNS TABLE AS RETURN (SELECT TOP 5 a FROM t ORDER BY a)", "")]
        [InlineData("CREATE PROCEDURE p AS SELECT a FROM t ORDER BY a", "")]
        public void EI030(string sql, string expected) => AssertFlags("EI030", sql, expected);

        // ---- Miscellaneous ----
        [Theory]
        [InlineData("DECLARE @t TABLE (a int)\nSELECT 1", "@t")]
        [InlineData("DECLARE @t TABLE (a int)\nINSERT INTO @t VALUES (1)", "")]
        [InlineData("DECLARE @a int\nSELECT 1", "")]
        public void MI001(string sql, string expected) => AssertFlags("MI001", sql, expected);

        [Theory]
        [InlineData("CREATE TABLE #t (a int)\nSELECT 1", "#t")]
        [InlineData("SELECT a INTO #t FROM u\nSELECT 1", "#t")]
        [InlineData("CREATE TABLE #t (a int)\nINSERT INTO #t VALUES (1)\nDROP TABLE #t", "#t")]
        [InlineData("CREATE TABLE #t (a int)\nSELECT * FROM #t", "")]
        [InlineData("CREATE TABLE ##t (a int)\nSELECT 1", "")]
        public void MI002(string sql, string expected) => AssertFlags("MI002", sql, expected);

        [Theory]
        [InlineData("CREATE PROCEDURE p @a int, @b int AS SELECT @a", "@b")]
        [InlineData("CREATE PROCEDURE p @a int AS SELECT @a", "")]
        [InlineData("CREATE FUNCTION f(@a int, @b int) RETURNS int AS BEGIN RETURN @a END", "@b")]
        [InlineData("DECLARE @a int", "")]
        public void MI006(string sql, string expected) => AssertFlags("MI006", sql, expected);

        [Theory]
        [InlineData("WAITFOR DELAY '00:00:05'", "WAITFOR DELAY")]
        [InlineData("WAITFOR TIME '10:00'", "WAITFOR TIME")]
        [InlineData("SELECT 'WAITFOR DELAY'", "")]
        public void MI007(string sql, string expected) => AssertFlags("MI007", sql, expected);

        [Theory]
        [InlineData("CREATE PROCEDURE p AS BEGIN SET QUOTED_IDENTIFIER ON SELECT 1 END", "QUOTED_IDENTIFIER")]
        [InlineData("SET QUOTED_IDENTIFIER ON\nGO\nCREATE PROCEDURE p AS SELECT 1", "")]
        [InlineData("SET QUOTED_IDENTIFIER ON", "")]
        public void MI008(string sql, string expected) => AssertFlags("MI008", sql, expected);

        // ---- Performance ----
        [Theory]
        [InlineData("SELECT * INTO #t FROM u", "INTO")]
        [InlineData("INSERT INTO t SELECT * FROM u", "")]
        [InlineData("SELECT @a = 1 FROM u", "")]
        public void PE003(string sql, string expected) => AssertFlags("PE003", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM t WITH (INDEX(ix))", "INDEX")]
        [InlineData("SELECT * FROM t WITH (INDEX = ix)", "INDEX")]
        [InlineData("SELECT * FROM t WITH (NOLOCK)", "")]
        public void PE004(string sql, string expected) => AssertFlags("PE004", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM a INNER LOOP JOIN b ON a.x = b.x", "LOOP")]
        [InlineData("SELECT * FROM a LEFT HASH JOIN b ON a.x = b.x", "HASH")]
        [InlineData("SELECT * FROM a INNER JOIN b ON a.x = b.x", "")]
        public void PE005(string sql, string expected) => AssertFlags("PE005", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM t WITH (TABLOCK)", "WITH")]
        [InlineData("SELECT * FROM t WITH (UPDLOCK, ROWLOCK)", "WITH")]
        [InlineData("WITH c AS (SELECT 1 a) SELECT * FROM c", "")]
        public void PE006(string sql, string expected) => AssertFlags("PE006", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM t OPTION (RECOMPILE)", "OPTION")]
        [InlineData("SELECT * FROM t", "")]
        public void PE007(string sql, string expected) => AssertFlags("PE007", sql, expected);

        [Theory]
        [InlineData("SET NOCOUNT OFF", "NOCOUNT OFF")]
        [InlineData("SET NOCOUNT ON", "")]
        public void PE008(string sql, string expected) => AssertFlags("PE008", sql, expected);

        [Theory]
        [InlineData("CREATE PROCEDURE p AS BEGIN SELECT 1\nCREATE TABLE #t (a int) END", "CREATE")]
        [InlineData("CREATE PROCEDURE p AS BEGIN CREATE TABLE #t (a int)\nSELECT 1 END", "")]
        public void PE010(string sql, string expected) => AssertFlags("PE010", sql, expected);

        [Theory]
        [InlineData("CREATE TRIGGER tr ON t AFTER INSERT AS PRINT 'x'", "PRINT")]
        [InlineData("CREATE PROCEDURE p AS PRINT 'x'", "")]
        public void PE011(string sql, string expected) => AssertFlags("PE011", sql, expected);

        [Theory]
        [InlineData("CREATE PROCEDURE p AS BEGIN SET ANSI_WARNINGS OFF SELECT 1 END", "ANSI_WARNINGS")]
        [InlineData("SET ANSI_WARNINGS OFF", "")]
        public void PE012(string sql, string expected) => AssertFlags("PE012", sql, expected);

        [Theory]
        [InlineData("IF (SELECT COUNT(*) FROM t WHERE a = 1) > 0 PRINT 1", "COUNT")]
        [InlineData("IF (SELECT COUNT(*) FROM t) = 0 PRINT 1", "COUNT")]
        [InlineData("IF (SELECT COUNT(*) FROM t) > 5 PRINT 1", "")]
        [InlineData("IF EXISTS (SELECT 1 FROM t) PRINT 1", "")]
        public void PE013(string sql, string expected) => AssertFlags("PE013", sql, expected);

        [Theory]
        [InlineData("IF (SELECT COUNT(*) FROM t WHERE a = 1) > 0 PRINT 1", "IF EXISTS (SELECT 1 FROM t WHERE a = 1) PRINT 1")]
        [InlineData("IF (SELECT COUNT(*) FROM t) = 0 PRINT 1", "IF NOT EXISTS (SELECT 1 FROM t) PRINT 1")]
        public void PE013HasARepair(string sql, string expected) => Assert.Equal(expected, CodeFixes.Apply(sql, Only("PE013")));

        [Theory]
        [InlineData("SET FORCEPLAN ON", "FORCEPLAN")]
        [InlineData("SET NOCOUNT ON", "")]
        public void PE014(string sql, string expected) => AssertFlags("PE014", sql, expected);

        [Theory]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT 1\nOPEN c\nFETCH NEXT FROM c", "CURSOR")]
        [InlineData("DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT 1\nOPEN c", "")]
        [InlineData("DECLARE c CURSOR LOCAL SCROLL FOR SELECT 1\nOPEN c\nFETCH LAST FROM c", "")]
        public void PE015(string sql, string expected) => AssertFlags("PE015", sql, expected);

        [Theory]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT 1\nOPEN c", "c")]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT 1\nOPEN c\nCLOSE c\nDEALLOCATE c", "")]
        public void PE016(string sql, string expected) => AssertFlags("PE016", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM t WHERE a = dbo.f(1, 'x')", "dbo.f(1, 'x')")]
        [InlineData("SELECT * FROM t WHERE a = dbo.f(b)", "")]
        [InlineData("SELECT dbo.f(1) FROM t", "")]
        public void PE017(string sql, string expected) => AssertFlags("PE017", sql, expected);

        [Theory]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT 1", "CURSOR")]
        [InlineData("DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT 1", "")]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT a FROM t FOR READ ONLY", "")]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT a FROM t FOR UPDATE", "")]
        public void PE018(string sql, string expected) => AssertFlags("PE018", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM t WHERE a IN (SELECT b FROM u)", "IN")]
        [InlineData("SELECT * FROM t WHERE a NOT IN (SELECT b FROM u)", "")]
        [InlineData("SELECT * FROM t WHERE a IN (1, 2)", "")]
        public void PE019(string sql, string expected) => AssertFlags("PE019", sql, expected);

        [Theory]
        [InlineData("INSERT INTO t SELECT a FROM u ORDER BY a", "ORDER")]
        [InlineData("INSERT INTO t SELECT TOP 5 a FROM u ORDER BY a", "")]
        [InlineData("SELECT a FROM u ORDER BY a", "")]
        public void PE020(string sql, string expected) => AssertFlags("PE020", sql, expected);

        [Theory]
        [InlineData("CREATE PROCEDURE p WITH RECOMPILE AS SELECT 1", "RECOMPILE")]
        [InlineData("EXEC p WITH RECOMPILE", "RECOMPILE")]
        [InlineData("SELECT * FROM t OPTION (RECOMPILE)", "")]
        public void PE021(string sql, string expected) => AssertFlags("PE021", sql, expected);

        [Theory]
        [InlineData("CREATE TABLE t (a int)", "t")]
        [InlineData("CREATE TABLE dbo.t (a int)", "")]
        [InlineData("CREATE TABLE #t (a int)", "")]
        public void PE023(string sql, string expected) => AssertFlags("PE023", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM Orders", "Orders")]
        [InlineData("SELECT * FROM Orders o JOIN Items i ON o.id = i.id", "Orders|Items")]
        [InlineData("SELECT * FROM dbo.Orders", "")]
        [InlineData("SELECT * FROM #tmp", "")]
        [InlineData("WITH c AS (SELECT 1 a) SELECT * FROM c", "")]
        [InlineData("DELETE FROM Orders WHERE a = 1", "Orders")]
        [InlineData("SELECT * FROM fn(1)", "")]
        public void PE002(string sql, string expected) => AssertFlags("PE002", sql, expected);

        [Theory]
        [InlineData("EXEC p", "p")]
        [InlineData("EXEC dbo.p", "")]
        [InlineData("EXEC sp_help", "")]
        [InlineData("EXEC @r = p 1", "p")]
        public void PE001(string sql, string expected) => AssertFlags("PE001", sql, expected);

        // ---- Script ----
        [Theory]
        [InlineData("SELECT 1 -- TODO: later", "-- TODO: later")]
        [InlineData("SELECT 1 /* BUG here */", "/* BUG here */")]
        [InlineData("SELECT 1 -- fixture", "")]
        [InlineData("SELECT 'TODO'", "")]
        public void SC004(string sql, string expected) => AssertFlags("SC004", sql, expected);

        [Theory]
        [InlineData("CREATE PROCEDURE p AS BEGIN GRANT EXECUTE ON p TO u END", "GRANT")]
        [InlineData("CREATE PROCEDURE p AS SELECT 1\nGO\nGRANT EXECUTE ON p TO u", "")]
        [InlineData("CREATE PROCEDURE p AS BEGIN GRANT EXECUTE ON q TO u END", "")]
        public void SC005(string sql, string expected) => AssertFlags("SC005", sql, expected);

        [Fact]
        public void SC006ReportsAndRepairsLoneLineFeeds()
        {
            Assert.Empty(Flagged("SC006", "SELECT 1\r\nSELECT 2\r\n"));
            Assert.Single(Flagged("SC006", "SELECT 1\nSELECT 2"));
            Assert.Equal("SELECT 1\r\nSELECT 2", CodeFixes.Apply("SELECT 1\nSELECT 2", Only("SC006")));
        }

        // ---- Style ----
        [Theory]
        [InlineData("SELECT total = SUM(x) FROM t", "total =")]
        [InlineData("SELECT a, total = SUM(x) FROM t", "total =")]
        [InlineData("SELECT @v = a FROM t", "")]
        [InlineData("SELECT SUM(x) AS total FROM t", "")]
        [InlineData("SELECT a FROM t WHERE b = 1", "")]
        public void ST002(string sql, string expected) => AssertFlags("ST002", sql, expected);

        [Fact]
        public void ST002HasARepair() =>
            Assert.Equal("SELECT SUM(x) AS total, b FROM t", CodeFixes.Apply("SELECT total = SUM(x), b FROM t", Only("ST002")));

        [Theory]
        [InlineData("CREATE PROCEDURE p AS SELECT 1", "SELECT")]
        [InlineData("CREATE PROCEDURE p AS BEGIN SELECT 1 END", "")]
        public void ST003(string sql, string expected) => AssertFlags("ST003", sql, expected);

        [Theory]
        [InlineData("DELETE TOP 10 FROM t", "TOP")]
        [InlineData("UPDATE TOP 10 t SET a = 1", "TOP")]
        [InlineData("DELETE TOP (10) FROM t", "")]
        [InlineData("INSERT INTO t SELECT TOP 10 a FROM u", "")]
        public void ST006(string sql, string expected) => AssertFlags("ST006", sql, expected);

        [Fact]
        public void ST006HasARepair() =>
            Assert.Equal("DELETE TOP (10) FROM t", CodeFixes.Apply("DELETE TOP 10 FROM t", Only("ST006")));

        [Theory]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT 1\nDECLARE c CURSOR LOCAL FOR SELECT 2", "c")]
        [InlineData("DECLARE c CURSOR LOCAL FOR SELECT 1\nGO\nDECLARE c CURSOR LOCAL FOR SELECT 2", "")]
        public void ST007(string sql, string expected) => AssertFlags("ST007", sql, expected);

        [Theory]
        [InlineData("EXEC p 1, 2", "1")]
        [InlineData("EXEC dbo.p @a = 1, 2", "2")]
        [InlineData("EXEC p @a = 1, @b = 2", "")]
        [InlineData("EXEC p", "")]
        [InlineData("EXEC sp_executesql @sql", "")]
        [InlineData("EXEC (@sql)", "")]
        public void ST008(string sql, string expected) => AssertFlags("ST008", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM dbo.a JOIN dbo.b ON 1 = 1", "dbo.a|dbo.b")]
        [InlineData("SELECT * FROM dbo.a x JOIN dbo.b ON 1 = 1", "dbo.b")]
        [InlineData("SELECT * FROM dbo.a", "")]
        [InlineData("SELECT * FROM a x, b y", "")]
        [InlineData("SELECT * FROM a, b y", "a")]
        public void ST010(string sql, string expected) => AssertFlags("ST010", sql, expected);

        [Theory]
        [InlineData("SELECT * FROM t WHERE a != 1", "!=")]
        [InlineData("SELECT * FROM t WHERE a <> 1", "")]
        [InlineData("SELECT * FROM t WHERE a ! = 1", "")]
        public void ST013(string sql, string expected) => AssertFlags("ST013", sql, expected);

        [Fact]
        public void ST013HasARepair() =>
            Assert.Equal("SELECT * FROM t WHERE a <> 1", CodeFixes.Apply("SELECT * FROM t WHERE a != 1", Only("ST013")));

        // ---- Rules that need the metadata ----
        private static SqlPrompter.Core.Model.IDatabaseMetadata Sample() => TestSupport.SampleDatabase();

        [Fact]
        public void MI003ReportsUnqualifiedColumnsWithARepair()
        {
            var sql = "SELECT Name FROM Users u JOIN Orders o ON u.Id = o.UserId";
            var issues = CodeAnalyzer.Analyze(sql, Only("MI003"), Sample()).Where(i => i.Code == "MI003").ToList();

            Assert.Contains(issues, i => sql.Substring(i.Start, i.Length) == "Name");
            Assert.All(issues, i => Assert.NotNull(i.Fix));
            Assert.StartsWith("SELECT u.Name FROM", CodeFixes.Apply(sql, Only("MI003"), Sample()));
        }

        [Fact]
        public void MI003IsSilentWithoutMetadataAndForASingleSource()
        {
            Assert.Empty(Flagged("MI003", "SELECT Name FROM Users u JOIN Orders o ON u.Id = o.UserId"));
            Assert.Empty(CodeAnalyzer.Analyze("SELECT Name FROM Users", Only("MI003"), Sample()));
        }

        [Fact]
        public void PE002HasARepairFromTheMetadata()
        {
            var fixedText = CodeFixes.Apply("SELECT * FROM Users", Only("PE002"), Sample());

            Assert.Equal("SELECT * FROM dbo.Users", fixedText);
        }

        [Fact]
        public void AllRulesOnALargeScriptStayFast()
        {
            var script = new System.Text.StringBuilder();
            for (var i = 0; i < 1500; i++)
            {
                script.Append("DECLARE @v").Append(i).Append(" int = ").Append(i).Append("\nSELECT a, b, CASE WHEN c = 1 THEN 2 END FROM dbo.T").Append(i).Append(" x JOIN dbo.U y ON x.id = y.id WHERE x.a IN (1, 2) ORDER BY 1\n");
                script.Append("DECLARE c").Append(i).Append(" CURSOR LOCAL FOR SELECT a FROM t\nOPEN c").Append(i).Append("\nFETCH NEXT FROM c").Append(i).Append(" INTO @v").Append(i).Append("\nCLOSE c").Append(i).Append("\nDEALLOCATE c").Append(i).Append("\nGO\n");
            }

            var all = new AnalysisSettings();
            foreach (var rule in AnalysisRules.All)
            {
                all.SetOn(rule, true);
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var issues = CodeAnalyzer.Analyze(script.ToString(), all, Sample());
            watch.Stop();

            Assert.NotEmpty(issues);
            Assert.True(watch.ElapsedMilliseconds < 4000, "took " + watch.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void IncompleteCodeNeverThrows()
        {
            foreach (var sql in new[] { "CREATE TABLE", "SELECT (SELECT", "DECLARE c CURSOR", "FETCH", "CREATE PROCEDURE", "SELECT CASE WHEN", "EXEC", "ALTER TABLE t ADD", "DBCC", "SET", "GRANT", "DROP INDEX", "SELECT a = ", "IF (SELECT COUNT(", "WAITFOR" })
            {
                var all = new AnalysisSettings();
                foreach (var rule in AnalysisRules.All)
                {
                    all.SetOn(rule, true);
                }

                CodeAnalyzer.Analyze(sql, all, Sample());
            }
        }
    }
}
