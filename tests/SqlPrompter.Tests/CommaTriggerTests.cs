using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class CommaTriggerTests
    {
        private static bool Opens(string marked)
        {
            var caret = marked.IndexOf('|');
            var sql = marked.Remove(caret, 1);
            return CommaTrigger.ShouldOpen(sql, caret, Analyze(marked));
        }

        [Theory]
        [InlineData("select 'testpro', |")]
        [InlineData("select 'testpro',|")]
        [InlineData("select a, |")]
        [InlineData("select a,| from dbo.Users")]
        [InlineData("select a, | from dbo.Users")]
        [InlineData("SELECT * FROM dbo.Users u ORDER BY u.Id, |")]
        [InlineData("SELECT * FROM dbo.Users u GROUP BY u.Id, |")]
        [InlineData("UPDATE dbo.Users SET Name = 1, |")]
        [InlineData("SELECT * FROM dbo.Users u, |")]
        [InlineData("SELECT COALESCE(a, |")]
        [InlineData("INSERT INTO dbo.Users (Id, |")]
        [InlineData("SELECT * FROM dbo.Users WHERE Id IN (a, |")]
        public void OpensAtTheNextItem(string sql) => Assert.True(Opens(sql), sql);

        [Theory]
        [InlineData("select a|")]
        [InlineData("select a |")]
        [InlineData("select 'a, |'")]
        [InlineData("select 1 -- a, |")]
        [InlineData("INSERT INTO dbo.Users (Id) VALUES (1, |")]
        [InlineData("INSERT INTO dbo.Users (Id) VALUES (1), (2, |")]
        [InlineData("SELECT * FROM dbo.Users WHERE Id IN (1, 2, |")]
        [InlineData("SELECT * FROM dbo.Users WHERE Name IN ('a', |")]
        [InlineData("DECLARE @a INT, |")]
        public void StaysClosedWhereTheUserTypesValuesOrOwnNames(string sql) => Assert.False(Opens(sql), sql);
    }
}