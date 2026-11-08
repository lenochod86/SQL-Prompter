using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The list opens after a space that follows WHERE, GROUP BY, HAVING and similar keywords (#382).</summary>
    public class KeywordSpaceTests
    {
        private static bool Opens(string sql)
        {
            var caret = sql.IndexOf('|');
            var text = sql.Remove(caret, 1);
            return KeywordSpaceTrigger.ShouldOpen(text, caret, Analyze(sql));
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.T t WHERE |")]
        [InlineData("SELECT * FROM dbo.T t WHERE t.A = 1 AND |")]
        [InlineData("SELECT * FROM dbo.T t WHERE t.A = 1 OR |")]
        [InlineData("SELECT * FROM dbo.T t GROUP BY |")]
        [InlineData("SELECT t.A, COUNT(*) FROM dbo.T t GROUP BY t.A HAVING |")]
        [InlineData("SELECT * FROM dbo.T t ORDER BY |")]
        [InlineData("SELECT * FROM dbo.T t JOIN dbo.U u ON |")]
        [InlineData("UPDATE dbo.T SET |")]
        [InlineData("SELECT | FROM dbo.T t")]
        [InlineData("SELECT * FROM dbo.T t WHERE t.A = |")]
        [InlineData("SELECT * FROM dbo.T t WHERE t.A + |")]
        [InlineData("SELECT CASE WHEN |")]
        [InlineData("SELECT * FROM dbo.T t WHERE NOT |")]
        public void ASpaceAfterAKeywordOpensTheList(string sql) => Assert.True(Opens(sql), sql);

        [Theory]
        [InlineData("SELECT * FROM dbo.Orders |")]
        [InlineData("SELECT * FROM dbo.Orders o |")]
        [InlineData("SELECT * FROM dbo.T t WHERE t.A = 1 |")]
        [InlineData("SELECT * FROM dbo.T t WHERE t.A |")]
        [InlineData("SELECT * FROM dbo.T t WHERE t.A IS NULL |")]
        [InlineData("SELECT * FROM dbo.T t ORDER BY t.A DESC |")]
        [InlineData("SELECT * |")]
        [InlineData("SELECT * FROM dbo.T t WHERE t.A = 'x |'")]
        [InlineData("SELECT * FROM dbo.T t -- WHERE |")]
        [InlineData("SELECT * FROM dbo.T t WHERE|")]
        [InlineData("SELECT * FROM dbo.T t WHERE t.A = 1 AND  x|")]
        public void NoListAfterANameALiteralOrInAStringOrAComment(string sql) => Assert.False(Opens(sql), sql);
    }
}
