using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The context after deleting characters equals the context of the same text typed forward (#352): the list is made again from it.</summary>
    public class ShortenedPositionTests
    {
        private static DatabaseSnapshot Shop()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Countries", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Cities", DbObjectKind.Table, false),
            };
            var columns = new Dictionary<int, IReadOnlyList<DbColumn>>
            {
                [2] = new[] { new DbColumn("IdCity", "int", false), new DbColumn("Name", "nvarchar", false) },
            };
            return new DatabaseSnapshot("shop", "dbo", new[] { "dbo" }, objects, "v1", null, null, null, new DbForeignKey[0], new Dictionary<int, IReadOnlyList<string>>()).WithColumns(columns);
        }

        private static IReadOnlyList<Suggestion> At(string sql) => SuggestionBuilder.Build(Analyze(sql), Shop());

        [Fact]
        public void AShortenedNameAfterTheSchemaGivesTheTablesNotTheColumns()
        {
            var items = At("SELECT * FROM dbo.Countries c JOIN dbo.Citi|");

            Assert.Contains(items, i => i.Kind == SuggestionKind.Table && i.DisplayText == "Cities");
            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Column);
        }

        [Fact]
        public void TheSchemaWithoutANameGivesAllObjectsOfTheSchema()
        {
            var items = At("SELECT * FROM dbo.Countries c JOIN dbo.|");

            Assert.Contains(items, i => i.DisplayText == "Cities");
            Assert.Contains(items, i => i.DisplayText == "Countries");
        }

        [Fact]
        public void BeforeTheSchemaTheListIsTheOneOfAJoin()
        {
            var items = At("SELECT * FROM dbo.Countries c JOIN |");

            Assert.Contains(items, i => i.Kind == SuggestionKind.Table && i.DisplayText == "Cities");
        }

        [Fact]
        public void ADottedTableNameIsNotAnObjectNameToComplete()
        {
            var items = At("SELECT * FROM dbo.Countries c JOIN dbo.Cities.|");

            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Table);
        }
    }
}
