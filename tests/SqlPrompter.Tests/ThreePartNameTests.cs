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
    public class ThreePartNameTests
    {
        private static DatabaseSnapshot Current() =>
            new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales" },
                new[] { new DbObject(1, "dbo", "Users", DbObjectKind.Table, false) },
                "v1", null, new[] { "master", "Archive", "sales" });

        private static DatabaseSnapshot Archive() =>
            new DatabaseSnapshot("Archive", "dbo", new[] { "dbo", "old" },
                new[]
                {
                    new DbObject(1, "dbo", "OldOrders", DbObjectKind.Table, false),
                    new DbObject(2, "old", "Legacy", DbObjectKind.Table, false),
                },
                "v1");

        [Theory]
        [InlineData("SELECT * FROM Archive.|", 1, "Archive")]
        [InlineData("SELECT * FROM Archive.dbo.|", 2, "Archive")]
        [InlineData("SELECT * FROM Archive..|", 2, "Archive")]
        [InlineData("SELECT * FROM Archive.dbo.Ol|", 2, "Archive")]
        public void QualifierKeepsTheDatabasePart(string sql, int count, string first)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.ObjectName, context.Kind);
            Assert.Equal(count, context.Qualifier.Count);
            Assert.Equal(first, context.Qualifier[0]);
        }

        [Fact]
        public void EmptySchemaBetweenTheDots()
        {
            Assert.Equal(new[] { "Archive", string.Empty }, Analyze("SELECT * FROM Archive..|").Qualifier);
        }

        [Fact]
        public void NamesWithMoreThanFourPartsAreNotResolved()
        {
            Assert.Equal(ContextKind.Name, Analyze("SELECT * FROM Srv.Archive.dbo.Users.|").Kind);
        }

        [Fact]
        public void DatabasesAreOfferedAmongSchemas()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), Current());

            Assert.Contains(items, i => i.Kind == SuggestionKind.Database && i.DisplayText == "Archive");
        }

        [Fact]
        public void DatabaseNameOpensItsObjects()
        {
            var context = Analyze("SELECT * FROM Archive.|");

            Assert.True(ForeignDatabase.TryResolve(context, Current(), out var database, out var inside));
            Assert.Equal("Archive", database);
            var items = SuggestionBuilder.Build(inside, Archive());

            Assert.Contains(items, i => i.Kind == SuggestionKind.Table && i.InsertText == "dbo.OldOrders");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Schema && i.DisplayText == "old");
            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Database);
        }

        [Fact]
        public void SchemaAfterTheDatabaseListsItsObjects()
        {
            Assert.True(ForeignDatabase.TryResolve(Analyze("SELECT * FROM Archive.old.|"), Current(), out _, out var inside));

            Assert.Equal(new[] { "Legacy" }, SuggestionBuilder.Build(inside, Archive()).Select(i => i.InsertText).ToArray());
        }

        [Fact]
        public void DoubleDotMeansTheDefaultSchema()
        {
            Assert.True(ForeignDatabase.TryResolve(Analyze("SELECT * FROM Archive..|"), Current(), out _, out var inside));

            Assert.Equal(new[] { "OldOrders" }, SuggestionBuilder.Build(inside, Archive()).Select(i => i.InsertText).ToArray());
            Assert.Equal(ReadinessState.Ready, Readiness.Evaluate(inside, Archive()).State);
        }

        [Fact]
        public void ASchemaOfTheCurrentDatabaseWinsOverADatabase()
        {
            // "sales" is both a schema here and a database on the server.
            Assert.False(ForeignDatabase.TryResolve(Analyze("SELECT * FROM sales.|"), Current(), out _, out _));
        }

        [Fact]
        public void UnknownNamesAreNotDatabases()
        {
            Assert.False(ForeignDatabase.TryResolve(Analyze("SELECT * FROM Nope.|"), Current(), out _, out _));
            Assert.False(ForeignDatabase.TryResolve(Analyze("SELECT * FROM Nope.dbo.|"), Current(), out _, out _));
            Assert.False(ForeignDatabase.TryResolve(Analyze("SELECT * FROM |"), Current(), out _, out _));
            Assert.False(ForeignDatabase.TryResolve(Analyze("SELECT * FROM Archive.|"), null, out _, out _));
        }
    }
}
