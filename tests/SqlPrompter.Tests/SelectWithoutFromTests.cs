using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Settings;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class SelectWithoutFromTests
    {
        private static SuggestionOptions Aliased(AliasSettings aliases = null) => new SuggestionOptions(KeywordCase.MatchTyped, aliases ?? new AliasSettings());

        [Theory]
        [InlineData("SELECT |", true)]
        [InlineData("SELECT em|", true)]
        [InlineData("SELECT u.Id, |", true)]
        [InlineData("SELECT | FROM dbo.Users u", false)]
        [InlineData("SELECT * FROM dbo.Users u WHERE |", false)]
        [InlineData("SELECT u.| FROM dbo.Users u", false)]
        [InlineData("UPDATE dbo.Users SET |", false)]
        public void DetectsASelectListWithoutFrom(string sql, bool expected)
        {
            Assert.Equal(expected, Analyze(sql).SelectWithoutFrom);
        }

        [Theory]
        [InlineData("SELECT em|", "SELECT em")]
        [InlineData("SELECT em| , Name", "SELECT em , Name")]
        [InlineData("SELECT em| WHERE 1 = 1", "SELECT em")]
        [InlineData("SELECT em|;SELECT 2", "SELECT em")]
        [InlineData("SELECT em| ORDER BY 1", "SELECT em")]
        [InlineData("SELECT 1 WHERE EXISTS (SELECT em|)", "SELECT 1 WHERE EXISTS (SELECT em")]
        [InlineData("SELECT em| UNION SELECT 2", "SELECT em")]
        public void FromGoesToTheEndOfTheSelectList(string sqlWithCaret, string textBeforeFrom)
        {
            var context = Analyze(sqlWithCaret);

            Assert.True(context.SelectWithoutFrom);
            Assert.Equal(textBeforeFrom, sqlWithCaret.Replace("|", string.Empty).Substring(0, context.FromInsertPosition));
        }

        [Fact]
        public void ColumnsOfAllTablesAreOfferedWithTheirFrom()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT em|"), SampleDatabase(), null, Aliased());

            var email = items.Single(i => i.Kind == SuggestionKind.Column && i.DisplayText == "Email");
            Assert.Equal("u.Email", email.InsertText);
            Assert.Equal("FROM dbo.Users AS u", email.FromClause);
            Assert.Equal("Users", email.Suffix);
        }

        [Fact]
        public void ColumnsWithTheSameNameCanBeToldApartByTable()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT na|"), SampleDatabase(), null, Aliased())
                .Where(i => i.Kind == SuggestionKind.Column).ToList();

            Assert.Equal(
                new[] { "FROM sales.Customers AS c", "FROM dbo.Users AS u", "FROM dbo.ActiveUsers AS au" }.OrderBy(x => x),
                items.Where(i => i.DisplayText == "Name").Select(i => i.FromClause).OrderBy(x => x));
        }

        [Fact]
        public void MatchesStartingWithTheTextComeFirst()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT id|"), SampleDatabase(), null, Aliased())
                .Where(i => i.Kind == SuggestionKind.Column).Select(i => i.DisplayText).ToList();

            // Id (starts with "id") before UserId, CustomerId, Order Id (contain it).
            Assert.Equal("Id", items.First());
            Assert.Contains("UserId", items);
        }

        [Fact]
        public void OptionsDecideAliasAndQualification()
        {
            var plain = new AliasSettings { AddAliases = false, QualifyObjectNames = false, QualifyColumnsWithAliases = false };

            var email = SuggestionBuilder.Build(Analyze("SELECT em|"), SampleDatabase(), null, Aliased(plain)).Single(i => i.DisplayText == "Email");

            Assert.Equal("Email", email.InsertText);
            Assert.Equal("FROM Users", email.FromClause);

            var tableName = SuggestionBuilder.Build(Analyze("SELECT em|"), SampleDatabase(), null,
                Aliased(new AliasSettings { AddAliases = false })).Single(i => i.DisplayText == "Email");
            Assert.Equal("Users.Email", tableName.InsertText);
            Assert.Equal("FROM dbo.Users", tableName.FromClause);
        }

        [Fact]
        public void NothingIsOfferedBeforeAnythingIsTyped()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT |"), SampleDatabase(), null, Aliased());

            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Column);
        }

        [Fact]
        public void OtherContextsAreUnchanged()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT em| FROM dbo.Users u"), SampleDatabase(), null, Aliased());

            Assert.All(items, i => Assert.Null(i.FromClause));
        }

        [Fact]
        public void ReadinessAsksForColumnsOfAllTablesThatLackThem()
        {
            var readiness = Readiness.Evaluate(Analyze("SELECT em|"), SampleDatabase(withColumns: false));

            Assert.Equal(ReadinessState.NeedsColumns, readiness.State);
            Assert.DoesNotContain(readiness.MissingColumns, o => o.IsSystem);
            Assert.Equal(ReadinessState.Ready, Readiness.Evaluate(Analyze("SELECT em|"), SampleDatabase()).State);
        }
    }
}
