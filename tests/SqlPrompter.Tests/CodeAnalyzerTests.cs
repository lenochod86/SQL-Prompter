using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class CodeAnalyzerTests
    {
        private static AnalysisSettings All() => new AnalysisSettings { SelectStar = true, LockHint = true };

        private static string[] Find(string sql, AnalysisSettings settings = null) =>
            CodeAnalyzer.Analyze(sql, settings ?? All()).Select(i => i.Kind + ":" + sql.Substring(i.Start, i.Length)).ToArray();

        [Theory]
        [InlineData("SELECT 1 FROM t WHERE a = NULL", "EqualsNull:=")]
        [InlineData("SELECT 1 FROM t WHERE a <> NULL", "EqualsNull:<>")]
        [InlineData("SELECT 1 FROM t WHERE a != null", "EqualsNull:!=")]
        [InlineData("SELECT 1 FROM t WHERE NULL = a", "EqualsNull:=")]
        [InlineData("SELECT 1 FROM t JOIN u ON t.a = NULL", "EqualsNull:=")]
        [InlineData("SELECT CASE WHEN a = NULL THEN 1 END FROM t", "EqualsNull:=")]
        [InlineData("IF @a = NULL PRINT 1", "EqualsNull:=")]
        [InlineData("SELECT 1 FROM t WHERE EXISTS (SELECT 1 FROM u WHERE u.a = NULL)", "EqualsNull:=")]
        public void ComparisonsWithNullAreReported(string sql, string expected) => Assert.Equal(new[] { expected }, Find(sql));

        [Theory]
        [InlineData("UPDATE t SET a = NULL WHERE b = 1")]
        [InlineData("SET @a = NULL")]
        [InlineData("DECLARE @a int = NULL")]
        [InlineData("SELECT a = NULL FROM t")]
        [InlineData("SELECT 1 FROM t WHERE a IS NULL")]
        [InlineData("INSERT INTO t (a) VALUES (NULL)")]
        [InlineData("SELECT 1 FROM t WHERE a = 'NULL'")]
        [InlineData("SELECT 1 -- WHERE a = NULL")]
        public void AssignmentsAndIsNullAreNotReported(string sql) => Assert.DoesNotContain(Find(sql), i => i.StartsWith("EqualsNull"));

        [Fact]
        public void AConditionAfterAnAssignmentInTheSameStatementIsStillChecked() =>
            Assert.Equal(new[] { "EqualsNull:=" }, Find("UPDATE t SET a = NULL WHERE b = NULL"));

        [Theory]
        [InlineData("SELECT * FROM a JOIN b WHERE a.x = 1")]
        [InlineData("SELECT * FROM a INNER JOIN b")]
        [InlineData("SELECT * FROM a LEFT JOIN b;")]
        [InlineData("SELECT * FROM a JOIN b JOIN c ON c.x = a.x")]
        public void AJoinWithoutOnIsReported(string sql) => Assert.Contains(Find(sql), i => i == "JoinWithoutOn:JOIN");

        [Theory]
        [InlineData("SELECT * FROM a JOIN b ON a.x = b.x")]
        [InlineData("SELECT * FROM a CROSS JOIN b")]
        [InlineData("SELECT * FROM a JOIN (SELECT 1 AS x) b ON a.x = b.x")]
        [InlineData("SELECT * FROM a JOIN b ON a.x = b.x JOIN c ON c.x = b.x")]
        [InlineData("SELECT * FROM a CROSS APPLY f(a.x) AS c")]
        public void CompleteJoinsAreNotReported(string sql) => Assert.DoesNotContain(Find(sql), i => i.StartsWith("JoinWithoutOn"));

        [Fact]
        public void OnlyTheJoinWithoutOnIsMarked()
        {
            var sql = "SELECT 1 FROM a JOIN b ON a.x = b.x JOIN c WHERE 1 = 1";

            var issue = CodeAnalyzer.Analyze(sql, All()).Single(i => i.Kind == CodeIssueKind.JoinWithoutOn);

            Assert.Equal(sql.LastIndexOf("JOIN"), issue.Start);
        }

        [Theory]
        [InlineData("INSERT INTO dbo.T VALUES (1)")]
        [InlineData("INSERT dbo.T SELECT 1")]
        [InlineData("INSERT INTO #t EXEC p")]
        [InlineData("INSERT INTO [dbo].[T] SELECT * FROM u")]
        public void InsertWithoutAColumnListIsReported(string sql) => Assert.Contains(Find(sql), i => i == "InsertWithoutColumns:INSERT");

        [Theory]
        [InlineData("INSERT INTO dbo.T (a, b) VALUES (1, 2)")]
        [InlineData("INSERT INTO dbo.T (a) SELECT 1")]
        [InlineData("INSERT INTO @t (a) VALUES (1)")]
        public void InsertWithAColumnListIsNotReported(string sql) => Assert.DoesNotContain(Find(sql), i => i.StartsWith("InsertWithoutColumns"));

        [Fact]
        public void AVariableThatIsNeverUsedIsReported() =>
            Assert.Equal(new[] { "UnusedVariable:@b" }, Find("DECLARE @a int = 1, @b int\nSELECT @a"));

        [Fact]
        public void UsedVariablesAndVariablesInOtherBatchesAreChecked()
        {
            Assert.Empty(Find("DECLARE @a int\nSET @a = 1\nSELECT @a"));
            Assert.Equal(new[] { "UnusedVariable:@a" }, Find("DECLARE @a int\nGO\nSELECT @a"));
        }

        [Fact]
        public void ADefaultThatMentionsAnotherVariableCountsAsUse() =>
            Assert.Equal(new[] { "UnusedVariable:@b" }, Find("DECLARE @a int = 1\nDECLARE @b int = @a"));

        [Fact]
        public void ProcedureParametersAndSystemVariablesAreNotReported()
        {
            Assert.DoesNotContain(Find("CREATE PROCEDURE p @a int AS SELECT 1"), i => i.StartsWith("UnusedVariable"));
            Assert.Empty(Find("SELECT @@ROWCOUNT"));
        }

        [Theory]
        [InlineData("SELECT * FROM t", 1)]
        [InlineData("SELECT a, * FROM t", 1)]
        [InlineData("SELECT t.* FROM t", 1)]
        [InlineData("SELECT TOP (10) * FROM t", 1)]
        [InlineData("SELECT DISTINCT * FROM t", 1)]
        [InlineData("SELECT * FROM t UNION SELECT * FROM u", 2)]
        [InlineData("SELECT COUNT(*) FROM t", 0)]
        [InlineData("SELECT a * b FROM t", 0)]
        [InlineData("SELECT (a + b) * 2 FROM t", 0)]
        [InlineData("SELECT a FROM t WHERE EXISTS (SELECT * FROM u)", 0)]
        [InlineData("SELECT a FROM t WHERE b IN (SELECT 2 * c FROM u)", 0)]
        public void SelectStarIsReportedOnlyInASelectList(string sql, int expected) =>
            Assert.Equal(expected, Find(sql).Count(i => i.StartsWith("SelectStar")));

        [Fact]
        public void LockHintsAreReported() =>
            Assert.Equal(new[] { "LockHint:NOLOCK", "LockHint:READUNCOMMITTED" }, Find("SELECT * FROM a WITH (NOLOCK) JOIN b WITH (READUNCOMMITTED) ON 1 = 1").Where(i => i.StartsWith("Lock")));

        [Fact]
        public void EveryRuleHasItsSwitch()
        {
            var sql = "DECLARE @u int\nSELECT * FROM a WITH (NOLOCK) JOIN b WHERE x = NULL\nINSERT INTO t VALUES (1)";

            Assert.Equal(6, Find(sql).Length);
            Assert.Empty(Find(sql, new AnalysisSettings { Enabled = false, SelectStar = true, LockHint = true }));
            Assert.DoesNotContain(Find(sql, new AnalysisSettings { EqualsNull = false, SelectStar = true, LockHint = true }), i => i.StartsWith("EqualsNull"));
            Assert.DoesNotContain(Find(sql, new AnalysisSettings { JoinWithoutOn = false, SelectStar = true, LockHint = true }), i => i.StartsWith("JoinWithoutOn"));
            Assert.DoesNotContain(Find(sql, new AnalysisSettings { InsertWithoutColumns = false, SelectStar = true, LockHint = true }), i => i.StartsWith("InsertWithoutColumns"));
            Assert.DoesNotContain(Find(sql, new AnalysisSettings { UnusedVariable = false, SelectStar = true, LockHint = true }), i => i.StartsWith("UnusedVariable"));
            Assert.DoesNotContain(Find(sql, new AnalysisSettings { SelectStar = false, LockHint = true }), i => i.StartsWith("SelectStar"));
            Assert.DoesNotContain(Find(sql, new AnalysisSettings { SelectStar = true, LockHint = false }), i => i.StartsWith("LockHint"));
        }

        [Fact]
        public void SelectStarAndLockHintsAreOffByDefault()
        {
            var settings = new AnalysisSettings();

            Assert.True(settings.Enabled);
            Assert.False(settings.SelectStar);
            Assert.False(settings.LockHint);
            Assert.Empty(Find("SELECT * FROM a WITH (NOLOCK)", settings));
        }

        [Fact]
        public void IssuesComeInTheOrderOfTheText()
        {
            var starts = CodeAnalyzer.Analyze("SELECT * FROM a WITH (NOLOCK) WHERE x = NULL", All()).Select(i => i.Start).ToArray();

            Assert.Equal(starts.OrderBy(s => s), starts);
        }
    }
}
