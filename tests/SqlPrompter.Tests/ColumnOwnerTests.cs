using System.Linq;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>A column item knows its table or view, so that its description can be read from the database (#206).</summary>
    public class ColumnOwnerTests
    {
        private static Suggestion Column(string sql, string name) =>
            SuggestionBuilder.Build(Analyze(sql), SampleDatabase()).First(i => i.Kind == SuggestionKind.Column && i.DisplayText == name);

        [Fact]
        public void AColumnOfASourceCarriesItsTable()
        {
            var item = Column("SELECT | FROM dbo.Users u", "Name");

            Assert.Equal("dbo", item.ColumnOwnerSchema);
            Assert.Equal("Users", item.ColumnOwnerName);
        }

        [Fact]
        public void AQualifiedColumnCarriesItsTable()
        {
            var item = Column("SELECT o.| FROM dbo.Orders o", "Total");

            Assert.Equal("Orders", item.ColumnOwnerName);
        }

        [Fact]
        public void TheOwnerIsTheTableOfEachSourceInAJoin()
        {
            Assert.Equal("Users", SuggestionBuilder.Build(Analyze("SELECT | FROM dbo.Users u JOIN dbo.Orders o ON 1 = 1"), SampleDatabase())
                .First(i => i.Kind == SuggestionKind.Column && i.DisplayText == "Email").ColumnOwnerName);
            Assert.Equal("Orders", SuggestionBuilder.Build(Analyze("SELECT | FROM dbo.Users u JOIN dbo.Orders o ON 1 = 1"), SampleDatabase())
                .First(i => i.Kind == SuggestionKind.Column && i.DisplayText == "Total").ColumnOwnerName);
        }

        [Fact]
        public void ItemsThatAreNotColumnsOfATableHaveNoOwner()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT | FROM dbo.Users u"), SampleDatabase());

            Assert.All(items.Where(i => i.Kind != SuggestionKind.Column), i => Assert.Null(i.ColumnOwnerName));
        }
    }
}
