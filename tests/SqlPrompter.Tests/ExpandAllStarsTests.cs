using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class ExpandAllStarsTests
    {
        private static string Run(string sql, out int expanded, out int skipped, int from = 0, int to = -1) =>
            StarExpanderAll.Expand(sql, from, to < 0 ? sql.Length : to, SampleDatabase(), SuggestionOptions.Default, out expanded, out skipped);

        [Fact]
        public void EveryStarOfTheScriptIsExpanded()
        {
            var result = Run("SELECT * FROM dbo.Users u\r\nGO\r\nSELECT * FROM dbo.Orders", out var expanded, out var skipped);

            Assert.Equal(2, expanded);
            Assert.Equal(0, skipped);
            Assert.Equal("SELECT u.Id,\r\n       u.Name,\r\n       u.Email FROM dbo.Users u\r\nGO\r\nSELECT Id,\r\n       UserId,\r\n       Total FROM dbo.Orders", result);
        }

        [Fact]
        public void CountStarAndMultiplicationStay()
        {
            var sql = "SELECT COUNT(*), 2 * 3 FROM dbo.Users";

            Assert.Equal(sql, Run(sql, out var expanded, out var skipped));
            Assert.Equal(0, skipped);
            Assert.Equal(0, expanded);
        }

        [Fact]
        public void UnknownSourcesStayAndAreCounted()
        {
            var sql = "SELECT * FROM dbo.Nothing";

            Assert.Equal(sql, Run(sql, out var expanded, out var skipped));
            Assert.Equal(0, expanded);
            Assert.Equal(1, skipped);
        }

        [Fact]
        public void OnlyStarsInTheRangeAreExpanded()
        {
            var sql = "SELECT * FROM dbo.Users\nSELECT * FROM dbo.Orders";
            var result = Run(sql, out var expanded, out _, 0, 10);

            Assert.Equal(1, expanded);
            Assert.EndsWith("SELECT * FROM dbo.Orders", result);
        }

        [Fact]
        public void TwoStarsInOneBatchAreBothExpanded()
        {
            var result = Run("SELECT * FROM dbo.Users;\nSELECT o.* FROM dbo.Orders o", out var expanded, out _);

            Assert.Equal(2, expanded);
            Assert.DoesNotContain("*", result);
        }
    }
}
