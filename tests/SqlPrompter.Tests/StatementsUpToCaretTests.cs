using SqlPrompter.Core.Analysis;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The span from the start of the script to the end of the statement at the caret (#420).</summary>
    public class StatementsUpToCaretTests
    {
        private static string UpTo(string sqlWithCaret)
        {
            var caret = sqlWithCaret.IndexOf('|');
            var sql = sqlWithCaret.Remove(caret, 1);
            var range = StatementLocator.FindUpTo(sql, caret);
            return range == null ? null : sql.Substring(range.Start, range.Length);
        }

        [Theory]
        [InlineData("SEL|ECT 1; SELECT 2; SELECT 3;", "SELECT 1;")]
        [InlineData("SELECT 1; SEL|ECT 2; SELECT 3;", "SELECT 1; SELECT 2;")]
        [InlineData("SELECT 1; SELECT 2; SEL|ECT 3;", "SELECT 1; SELECT 2; SELECT 3;")]
        [InlineData("SELECT 1;\r\nSELECT 2;\r\nSELECT 3|", "SELECT 1;\r\nSELECT 2;\r\nSELECT 3")]
        public void TheCaretStatementIsTheLastOneRun(string sql, string expected) => Assert.Equal(expected, UpTo(sql));

        [Fact]
        public void BetweenStatementsTheLastStatementBeforeTheCaretIsTheLastOneRun()
        {
            Assert.Equal("SELECT 1;\r\nSELECT 2;", UpTo("SELECT 1;\r\nSELECT 2;\r\n\r\n|\r\nSELECT 3;"));
        }

        [Fact]
        public void AfterTheLastStatementEverythingRuns()
        {
            Assert.Equal("SELECT 1;\r\nSELECT 2;", UpTo("SELECT 1;\r\nSELECT 2;\r\n\r\n|"));
        }

        [Fact]
        public void StatementsWithoutSemicolonsEndBeforeTheNextOne()
        {
            Assert.Equal("DECLARE @x int\r\nINSERT INTO dbo.T (a) SELECT 1", UpTo("DECLARE @x int\r\nINSERT INTO dbo.T (a) SEL|ECT 1\r\nPRINT 'x'"));
        }

        [Fact]
        public void TheRunStartsAtTheBeginningOfTheScriptAndIncludesGoLinesAndComments()
        {
            Assert.Equal("-- first\r\nSELECT 1\r\nGO\r\nSELECT 2", UpTo("-- first\r\nSELECT 1\r\nGO\r\nSELECT |2\r\nSELECT 3"));
        }

        [Fact]
        public void ACaretOnAGoLineRunsThroughTheStatementBeforeIt()
        {
            Assert.Equal("SELECT 1", UpTo("SELECT 1\r\nG|O\r\nSELECT 2"));
        }

        [Fact]
        public void NothingToRunInAnEmptyScriptACommentOrBeforeAGoOnly()
        {
            Assert.Null(StatementLocator.FindUpTo(string.Empty, 0));
            Assert.Null(StatementLocator.FindUpTo("-- only a comment", 3));
            Assert.Null(UpTo("G|O\r\nSELECT 1"));
        }

        [Fact]
        public void ACaretBeforeTheFirstStatementRunsTheFirstOne()
        {
            Assert.Equal("-- c\r\nSELECT 1;", UpTo("-- |c\r\nSELECT 1; SELECT 2;"));
        }
    }
}
