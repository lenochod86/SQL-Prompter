using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class CtrlTapDetectorTests
    {
        [Fact]
        public void CtrlOnlyIsATap()
        {
            var d = new CtrlTapDetector();
            d.CtrlDown(false, true, 5);
            Assert.True(d.CtrlUp(true, 5));
        }

        [Fact]
        public void AChordThatNeverReachedTheEditorIsNoTap()
        {
            // Ctrl+A or Ctrl+Space: the editor saw only Ctrl down and up, but the thread saw another key.
            var d = new CtrlTapDetector();
            d.CtrlDown(false, true, 5);
            Assert.False(d.CtrlUp(true, 6));
        }

        [Fact]
        public void AKeyTheEditorSawIsNoTap()
        {
            var d = new CtrlTapDetector();
            d.CtrlDown(false, true, 5);
            d.Interrupted();
            Assert.False(d.CtrlUp(true, 5));
        }

        [Fact]
        public void KeyRepeatOfAHeldCtrlIsNoTap()
        {
            var d = new CtrlTapDetector();
            d.CtrlDown(false, true, 5);
            d.CtrlDown(true, true, 5);
            Assert.False(d.CtrlUp(true, 5));
        }

        [Fact]
        public void AnotherModifierAtDownOrUpIsNoTap()
        {
            var d = new CtrlTapDetector();
            d.CtrlDown(false, false, 5);
            Assert.False(d.CtrlUp(true, 5));
            d.CtrlDown(false, true, 5);
            Assert.False(d.CtrlUp(false, 5));
        }

        [Fact]
        public void OneTapIsCountedOnce()
        {
            var d = new CtrlTapDetector();
            d.CtrlDown(false, true, 5);
            Assert.True(d.CtrlUp(true, 5));
            Assert.False(d.CtrlUp(true, 5));
        }

        [Fact]
        public void InputBeforeCtrlDownDoesNotCount()
        {
            var d = new CtrlTapDetector();
            d.CtrlDown(false, true, 9);
            Assert.True(d.CtrlUp(true, 9));
        }
    }
}
