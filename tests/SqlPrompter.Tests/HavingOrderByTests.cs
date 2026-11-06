using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>HAVING offers the aggregates of the select list; ORDER BY the items of the select list, narrowed by GROUP BY and DISTINCT (#214).</summary>
    public class HavingOrderByTests
    {
        private static IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), SampleDatabase());

        private static string[] Leading(string sql) => Items(sql).Where(i => i.Group < -50).OrderBy(i => i.Group).Select(i => i.DisplayText).ToArray();

        [Theory]
        [InlineData("SELECT UserId, COUNT(*) AS Cnt, SUM(Total) FROM dbo.Orders GROUP BY UserId HAVING |", "COUNT(*)", "SUM(Total)")]
        [InlineData("SELECT UserId, COUNT(*) FROM dbo.Orders GROUP BY UserId HAVING COUNT(*) > 1 AND |", "COUNT(*)")]
        [InlineData("SELECT UserId, COUNT(*) FROM dbo.Orders GROUP BY UserId HAVING COUNT(*) > 1 OR |", "COUNT(*)")]
        public void HavingOffersTheAggregatesOfTheSelectListFirst(string sql, params string[] expected)
        {
            Assert.Equal(expected, Leading(sql));
        }

        [Fact]
        public void AForcedKeywordCaseDoesNotChangeTheAggregateAsWritten()
        {
            var sql = "SELECT UserId, SUM(Total) FROM dbo.Orders GROUP BY UserId HAVING |";
            var caret = sql.IndexOf('|');
            var options = new SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.Upper, new SqlPrompter.Core.Settings.AliasSettings());

            var items = SuggestionBuilder.Build(SqlPrompter.Core.Analysis.ContextAnalyzer.Analyze(sql.Remove(caret, 1), caret), SampleDatabase(), null, options);

            Assert.Contains(items, i => i.InsertText == "SUM(Total)");
        }

        [Fact]
        public void HavingDoesNotOfferTheAliasOrThePlainColumns()
        {
            var leading = Leading("SELECT UserId, COUNT(*) AS Cnt FROM dbo.Orders GROUP BY UserId HAVING |");

            Assert.DoesNotContain("Cnt", leading);
            Assert.DoesNotContain("UserId", leading);
        }

        [Fact]
        public void HavingAfterAnOperandIsUnchanged()
        {
            Assert.Empty(Leading("SELECT UserId, COUNT(*) FROM dbo.Orders GROUP BY UserId HAVING COUNT(*) |"));
        }

        [Fact]
        public void OrderByOffersTheAliasesAndExpressionsOfTheSelectList()
        {
            var leading = Leading("SELECT Id AS Key1, Total * 2 AS Double, Total FROM dbo.Orders ORDER BY |");

            Assert.Equal(new[] { "Key1", "Double", "Total" }, leading);
        }

        [Fact]
        public void AnAliasWithoutAsAndAnAliasWithEqualsCount()
        {
            var leading = Leading("SELECT Id Key1, Mine = Total FROM dbo.Orders ORDER BY |");

            Assert.Equal(new[] { "Key1", "Mine" }, leading);
        }

        [Fact]
        public void OrderByAfterACommaOffersThemToo()
        {
            Assert.Equal(new[] { "UserId", "Total" }, Leading("SELECT UserId, Total FROM dbo.Orders ORDER BY UserId, |"));
        }

        [Fact]
        public void TheStarIsNoItem()
        {
            Assert.Empty(Leading("SELECT * FROM dbo.Orders ORDER BY |"));
        }

        [Fact]
        public void WithGroupByOnlyTheGroupedColumnsRemain()
        {
            var columns = Items("SELECT UserId, COUNT(*) AS Cnt FROM dbo.Orders GROUP BY UserId ORDER BY |").Where(i => i.Kind == SuggestionKind.Column && i.Group >= -50).Select(i => i.DisplayText).ToArray();

            Assert.Equal(new[] { "UserId" }, columns);
            Assert.Contains("Cnt", Leading("SELECT UserId, COUNT(*) AS Cnt FROM dbo.Orders GROUP BY UserId ORDER BY |"));
        }

        [Fact]
        public void WithDistinctOnlyTheColumnsOfTheSelectListRemain()
        {
            var columns = Items("SELECT DISTINCT UserId FROM dbo.Orders ORDER BY |").Where(i => i.Kind == SuggestionKind.Column && i.Group >= -50).Select(i => i.DisplayText).ToArray();

            Assert.Equal(new[] { "UserId" }, columns);
        }

        [Fact]
        public void WithoutGroupByOrDistinctAllColumnsStay()
        {
            var columns = Items("SELECT UserId FROM dbo.Orders ORDER BY |").Where(i => i.Kind == SuggestionKind.Column && i.Group >= -50).Select(i => i.DisplayText).ToArray();

            Assert.Equal(new[] { "Id", "UserId", "Total" }, columns);
        }

        [Fact]
        public void TheGroupByRestrictionDoesNotReachOtherClauses()
        {
            var columns = Items("SELECT UserId FROM dbo.Orders GROUP BY UserId HAVING |").Where(i => i.Kind == SuggestionKind.Column).Select(i => i.DisplayText).ToArray();

            Assert.Equal(new[] { "Id", "UserId", "Total" }, columns);
        }

        [Fact]
        public void TheGroupedColumnQualifiedByItsAliasIsRecognized()
        {
            var columns = Items("SELECT o.UserId, COUNT(*) FROM dbo.Orders o GROUP BY o.UserId ORDER BY |").Where(i => i.Kind == SuggestionKind.Column && i.Group >= -50).Select(i => i.DisplayText).ToArray();

            Assert.Equal(new[] { "UserId" }, columns);
        }
    }
}
