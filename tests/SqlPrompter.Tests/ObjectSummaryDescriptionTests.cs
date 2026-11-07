using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Model;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The Description column of the Summary tab (#206).</summary>
    public class ObjectSummaryDescriptionTests
    {
        private static KeyValuePair<string, string> D(string name, string text) => new KeyValuePair<string, string>(name, text);

        private static ObjectSummary Summary() => ObjectSummary.For(SampleDatabase(), "dbo", "Users");

        [Fact]
        public void ADescriptionColumnIsAddedWithTheTextOfEachColumn()
        {
            var summary = Summary().WithDescriptions(new[] { D("Id", "Primary key"), D("Email", "Login address") });

            Assert.Equal(new[] { "Column Name", "Data Type", "Nullability", "Description" }, summary.Headers.ToArray());
            Assert.Equal("Primary key", summary.Rows.First(r => r[0] == "Id")[3]);
            Assert.Equal("Login address", summary.Rows.First(r => r[0] == "Email")[3]);
            Assert.Equal(string.Empty, summary.Rows.First(r => r[0] == "Name")[3]);
        }

        [Fact]
        public void TheNamesAreMatchedIgnoringCase() =>
            Assert.Equal("Key", Summary().WithDescriptions(new[] { D("ID", "Key") }).Rows.First(r => r[0] == "Id")[3]);

        [Fact]
        public void NoDescriptionsLeaveTheSummaryAsItIs()
        {
            var summary = Summary();

            Assert.Same(summary, summary.WithDescriptions(new KeyValuePair<string, string>[0]));
            Assert.Same(summary, summary.WithDescriptions(new[] { D("Id", "  ") }));
            Assert.Same(summary, summary.WithDescriptions(null));
        }

        [Fact]
        public void LongTextsAreShortenedToOneLine()
        {
            var summary = Summary().WithDescriptions(new[] { D("Id", "first line\r\nsecond line " + new string('x', 200)) });
            var cell = summary.Rows.First(r => r[0] == "Id")[3];

            Assert.DoesNotContain("\n", cell);
            Assert.StartsWith("first line second line", cell);
            Assert.EndsWith("...", cell);
            Assert.True(cell.Length <= 83);
        }

        [Fact]
        public void TheClipboardTextHasTheNewColumnToo() =>
            Assert.Contains("Description", Summary().WithDescriptions(new[] { D("Id", "Key") }).ToText().Split('\n')[0]);
    }
}
