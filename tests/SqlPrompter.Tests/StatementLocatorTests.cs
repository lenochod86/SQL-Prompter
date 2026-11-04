using SqlPrompter.Core.Analysis;
using Xunit;

namespace SqlPrompter.Tests
{
    public class StatementLocatorTests
    {
        private static string Find(string sqlWithCaret)
        {
            var caret = sqlWithCaret.IndexOf('|');
            var sql = sqlWithCaret.Remove(caret, 1);
            var range = StatementLocator.Find(sql, caret);
            return range == null ? null : sql.Substring(range.Start, range.Length);
        }

        [Theory]
        [InlineData("SELECT 1; SEL|ECT 2;", "SELECT 2;")]
        [InlineData("SEL|ECT 1; SELECT 2;", "SELECT 1;")]
        [InlineData("SELECT 1;| SELECT 2;", "SELECT 1;")]
        [InlineData("SELECT a,\r\n  b|\r\nFROM dbo.T\r\nWHERE a = 1\r\n\r\nUPDATE dbo.T SET a = 2", "SELECT a,\r\n  b\r\nFROM dbo.T\r\nWHERE a = 1")]
        [InlineData("SELECT 1\r\nSELECT 2|", "SELECT 2")]
        public void StatementAtTheCaret(string sql, string expected)
        {
            Assert.Equal(expected, Find(sql));
        }

        [Fact]
        public void StatementWithoutSemicolonsEndsBeforeTheNextOne()
        {
            Assert.Equal("INSERT INTO dbo.T (a) SELECT 1", Find("DECLARE @x int\r\nINSERT INTO dbo.T (a) SEL|ECT 1\r\nPRINT 'x'"));
        }

        [Fact]
        public void CommentsAroundTheStatementAreNotIncluded()
        {
            Assert.Equal("SELECT 1;", Find("-- note\r\nSELECT |1; -- trailing\r\n"));
        }

        [Fact]
        public void CaretInTheWhiteSpaceAfterAStatementBelongsToIt()
        {
            Assert.Equal("SELECT 1;", Find("SELECT 1;\r\n\r\n|\r\nSELECT 2;"));
        }

        [Fact]
        public void GoLineAndEmptyScriptHaveNoStatement()
        {
            Assert.Null(Find("SELECT 1\r\nG|O\r\nSELECT 2"));
            Assert.Null(StatementLocator.Find(string.Empty, 0));
            Assert.Null(StatementLocator.Find("-- only a comment", 3));
        }

        [Fact]
        public void BatchesSeparatedByGoAreSeparateStatements()
        {
            Assert.Equal("SELECT 2", Find("SELECT 1\r\nGO\r\nSELECT |2"));
        }
    }
}
