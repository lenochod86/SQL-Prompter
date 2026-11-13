using SqlPrompter.Core.Coloring;
using Xunit;

namespace SqlPrompter.Tests
{
    public class TabColorGuardTests
    {
        private const int Red = unchecked((int)0xFFFF2F34);
        private const int ShellGreen = unchecked((int)0xFF6BA02B);

        [Fact]
        public void OurColorAndTheColorizedFlagAreInPlace()
        {
            Assert.True(TabColorGuard.IsInPlace(Red, true, Red));
        }

        [Fact]
        public void AnotherColorTheFlagOffOrNoBrushAreNotInPlace()
        {
            Assert.False(TabColorGuard.IsInPlace(ShellGreen, true, Red));
            Assert.False(TabColorGuard.IsInPlace(Red, false, Red));
            Assert.False(TabColorGuard.IsInPlace(null, true, Red));
        }

        [Fact]
        public void AShellChangeIsAnsweredOnlyForOurWindowsAndNeverWhileWeSet()
        {
            Assert.True(TabColorGuard.MustReassert(true, false, ShellGreen, true, Red));
            Assert.False(TabColorGuard.MustReassert(true, false, Red, true, Red));
            Assert.False(TabColorGuard.MustReassert(false, false, ShellGreen, true, Red));
            Assert.False(TabColorGuard.MustReassert(true, true, ShellGreen, true, Red));
        }
    }
}
