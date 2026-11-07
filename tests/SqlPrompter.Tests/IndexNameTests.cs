using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>Index names after ALTER INDEX and DROP INDEX (#200).</summary>
    public class IndexNameTests
    {
        private static DatabaseSnapshot Database()
        {
            var objects = new[] { new DbObject(1, "dbo", "Users", DbObjectKind.Table, false), new DbObject(2, "sales", "Orders", DbObjectKind.Table, false) };
            var indexes = new[]
            {
                new DbIndex("dbo", "Users", "IX_Users_Name", "nonclustered"),
                new DbIndex("sales", "Orders", "PK_Orders", "unique clustered"),
            };
            return new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales" }, objects, "v1", indexes: indexes);
        }

        private static IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), Database());

        [Theory]
        [InlineData("ALTER INDEX |")]
        [InlineData("DROP INDEX |")]
        public void AllIndexesAreOffered(string sql)
        {
            var names = Items(sql).Where(i => i.Kind == SuggestionKind.Index).Select(i => i.DisplayText).ToArray();

            Assert.Equal(new[] { "IX_Users_Name", "PK_Orders" }, names.OrderBy(n => n).ToArray());
        }

        [Fact]
        public void DropIndexIfExistsOffersTheIndexesToo() =>
            Assert.Contains(Items("DROP INDEX IF EXISTS |"), i => i.DisplayText == "IX_Users_Name");

        [Fact]
        public void TheInsertTextNamesTheTable()
        {
            var item = Items("DROP INDEX |").First(i => i.DisplayText == "IX_Users_Name");

            Assert.Equal("IX_Users_Name ON dbo.Users", item.InsertText);
            Assert.Contains("nonclustered index on dbo.Users", item.Description);
        }

        [Fact]
        public void AlterIndexOffersAllButDropDoesNot()
        {
            Assert.Contains(Items("ALTER INDEX |"), i => i.DisplayText == "ALL");
            Assert.DoesNotContain(Items("DROP INDEX |"), i => i.DisplayText == "ALL");
        }

        [Fact]
        public void CreateIndexAndOtherPlacesAreNotAffected()
        {
            Assert.DoesNotContain(Items("CREATE INDEX |"), i => i.Kind == SuggestionKind.Index);
            Assert.DoesNotContain(Items("SELECT * FROM |"), i => i.Kind == SuggestionKind.Index);
        }

        [Fact]
        public void NoIndexesKnownGivesNone() =>
            Assert.DoesNotContain(SuggestionBuilder.Build(Analyze("DROP INDEX |"), SampleDatabase()), i => i.Kind == SuggestionKind.Index);
    }
}
