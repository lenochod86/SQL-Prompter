using System.Linq;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>Window functions: OVER after a ranking function and the items with their OVER clause (#215).</summary>
    public class WindowFunctionTests
    {
        private static System.Collections.Generic.IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), SampleDatabase());

        [Theory]
        [InlineData("SELECT ROW_NUMBER() |")]
        [InlineData("SELECT RANK() |")]
        [InlineData("SELECT NTILE(4) |")]
        [InlineData("SELECT LAG(Id) |")]
        [InlineData("SELECT Id, DENSE_RANK() | FROM dbo.Users")]
        public void OverComesFirstAfterAFunctionThatNeedsIt(string sql)
        {
            var keywords = Items(sql).Where(i => i.Kind == SuggestionKind.Keyword).ToList();
            var first = keywords.OrderBy(k => k.Group).First();

            Assert.Equal("OVER", first.InsertText, true);
            Assert.True(first.Group < keywords.Where(k => k.DisplayText.ToUpperInvariant() == "FROM").Min(k => k.Group));
        }

        [Fact]
        public void OverIsPreselectedAfterARankingFunction()
        {
            var context = Analyze("SELECT ROW_NUMBER() |");

            Assert.Equal(new[] { "OVER" }, context.PreferredKeywords);
            Assert.False(context.SoftSelection);
        }

        [Fact]
        public void OverIsNotFirstAfterAnOrdinaryAggregate()
        {
            var keywords = Items("SELECT COUNT(*) |").Where(i => i.Kind == SuggestionKind.Keyword).OrderBy(i => i.Group).ToList();

            Assert.False(string.Equals("OVER", keywords.First().InsertText, System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void RankingFunctionHasAnItemWithItsOverClauseAndTheCaretInPartitionBy()
        {
            var item = Items("SELECT |").Single(i => i.DisplayText == "ROW_NUMBER() OVER (...)");

            Assert.Equal("ROW_NUMBER() OVER (PARTITION BY  ORDER BY )", item.InsertText);
            Assert.Equal(" ORDER BY )".Length, item.CaretBack);
            Assert.Equal("ROW_NUMBER() OVER (PARTITION BY ", item.InsertText.Substring(0, item.InsertText.Length - item.CaretBack));
        }

        [Fact]
        public void AggregateAndAnalyticFunctionsPutTheCaretInTheArgument()
        {
            foreach (var name in new[] { "SUM", "COUNT", "AVG", "MIN", "MAX", "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE", "NTILE" })
            {
                var item = Items("SELECT |").Single(i => i.DisplayText == name + "() OVER (...)");

                Assert.Equal(name + "() OVER (PARTITION BY  ORDER BY )", item.InsertText);
                Assert.Equal(name + "(", item.InsertText.Substring(0, item.InsertText.Length - item.CaretBack));
            }
        }

        [Fact]
        public void FunctionsWithoutAnOverClauseHaveNoSuchItem()
        {
            Assert.DoesNotContain(Items("SELECT |"), i => i.DisplayText.StartsWith("UPPER") && i.DisplayText.Contains("OVER"));
        }
    }
}
