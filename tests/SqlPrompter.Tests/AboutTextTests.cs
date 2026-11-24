using System;
using SqlPrompter.Core.Help;
using Xunit;

namespace SqlPrompter.Tests
{
    public class AboutTextTests
    {
        [Fact]
        public void VersionLineHandlesAMissingVersion()
        {
            Assert.Equal("0.200.61", AboutText.VersionLine(new Version(0, 200, 61)));
            Assert.Equal("unknown", AboutText.VersionLine(null));
        }

        [Theory]
        [InlineData("0.200.61", "0.200.62", true, false, "Version 0.200.62 is available.")]
        [InlineData("0.200.61", "0.200.61", true, false, "You have the latest version.")]
        [InlineData("0.200.62", "0.200.61", true, false, "You have the latest version.")]
        [InlineData("0.200.61", null, true, true, "The latest version could not be found.")]
        [InlineData("0.200.61", null, false, false, "Not checked in this session.")]
        public void UpdateStatusSaysWhatTheLastCheckFound(string current, string latest, bool checkedYet, bool failed, string expected)
        {
            Assert.Equal(expected, AboutText.UpdateStatus(Version.Parse(current), latest == null ? null : Version.Parse(latest), checkedYet, failed));
        }

        [Fact]
        public void DiagnosticsListsWhatIsKnownAndSkipsTheRest()
        {
            var text = AboutText.Diagnostics(new Version(0, 200, 61), "22.1.1", ".NET Framework 4.8", null, "C:/log.txt", "");
            Assert.Equal("SQL Prompter 0.200.61\r\nSSMS build: 22.1.1\r\nRuntime: .NET Framework 4.8\r\nLog: C:/log.txt".Replace("\r\n", Environment.NewLine), text);
        }
    }
}
