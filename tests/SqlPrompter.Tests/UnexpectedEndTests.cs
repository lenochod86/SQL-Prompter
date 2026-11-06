using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class UnexpectedEndTests
    {
        private static string[] Find(string sql, bool rule = true)
        {
            var settings = new AnalysisSettings { UnexpectedEnd = rule };
            return CodeAnalyzer.Analyze(sql, settings).Where(i => i.Kind == CodeIssueKind.UnexpectedEnd).Select(i => sql.Substring(i.Start, i.Length) + "|" + i.Message).ToArray();
        }

        [Fact]
        public void TheExampleOfTheIssueIsReported()
        {
            var found = Find(";WITH cte AS (\r\nSELECT *\r\nFROM");

            Assert.Contains(found, f => f.StartsWith("(|") && f.Contains("parenthesis"));
            Assert.Equal(1, found.Count(f => f.Contains("parenthesis")));
        }

        [Fact]
        public void AnUnclosedParenthesisIsUnderlinedAtTheOpeningOne() =>
            Assert.Equal(new[] { "(|Unexpected end: the parenthesis is not closed." }, Find("SELECT (1 + 2"));

        [Theory]
        [InlineData("SELECT 1 FROM t WHERE a = (SELECT MAX(b) FROM u WHERE c = 1")]
        [InlineData("SELECT COUNT(")]
        public void NestedUnclosedParenthesesAreReportedOnce(string sql) =>
            Assert.Contains(Find(sql), f => f.Contains("parenthesis"));

        [Fact]
        public void ACteWithoutAStatementIsReported() =>
            Assert.Equal(new[] { "WITH|Unexpected end: the CTE cte is not followed by a statement." }, Find("WITH cte AS (SELECT 1 AS a)"));

        [Fact]
        public void ACteWithoutAStatementBeforeGoIsReported() =>
            Assert.Equal(new[] { "WITH|Unexpected end: the CTE b is not followed by a statement." }, Find("WITH a AS (SELECT 1 AS x), b (y) AS (SELECT 2)\r\nGO\r\nSELECT 1"));

        [Theory]
        [InlineData("SELECT * FROM", "FROM")]
        [InlineData("SELECT * FROM t WHERE", "WHERE")]
        [InlineData("SELECT * FROM t WHERE a = 1 AND", "AND")]
        [InlineData("SELECT * FROM t WHERE a = 1 OR", "OR")]
        [InlineData("SELECT * FROM t INNER JOIN", "JOIN")]
        [InlineData("SELECT * FROM t JOIN u ON", "ON")]
        [InlineData("SELECT a FROM t GROUP BY", "BY")]
        [InlineData("SELECT a FROM t ORDER BY", "BY")]
        [InlineData("UPDATE t SET", "SET")]
        [InlineData("INSERT INTO", "INTO")]
        [InlineData("INSERT INTO t VALUES", "VALUES")]
        [InlineData("EXEC", "EXEC")]
        [InlineData("SELECT", "SELECT")]
        [InlineData("SELECT * FROM t WHERE a =", "=")]
        [InlineData("SELECT a,", ",")]
        [InlineData("SELECT * FROM t;", null)]
        public void AClauseWithoutItsContentIsReported(string sql, string underlined)
        {
            var found = Find(sql);
            if (underlined == null)
            {
                Assert.Empty(found);
            }
            else
            {
                Assert.Equal(underlined, found.Single().Split('|')[0]);
            }
        }

        [Fact]
        public void AClauseWithoutContentBeforeGoIsReported() =>
            Assert.Equal("FROM", Find("SELECT * FROM\r\nGO\r\nSELECT 1").Single().Split('|')[0]);

        [Theory]
        [InlineData("SELECT 'abc", "'abc")]
        [InlineData("SELECT [abc", "[abc")]
        [InlineData("SELECT 1 /* open", "/* open")]
        public void UnterminatedTokensAreReported(string sql, string underlined) =>
            Assert.Equal(underlined, Find(sql).First().Split('|')[0]);

        [Theory]
        [InlineData("BEGIN SELECT 1", "BEGIN")]
        [InlineData("SELECT CASE WHEN a = 1 THEN 2 FROM t", "CASE")]
        [InlineData("BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 2", "BEGIN")]
        public void BlocksWithoutEndAreReported(string sql, string underlined) =>
            Assert.Equal(underlined, Find(sql).Single(f => f.Contains("no END")).Split('|')[0]);

        [Theory]
        [InlineData("WITH cte AS (SELECT 1 AS a) SELECT * FROM cte")]
        [InlineData(";WITH a AS (SELECT 1 AS x), b AS (SELECT 2 AS y) INSERT INTO t SELECT * FROM a")]
        [InlineData("WITH cte AS (SELECT 1 AS a) UPDATE t SET a = 1")]
        [InlineData("SELECT * FROM (SELECT (1 + 2) AS a, 'text (' AS b) x /* ( */")]
        [InlineData("SELECT 1 -- WHERE")]
        [InlineData("SET NOCOUNT ON")]
        [InlineData("SET XACT_ABORT ON;\r\nGO\r\nSELECT 1")]
        [InlineData("BEGIN TRAN\r\nUPDATE t SET a = 1\r\nCOMMIT")]
        [InlineData("BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 2 END CATCH")]
        [InlineData("IF 1 = 1 BEGIN SELECT CASE WHEN 1 = 1 THEN 1 ELSE 2 END END")]
        [InlineData("SELECT * FROM t")]
        [InlineData("SELECT TOP (10) * FROM t ORDER BY a")]
        [InlineData("SELECT COUNT(*)")]
        [InlineData("DECLARE @a int = 1")]
        [InlineData("END CONVERSATION @h")]
        [InlineData("SELECT * FROM t JOIN u ON t.a = u.a")]
        [InlineData("")]
        public void CompleteScriptsReportNothing(string sql) => Assert.Empty(Find(sql));

        [Fact]
        public void ARuleSwitchedOffReportsNothing() => Assert.Empty(Find("SELECT * FROM", rule: false));

        [Fact]
        public void ASetOnThatEndsAJoinConditionStatementIsStillReported() =>
            Assert.Equal("ON", Find("SELECT * FROM t JOIN u ON").Single().Split('|')[0]);

        [Fact]
        public void OnlyTheBatchThatEndsIncompleteIsReported()
        {
            var found = Find("SELECT * FROM t\r\nGO\r\nSELECT * FROM u WHERE\r\nGO\r\nSELECT 1");

            Assert.Equal(new[] { "WHERE" }, found.Select(f => f.Split('|')[0]).ToArray());
        }
    }
}
