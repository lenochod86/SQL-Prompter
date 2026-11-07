using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Snippets;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>Tab stops between the slots of a window function and its parameter info (#215).</summary>
    public class WindowFunctionSlotTests
    {
        private static Suggestion Window(string name) =>
            SuggestionBuilder.Build(Analyze("SELECT |"), SampleDatabase()).First(i => i.DisplayText == name + "() OVER (...)");

        [Fact]
        public void ARankingFunctionHasFieldsForPartitionAndOrder()
        {
            var snippet = Assert.IsType<Snippet>(Window("ROW_NUMBER").Tag);
            var expanded = SnippetExpander.Expand(snippet, new SnippetContext());

            Assert.Equal("ROW_NUMBER() OVER (PARTITION BY partition_column ORDER BY order_column)", expanded.Text);
            Assert.Equal(new[] { "partition", "order" }, expanded.Fields.Select(f => f.Name).ToArray());
        }

        [Fact]
        public void AFunctionWithAnArgumentStartsWithTheArgumentField()
        {
            var snippet = Assert.IsType<Snippet>(Window("SUM").Tag);
            var expanded = SnippetExpander.Expand(snippet, new SnippetContext());

            Assert.Equal("SUM(column) OVER (PARTITION BY partition_column ORDER BY order_column)", expanded.Text);
            Assert.Equal(new[] { "argument", "partition", "order" }, expanded.Fields.Select(f => f.Name).ToArray());
        }

        [Fact]
        public void TheInsertTextAndCaretBackStillDescribeTheFallback()
        {
            var item = Window("ROW_NUMBER");

            Assert.Equal("ROW_NUMBER() OVER (PARTITION BY  ORDER BY )", item.InsertText);
            Assert.Equal(" ORDER BY )".Length, item.CaretBack);
        }

        [Theory]
        [InlineData("SELECT LAG(|", "LAG", 0)]
        [InlineData("SELECT LAG(Id, |", "LAG", 1)]
        [InlineData("SELECT LEAD(Id, 1, |", "LEAD", 2)]
        [InlineData("SELECT NTILE(|", "NTILE", 0)]
        [InlineData("SELECT SUM(|", "SUM", 0)]
        [InlineData("SELECT first_value(|", "FIRST_VALUE", 0)]
        public void ParameterInfoShowsTheArgumentsOfAWindowFunction(string marked, string name, int current)
        {
            var caret = marked.IndexOf('|');
            var info = ParameterInfoFinder.Find(marked.Remove(caret, 1), caret, SampleDatabase());

            Assert.NotNull(info);
            Assert.Equal(name, info.Name);
            Assert.Equal(current, info.Current);
        }

        [Fact]
        public void ParameterInfoWorksWithoutMetadataToo()
        {
            var info = ParameterInfoFinder.Find("SELECT LAG(", "SELECT LAG(".Length, null);

            Assert.Equal(3, info.Parameters.Count);
        }

        [Fact]
        public void FunctionsWithoutArgumentsAndPastTheLastParameterShowNothingOrMinusOne()
        {
            Assert.Null(ParameterInfoFinder.Find("SELECT ROW_NUMBER(", "SELECT ROW_NUMBER(".Length, SampleDatabase()));
            Assert.Equal(-1, ParameterInfoFinder.Find("SELECT LAG(a, b, c, ", "SELECT LAG(a, b, c, ".Length, SampleDatabase()).Current);
        }
    }
}
