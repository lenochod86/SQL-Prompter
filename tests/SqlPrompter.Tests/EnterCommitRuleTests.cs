using SqlPrompter.Core.Completion;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Enter commits the item of the list the user sees, never a stale editor session next to it (#517).</summary>
    public sealed class EnterCommitRuleTests
    {
        [Fact]
        public void TheEditorSessionDoesNotCommitWhileTheOwnListIsOpen()
        {
            // The list opened after a snippet is inserted while the editor session of the snippet prefix is still there: the arrow keys move the own list.
            Assert.False(EnterCommitRule.EditorSessionShouldCommit(ownListOpen: true, sessionDismissed: false, hardSelection: true, suggestionItemSelected: false, hasSelectedItem: true));
        }

        [Fact]
        public void TheEditorSessionCommitsAHardSelectedItemWhenThereIsNoOwnList()
        {
            Assert.True(EnterCommitRule.EditorSessionShouldCommit(ownListOpen: false, sessionDismissed: false, hardSelection: true, suggestionItemSelected: false, hasSelectedItem: true));
        }

        [Theory]
        [InlineData(true, true, false, true)]
        [InlineData(false, false, false, true)]
        [InlineData(false, true, true, true)]
        [InlineData(false, true, false, false)]
        public void TheEditorSessionDoesNotCommitInTheOtherCases(bool dismissed, bool hard, bool suggestionItem, bool hasItem)
        {
            Assert.False(EnterCommitRule.EditorSessionShouldCommit(ownListOpen: false, sessionDismissed: dismissed, hardSelection: hard, suggestionItemSelected: suggestionItem, hasSelectedItem: hasItem));
        }
    }
}
