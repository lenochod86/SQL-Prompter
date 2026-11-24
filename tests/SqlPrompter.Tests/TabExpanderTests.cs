using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Tabs shown at the tab stops of the editor (#450).</summary>
    public class TabExpanderTests
    {
        [Theory]
        [InlineData("\tx", 4, "    x")]
        [InlineData("\tx", 2, "  x")]
        [InlineData("\tx", 8, "        x")]
        [InlineData("ab\tx", 4, "ab  x")]
        [InlineData("abcd\tx", 4, "abcd    x")]
        [InlineData("a\t\tx", 4, "a       x")]
        [InlineData(" \tx", 4, "    x")]
        [InlineData("\t \tx", 4, "        x")]
        [InlineData("  \t  \tx", 4, "        x")]
        public void ATabGoesToTheNextTabStop(string text, int size, string expected) => Assert.Equal(expected, TabExpander.Expand(text, size));

        [Fact]
        public void EveryLineStartsAtColumnZero()
        {
            Assert.Equal("a   b\r\n    c\r\n\r\nd   e", TabExpander.Expand("a\tb\r\n\tc\r\n\r\nd\te", 4));
            Assert.Equal("a   b\n    c", TabExpander.Expand("a\tb\n\tc", 4));
            Assert.Equal("a   b\r    c", TabExpander.Expand("a\tb\r\tc", 4));
        }

        [Fact]
        public void TheContinuationLinesOfAnExecLineUpUnderTheFirstParameter()
        {
            // Four tabs after "EXEC x @a = 1" style indentation: the same text the editor shows with a tab size of 4.
            var text = "EXEC dbo.p @a = 1\n\t\t\t\t\t\t\t, @b = 2";

            Assert.Equal("EXEC dbo.p @a = 1\n" + new string(' ', 28) + ", @b = 2", TabExpander.Expand(text, 4));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("no tabs", "no tabs")]
        public void NothingToExpand(string text, string expected) => Assert.Equal(expected, TabExpander.Expand(text, 4));

        [Fact]
        public void ABadTabSizeFallsBackToFour() => Assert.Equal("    x", TabExpander.Expand("\tx", 0));
    }
}
