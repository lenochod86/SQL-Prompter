using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    public class FormatSampleTests
    {
        [Fact]
        public void TheSampleIsFormattedIntoSeveralLinesWithTheDefaultStyle()
        {
            var result = SqlFormatter.Format(FormatSample.Text, new FormatStyle());

            Assert.True(result.Formatted >= 6);
            Assert.Equal(0, result.Kept);
            Assert.True(result.Text.Split('\n').Length > FormatSample.Text.Split('\n').Length);
            Assert.Contains("SELECT", result.Text);
        }

        [Fact]
        public void TheStyleChangesTheSample()
        {
            var commas = SqlFormatter.Format(FormatSample.Text, new FormatStyle { CommaBeforeItems = true }).Text;
            var plain = SqlFormatter.Format(FormatSample.Text, new FormatStyle { CommaBeforeItems = false }).Text;

            Assert.NotEqual(plain, commas);
        }
    }
}
