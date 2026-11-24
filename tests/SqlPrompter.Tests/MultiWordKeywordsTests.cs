using SqlPrompter.Core.Completion;
using Xunit;

namespace SqlPrompter.Tests
{
    public class MultiWordKeywordsTests
    {
        [Theory]
        [InlineData("IS", "IS NULL")]
        [InlineData("is", "IS NOT NULL")]
        [InlineData("IS NOT", "IS NOT NULL")]
        [InlineData("ORDER", "ORDER BY")]
        [InlineData("left", "LEFT JOIN")]
        public void ACompleteFirstWordIsLeading(string typed, string item) => Assert.True(MultiWordKeywords.IsLeadingWords(typed, item));

        [Theory]
        [InlineData("I", "IS NULL")]
        [InlineData("IS N", "IS NOT NULL")]
        [InlineData("IS NULL", "IS NULL")]
        [InlineData("IN", "IN ()")]
        [InlineData("IS", "IS")]
        [InlineData("", "IS NULL")]
        [InlineData("WHERE", "WHERE")]
        public void OtherwiseTheSpaceCommitsAsBefore(string typed, string item) => Assert.False(MultiWordKeywords.IsLeadingWords(typed, item));
    }
}
