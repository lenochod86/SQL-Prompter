using System;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The session of the open tabs around a shutdown of SSMS (#303).</summary>
    public class TabShutdownGuardTests
    {
        private static readonly DateTime Start = new DateTime(2026, 11, 7, 12, 0, 0);

        [Fact]
        public void NothingIsPendingUntilAShutdownBegins()
        {
            var guard = new TabShutdownGuard();

            Assert.False(guard.Pending);
            Assert.False(guard.Poll(Start, true));
        }

        [Fact]
        public void ADialogThatKeepsTheWindowDisabledKeepsTheShutdownPending()
        {
            var guard = new TabShutdownGuard();
            guard.Begin();

            for (var i = 0; i < 100; i++)
            {
                Assert.False(guard.Poll(Start.AddSeconds(i), false));
            }

            Assert.True(guard.Pending);
        }

        [Fact]
        public void AWindowThatStaysEnabledMeansTheShutdownWasCancelled()
        {
            var guard = new TabShutdownGuard();
            guard.Begin();

            Assert.False(guard.Poll(Start, true));
            Assert.False(guard.Poll(Start.AddSeconds(2), true));
            Assert.True(guard.Poll(Start.AddSeconds(3), true));
            Assert.False(guard.Pending);
            Assert.False(guard.Poll(Start.AddSeconds(5), true));
        }

        [Fact]
        public void ABriefEnabledMomentBetweenDialogsDoesNotCancel()
        {
            var guard = new TabShutdownGuard();
            guard.Begin();

            Assert.False(guard.Poll(Start, true));
            Assert.False(guard.Poll(Start.AddSeconds(1), false));
            Assert.False(guard.Poll(Start.AddSeconds(2), true));
            Assert.False(guard.Poll(Start.AddSeconds(4), true));
            Assert.True(guard.Poll(Start.AddSeconds(5), true));
        }

        [Theory]
        [InlineData(0, true, false, true, 0, true)]   // the last tab closed by hand: an empty list is written at once
        [InlineData(0, true, true, true, 0, false)]   // closes during a shutdown are not the user's
        [InlineData(0, false, false, true, 0, false)] // a start that restored nothing never wipes the saved tabs
        [InlineData(0, true, false, false, 1, false)] // the timer waits for a second empty tick
        [InlineData(0, true, false, false, 2, true)]
        [InlineData(3, true, false, false, 0, true)]
        public void TheSessionIsWrittenWhenTheUserChangedIt(int tabs, bool sawTabs, bool shuttingDown, bool userChange, int emptyTicks, bool expected)
        {
            Assert.Equal(expected, TabSessionPolicy.ShouldSave(tabs, sawTabs, shuttingDown, userChange, emptyTicks));
        }
    }
}
