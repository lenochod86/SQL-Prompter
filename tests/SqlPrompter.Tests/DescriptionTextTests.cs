using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The text of the database descriptions next to the list (#206).</summary>
    public class DescriptionTextTests
    {
        private static KeyValuePair<string, string> C(string name, string text) => new KeyValuePair<string, string>(name, text);

        [Fact]
        public void TheObjectDescriptionComesFirstThenTheDescribedColumns() =>
            Assert.Equal("The customers\nId: key\nName: full name", DescriptionText.Format("The customers", new[] { C("Id", "key"), C("Name", "full name") }));

        [Fact]
        public void NothingToShowGivesNull()
        {
            Assert.Null(DescriptionText.Format(null, null));
            Assert.Null(DescriptionText.Format("  ", new[] { C("A", null), C("B", " ") }));
        }

        [Fact]
        public void ColumnsWithoutTextAreLeftOut() =>
            Assert.Equal("Id: key", DescriptionText.Format(null, new[] { C("A", null), C("Id", "key") }));

        [Fact]
        public void LongTextsAreShortenedAndColumnTextsStayOnOneLine()
        {
            var text = DescriptionText.Format(new string('x', 1000), new[] { C("Id", "line one\r\nline two") });

            Assert.Equal(DescriptionText.MaxObjectLength + 3, text.Split('\n')[0].Length);
            Assert.EndsWith("Id: line one line two", text);
        }

        [Fact]
        public void ManyColumnsAreCounted()
        {
            var columns = Enumerable.Range(1, 15).Select(i => C("C" + i, "d")).ToList();

            var lines = DescriptionText.Format(null, columns).Split('\n');

            Assert.Equal(DescriptionText.MaxColumns + 1, lines.Length);
            Assert.Equal("... and 3 more described columns", lines.Last());
        }
    }
}
