using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    public class TabSessionPolicyTests
    {
        [Fact]
        public void OpenTabsAreSavedByTheTimer() =>
            Assert.True(TabSessionPolicy.ShouldSave(3, true, false, false, 0));

        [Fact]
        public void ClosingTheLastTabByHandIsSavedAtOnce() =>
            Assert.True(TabSessionPolicy.ShouldSave(0, true, false, true, 0));

        [Fact]
        public void AnEmptyListFoundByTheTimerWaitsForASecondTick()
        {
            Assert.False(TabSessionPolicy.ShouldSave(0, true, false, false, 1));
            Assert.True(TabSessionPolicy.ShouldSave(0, true, false, false, 2));
        }

        [Fact]
        public void NothingIsSavedOnceSsmsShutsDown()
        {
            Assert.False(TabSessionPolicy.ShouldSave(2, true, true, false, 0));
            Assert.False(TabSessionPolicy.ShouldSave(0, true, true, true, 5));
        }

        [Fact]
        public void AStartThatSawNoTabsNeverWipesTheSavedOnes()
        {
            Assert.False(TabSessionPolicy.ShouldSave(0, false, false, true, 5));
            Assert.False(TabSessionPolicy.ShouldSave(0, false, false, false, 5));
        }
    }
}
