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

        // FROM t ORDER + Space gave ORDER BY, and the BY typed next became GROUP BY (#550): a complete first word is typed, as in #444.
        [Theory]
        [InlineData("ORDER", "ORDER BY")]
        [InlineData("order", "ORDER BY")]
        [InlineData("group", "GROUP BY")]
        [InlineData("inner", "INNER JOIN")]
        [InlineData("left", "LEFT OUTER JOIN")]
        [InlineData("left outer", "LEFT OUTER JOIN")]
        public void SpaceAfterTheFirstWordsOfAKeywordIsTyped(string typed, string keyword) => Assert.False(SoftSpaceCommit.Commits(typed, keyword));
    }
}
