using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class StarExpanderTests
    {
        private static string Expand(string sqlWithCaret, EditorLayout editor = null)
        {
            var caret = sqlWithCaret.IndexOf('|');
            var sql = sqlWithCaret.Remove(caret, 1);
            var options = SuggestionOptions.Default.WithEditor(editor ?? new EditorLayout { NewLine = "\n" });
            var expansion = StarExpander.TryExpand(sql, caret, SampleDatabase(), ScriptModel.Build(sql, caret), options);
            return expansion == null ? null : sql.Remove(expansion.Start, expansion.Length).Insert(expansion.Start, expansion.Text);
        }

        [Fact]
        public void StarBecomesTheColumnsWithTheAliasOneOnEachLine()
        {
            Assert.Equal(
                "SELECT u.Id,\n       u.Name,\n       u.Email FROM dbo.Users u",
                Expand("SELECT *| FROM dbo.Users u", new EditorLayout { NewLine = "\n", LinePrefix = "SELECT " }));
        }

        [Theory]
        [InlineData(true, true, "SELECT\tu.Id\n\t  , u.Name\n\t  , u.Email FROM dbo.Users u")]
        [InlineData(true, false, "SELECT  u.Id\n      , u.Name\n      , u.Email FROM dbo.Users u")]
        [InlineData(false, true, "SELECT\tu.Id,\n\t\tu.Name,\n\t\tu.Email FROM dbo.Users u")]
        [InlineData(false, false, "SELECT  u.Id,\n        u.Name,\n        u.Email FROM dbo.Users u")]
        public void ExpansionFollowsTheFormattingStyle(bool commaBefore, bool tabs, string expected)
        {
            var style = new SqlPrompter.Core.Formatting.FormatStyle { CommaBeforeItems = commaBefore, UseTabs = tabs, TabWidth = 4 };
            var layout = new EditorLayout { NewLine = "\n", LinePrefix = tabs ? "SELECT\t" : "SELECT  ", Style = style };

            Assert.Equal(expected, Expand(layout.LinePrefix + "*| FROM dbo.Users u", layout));
        }
        [Fact]
        public void TableWithoutAliasGetsNoAliasInASingleTableQuery()
        {
            Assert.Equal("SELECT Id,\nName,\nEmail FROM dbo.Users", Expand("SELECT *| FROM dbo.Users"));
        }

        [Fact]
        public void SeveralTablesAreExpandedInSourceOrder()
        {
            var result = Expand("SELECT *| FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id");

            Assert.Equal("SELECT u.Id,\nu.Name,\nu.Email,\no.Id,\no.UserId,\no.Total FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id", result);
        }

        [Fact]
        public void TablesWithoutAliasAreQualifiedWithTheirNameWhenThereAreSeveral()
        {
            var result = Expand("SELECT *| FROM dbo.Users JOIN dbo.ActiveUsers a ON a.Id = Users.Id");

            Assert.StartsWith("SELECT Users.Id,\nUsers.Name,\nUsers.Email,\na.Id,\na.Name FROM", result);
        }

        [Fact]
        public void QualifiedStarExpandsOnlyThatTable()
        {
            var result = Expand("SELECT o.*| FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id");

            Assert.Equal("SELECT o.Id,\no.UserId,\no.Total FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id", result);
        }

        [Fact]
        public void StarAfterAnotherItemKeepsTheAlignment()
        {
            var result = Expand("SELECT u.Name, *| FROM dbo.Orders o", new EditorLayout { NewLine = "\n", LinePrefix = "SELECT u.Name, " });

            Assert.Equal("SELECT u.Name, o.Id,\n               o.UserId,\n               o.Total FROM dbo.Orders o", result);
        }

        [Fact]
        public void TopAndDistinctDoNotStopTheExpansion()
        {
            Assert.StartsWith("SELECT TOP 5 Id,", Expand("SELECT TOP 5 *| FROM dbo.Users"));
            Assert.StartsWith("SELECT DISTINCT Id,", Expand("SELECT DISTINCT *| FROM dbo.Users"));
            Assert.StartsWith("SELECT TOP (5) Id,", Expand("SELECT TOP (5) *| FROM dbo.Users"));
        }

        [Fact]
        public void TabIsLeftAloneWhereTheStarIsNotASelectListStar()
        {
            Assert.Null(Expand("SELECT COUNT(*|) FROM dbo.Users"));
            Assert.Null(Expand("SELECT Id *| 2 FROM dbo.Users"));
            Assert.Null(Expand("SELECT 2 *| FROM dbo.Users"));
            Assert.Null(Expand("SELECT * FROM dbo.Users|"));
            Assert.Null(Expand("SELECT *| FROM dbo.Unknown"));
        }

        [Fact]
        public void ColumnsOfScriptTablesAreUsed()
        {
            var result = Expand("WITH c AS (SELECT Id, Name FROM dbo.Users) SELECT *| FROM c");

            Assert.Equal("WITH c AS (SELECT Id, Name FROM dbo.Users) SELECT Id,\nName FROM c", result);
        }
    }
}
