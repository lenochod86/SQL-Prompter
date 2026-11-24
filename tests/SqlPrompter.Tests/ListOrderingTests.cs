using System.Linq;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class ListOrderingTests
    {
        private static string[] Names(params string[] items) => items;

        [Fact]
        public void SortIsOrdinalByTheSortTextAndStable()
        {
            var items = new[] { ("b", "2"), ("a", "1"), ("c", "2"), ("d", "1"), ("Z", "3") };

            var sorted = ListOrdering.Sort(items, i => i.Item2).Select(i => i.Item1).ToArray();

            Assert.Equal(new[] { "a", "d", "b", "c", "Z" }, sorted);
        }

        [Fact]
        public void SortPutsUppercaseBeforeLowercaseLikeTheEditorDoes()
        {
            Assert.Equal(new[] { "B", "a" }, ListOrdering.Sort(new[] { "a", "B" }, s => s).ToArray());
        }

        [Fact]
        public void SortOfAListFollowsTheGroupsOfTheSuggestions()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Users u WHERE u.Id = 1 |"), SampleDatabase());

            var sorted = ListOrdering.Sort(items, i => i.SortText);

            Assert.True(sorted.Select(i => i.Group).SequenceEqual(sorted.Select(i => i.Group).OrderBy(g => g)));
            Assert.Equal(sorted.First().Group, items.Min(i => i.Group));
        }

        [Fact]
        public void UsageMovesAnItemUpWithinItsGroup()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), SampleDatabase()).Where(i => i.Group == 0).Take(2).ToList();

            var sorted = ListOrdering.Sort(items, i => i == items[1] ? i.RankedSortText(5) : i.RankedSortText(0));

            Assert.Same(items[1], sorted[0]);
        }

        [Fact]
        public void FilterKeepsAllForNoTypedText()
        {
            Assert.Equal(Names("a", "b"), ListOrdering.Filter(Names("a", "b"), string.Empty, s => s).ToArray());
            Assert.Equal(Names("a", "b"), ListOrdering.Filter(Names("a", "b"), null, s => s).ToArray());
        }

        [Fact]
        public void FilterWantsTheTypedTextAnywhereAndPutsPrefixMatchesFirst()
        {
            var result = ListOrdering.Filter(Names("OrderId", "Name", "CustomerName", "NameFirst", "Id"), "name", s => s);

            Assert.Equal(Names("Name", "NameFirst", "CustomerName"), result.ToArray());
        }

        [Fact]
        public void FilterIsCaseInsensitiveAndKeepsTheOrderInsideBothParts()
        {
            var result = ListOrdering.Filter(Names("xAb", "Abc", "zab", "aBd"), "AB", s => s);

            Assert.Equal(Names("Abc", "aBd", "xAb", "zab"), result.ToArray());
        }

        [Fact]
        public void FilterOfNothingMatchingIsEmpty()
        {
            Assert.Empty(ListOrdering.Filter(Names("a", "b"), "z", s => s));
        }

        [Fact]
        public void MatchesIsTheSamePredicate()
        {
            Assert.True(ListOrdering.Matches("CustomerName", "name"));
            Assert.True(ListOrdering.Matches("CustomerName", string.Empty));
            Assert.False(ListOrdering.Matches("CustomerName", "zz"));
            Assert.False(ListOrdering.Matches(null, "a"));
        }

        [Fact]
        public void OneTypedCharacterKeepsOnlyTheMatchesAndTheBestOnesFirst()
        {
            // The objects of the sales schema of the test database, in the order of their sort text (#521).
            var names = new[] { "Orders", "Customers", "OrderItems", "vOrderTotals", "Payments", "ProductCategories", "Products", "ShipmentLines" };

            var matches = ListOrdering.Filter(names, "P", s => s, loose: true);

            Assert.DoesNotContain("Orders", matches);
            Assert.DoesNotContain("Customers", matches);
            Assert.DoesNotContain("OrderItems", matches);
            Assert.Equal(new[] { "Payments", "ProductCategories", "Products", "ShipmentLines" }, matches.ToArray());
        }

        [Fact]
        public void OneTypedCharacterPutsThePrefixMatchBeforeTheOthers()
        {
            var names = new[] { "all_columns", "check_constraints", "columns", "objects" };

            var matches = ListOrdering.Filter(names, "co", s => s, loose: true);

            Assert.Equal("columns", matches[0]);
        }
    }
}
