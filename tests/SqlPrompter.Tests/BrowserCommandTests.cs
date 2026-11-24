using SqlPrompter.Core.Help;
using Xunit;

namespace SqlPrompter.Tests
{
    public class BrowserCommandTests
    {
        private const string Url = "file:///C:/Ext/help/index.html";

        [Fact]
        public void EdgeStyleCommandGetsTheAddressInPlaceOfThePlaceholder()
        {
            Assert.True(BrowserCommand.TryBuild("\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" --single-argument %1", Url, out var program, out var arguments));

            Assert.Equal("C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe", program);
            Assert.Equal("--single-argument " + Url, arguments);
        }

        [Fact]
        public void QuotedPlaceholderKeepsItsQuotes()
        {
            Assert.True(BrowserCommand.TryBuild("\"C:\\Firefox\\firefox.exe\" -osint -url \"%1\"", Url, out var program, out var arguments));

            Assert.Equal("C:\\Firefox\\firefox.exe", program);
            Assert.Equal("-osint -url \"" + Url + "\"", arguments);
        }

        [Fact]
        public void ACommandWithoutAPlaceholderGetsTheAddressAtTheEnd()
        {
            Assert.True(BrowserCommand.TryBuild("\"C:\\Browser\\b.exe\"", Url, out var program, out var arguments));

            Assert.Equal("C:\\Browser\\b.exe", program);
            Assert.Equal("\"" + Url + "\"", arguments);
        }

        [Fact]
        public void AnUnquotedProgramEndsAtTheFirstSpace()
        {
            Assert.True(BrowserCommand.TryBuild("browser.exe %1", Url, out var program, out var arguments));

            Assert.Equal("browser.exe", program);
            Assert.Equal(Url, arguments);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\"unterminated")]
        [InlineData("\"\" %1")]
        public void NothingUsableGivesFalse(string command)
        {
            Assert.False(BrowserCommand.TryBuild(command, Url, out _, out _));
        }
    }
}
