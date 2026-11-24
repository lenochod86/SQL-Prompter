using SqlPrompter.Core.Completion;
using Xunit;

namespace SqlPrompter.Tests
{
    public class SoftSpaceCommitTests
    {
        [Theory]
        [InlineData("WHE", "WHERE")]
        [InlineData("whe", "WHERE")]
        [InlineData("where", "WHERE")]
        [InlineData("join", "JOIN")]
        [InlineData("or", "OR")]
        [InlineData("inn", "INNER JOIN")]
        [InlineData("gro", "GROUP BY")]
        public void SpaceCommitsAKeyword(string typed, string keyword) => Assert.True(SoftSpaceCommit.Commits(typed, keyword));

        [Theory]
        [InlineData("ce", "WHERE")]
        [InlineData("w", "WHERE")]
        [InlineData("wh", "WHERE")]
        [InlineData("j", "JOIN")]
        [InlineData("wherex", "WHERE")]
        [InlineData("", "WHERE")]
        public void SpaceKeepsAnAliasAsTyped(string typed, string keyword) => Assert.False(SoftSpaceCommit.Commits(typed, keyword));
    }
}
