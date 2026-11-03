using SqlPrompter.Core.Completion;
using Xunit;

namespace SqlPrompter.Tests
{
    public class CommitKeysTests
    {
        [Fact]
        public void DefaultCommitsOnlyOnSpace()
        {
            var keys = CommitKeys.Default;

            Assert.True(keys.Commits(' '));
            foreach (var c in ".,();]=<>+-*/'")
            {
                Assert.False(keys.Commits(c), "should not commit on '" + c + "'");
            }
        }

        [Fact]
        public void EveryCharacterThatCommitsEndsTheSession()
        {
            var all = new CommitKeys(space: true, dot: true, comma: true, parentheses: true, semicolon: true, closingBracket: true);

            foreach (var c in " .,();]")
            {
                Assert.True(all.Commits(c));
                Assert.Contains(c, all.PotentialCharacters);
            }
        }

        [Fact]
        public void WordCharactersNeverEndTheSession()
        {
            foreach (var c in "a_Z9@#$")
            {
                Assert.DoesNotContain(c, CommitKeys.Default.PotentialCharacters);
            }
        }

        [Fact]
        public void OptionsAreIndependent()
        {
            var dotOnly = new CommitKeys(space: false, dot: true, comma: false, parentheses: false, semicolon: false, closingBracket: false);

            Assert.True(dotOnly.Commits('.'));
            Assert.False(dotOnly.Commits(' '));
            Assert.False(dotOnly.Commits(','));
        }
    }
}
