using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The context right after a comment (#412): a comment on a line of its own ends the statement above it as far as the caret is concerned.</summary>
    public class ContextAfterCommentTests
    {
        [Theory]
        [InlineData("SELECT *\nFROM nc\nORDER BY nc.Id\n\n/*\nUPDATE x\nSELECT TOP (100) * ORDER BY 1 DESC\n*/|")]
        [InlineData("SELECT *\nFROM nc\nORDER BY nc.Id\n/* c */\n|")]
        [InlineData("SELECT *\nFROM nc\nORDER BY nc.Id\n-- note\n|")]
        [InlineData("SELECT *\nFROM nc\nORDER BY nc.Id\n-- note\n-- more\n|")]
        public void AfterACommentOnItsOwnLineTheCaretIsAtAStatementStart(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.Keyword, context.Kind);
            Assert.Contains("SELECT", context.PreferredKeywords);
            Assert.DoesNotContain("OFFSET", context.PreferredKeywords);
        }

        [Fact]
        public void TheReportedScriptOffersNoContinuationOfTheStatementAboveTheComment()
        {
            var context = Analyze("SELECT *\nFROM nc\nORDER BY nc.IdNsureContract\n\n/*\nUPDATE ...\nSELECT TOP (100) * FROM nc ORDER BY 1 DESC\n*/|");

            Assert.DoesNotContain("ASC", context.PreferredKeywords);
            Assert.DoesNotContain("UNION", context.PreferredKeywords);
            Assert.NotEqual(ContextKind.Expression, context.Kind);
        }

        [Theory]
        [InlineData("SELECT a /* c */ |")]
        [InlineData("SELECT a /* c */ |FROM t")]
        [InlineData("SELECT * FROM t -- c\nWHERE |")]
        [InlineData("SELECT *\n/* c */ FROM |")]
        public void ACommentInsideAStatementKeepsTheContext(string sql)
        {
            var context = Analyze(sql);

            Assert.DoesNotContain("UPDATE", context.PreferredKeywords ?? new string[0]);
        }

        [Fact]
        public void ACommentBetweenClausesOnItsOwnLineStillContinuesTheStatementOnceAWordIsTyped()
        {
            Assert.Equal(ContextKind.ObjectName, Analyze("SELECT *\n-- the table\nFROM |").Kind);
            Assert.Equal(ContextKind.Expression, Analyze("SELECT *\nFROM dbo.Users u\n/* c */\nWHERE u.|").Kind);
        }

        [Fact]
        public void NothingIsOfferedInsideAComment()
        {
            Assert.Equal(ContextKind.None, Analyze("SELECT 1\n/* a |b */").Kind);
        }
    }
}
