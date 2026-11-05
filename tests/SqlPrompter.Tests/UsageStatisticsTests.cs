using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Settings;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class UsageStatisticsTests
    {
        [Fact]
        public void RecordingCountsPerKey()
        {
            var stats = new UsageStatistics();
            stats.Record("a");
            stats.Record("a");
            stats.Record("b");

            Assert.Equal(2, stats.CountOf("a"));
            Assert.Equal(1, stats.CountOf("b"));
            Assert.Equal(0, stats.CountOf("c"));
            Assert.Equal(0, stats.CountOf(null));
            Assert.Equal(2, stats.EntryCount);
        }

        [Fact]
        public void ClearForgetsEverything()
        {
            var stats = new UsageStatistics();
            stats.Record("a");
            var version = stats.Version;

            stats.Clear();

            Assert.Equal(0, stats.CountOf("a"));
            Assert.NotEqual(version, stats.Version);
        }

        [Fact]
        public void TheStatisticsSurviveAJsonRoundTrip()
        {
            var stats = new UsageStatistics();
            stats.Record("db|Table|Orders|dbo");
            stats.Record("db|Table|Orders|dbo");
            stats.Record("db|Keyword|WHERE|");

            var again = UsageStatistics.FromJson(stats.ToJson());

            Assert.Equal(2, again.CountOf("db|Table|Orders|dbo"));
            Assert.Equal(1, again.CountOf("db|Keyword|WHERE|"));
        }

        [Fact]
        public void MergingAddsTheCounts()
        {
            var one = new UsageStatistics();
            one.Record("a");
            var two = new UsageStatistics();
            two.Record("a");
            two.Record("b");

            one.MergeFrom(two);

            Assert.Equal(2, one.CountOf("a"));
            Assert.Equal(1, one.CountOf("b"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"key\": 1}")]
        public void BrokenTextGivesEmptyStatistics(string json) => Assert.Equal(0, UsageStatistics.FromJson(json).EntryCount);

        [Fact]
        public void TheLeastUsedAreForgottenPastTheLimit()
        {
            var stats = new UsageStatistics();
            stats.Record("keep");
            stats.Record("keep");
            for (var i = 0; i < UsageStatistics.MaxEntries + 10; i++)
            {
                stats.Record("k" + i);
            }

            Assert.True(stats.EntryCount <= UsageStatistics.MaxEntries);
            Assert.Equal(2, stats.CountOf("keep"));
        }

        [Fact]
        public void MoreUsedItemsSortFirstWithinTheirGroup()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), SampleDatabase())
                .Where(i => i.Kind == SuggestionKind.Table && i.Group == 0)
                .ToList();
            var orders = items.Single(i => i.DisplayText == "Orders");
            var users = items.Single(i => i.DisplayText == "Users");

            Assert.True(string.CompareOrdinal(users.SortText, orders.SortText) > 0);
            Assert.True(string.CompareOrdinal(orders.RankedSortText(1), users.RankedSortText(0)) < 0);
            Assert.True(string.CompareOrdinal(orders.RankedSortText(5), users.RankedSortText(1)) < 0);
            Assert.True(string.CompareOrdinal(orders.RankedSortText(0), users.RankedSortText(0)) < 0);
        }

        [Fact]
        public void TheGroupStillComesBeforeTheUsage()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT | FROM dbo.Users u"), SampleDatabase());
            var table = items.First(i => i.Kind == SuggestionKind.Column);
            var keyword = items.First(i => i.Kind == SuggestionKind.Keyword);

            Assert.True(string.CompareOrdinal(table.RankedSortText(0), keyword.RankedSortText(500)) < 0);
        }

        [Fact]
        public void TheKeyNamesKindTextAndOwner()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), SampleDatabase());

            Assert.Equal("Table|Orders|dbo", items.Single(i => i.DisplayText == "Orders" && i.Kind == SuggestionKind.Table).UsageKey);
        }

        [Fact]
        public void RankingIsOnByDefault() => Assert.True(new BehaviorSettings().RankByUsage);
    }
}
