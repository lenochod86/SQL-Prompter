using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Sql;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class SynonymTests
    {
        private static DatabaseSnapshot WithSynonyms()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "U", DbObjectKind.Synonym, false, SqlIdentifier.SplitName("[dbo].[Users]")),
                new DbObject(3, "dbo", "U2", DbObjectKind.Synonym, false, SqlIdentifier.SplitName("[dbo].[U]")),
                new DbObject(4, "dbo", "Remote", DbObjectKind.Synonym, false, SqlIdentifier.SplitName("[OtherServer].[Db].[dbo].[T]")),
                new DbObject(5, "dbo", "Dangling", DbObjectKind.Synonym, false, SqlIdentifier.SplitName("[dbo].[Missing]")),
            };
            return new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, objects, "v1")
                .WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>> { [1] = Columns("Id", "Name") });
        }

        [Fact]
        public void NameIsSplitIntoParts()
        {
            Assert.Equal(new[] { "dbo", "Order Details" }, SqlIdentifier.SplitName("[dbo].[Order Details]"));
            Assert.Equal(new[] { "A", "B", "C" }, SqlIdentifier.SplitName("A.B.C"));
        }

        [Fact]
        public void SynonymsAreOffered()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), WithSynonyms());

            var synonym = items.Single(i => i.DisplayText == "U");
            Assert.Equal(SuggestionKind.Table, synonym.Kind);
            Assert.Contains("synonym", synonym.Description);
            Assert.Contains("dbo.Users", synonym.Description);
        }

        [Fact]
        public void ColumnsComeFromTheTargetAlsoThroughAChain()
        {
            var db = WithSynonyms();

            foreach (var name in new[] { "U", "U2" })
            {
                var items = SuggestionBuilder.Build(Analyze("SELECT | FROM dbo." + name + " s"), db);
                Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.DisplayText == "Name");
                Assert.Equal(ReadinessState.Ready, Readiness.Evaluate(Analyze("SELECT | FROM dbo." + name + " s"), db).State);
            }
        }

        [Theory]
        [InlineData("Remote")]
        [InlineData("Dangling")]
        public void SynonymsWithoutAKnownTargetOfferNoColumns(string name)
        {
            var db = WithSynonyms();
            var context = Analyze("SELECT | FROM dbo." + name + " s");

            Assert.DoesNotContain(SuggestionBuilder.Build(context, db), i => i.Kind == SuggestionKind.Column);
            Assert.Equal(ReadinessState.Unsupported, Readiness.Evaluate(context, db).State);
        }
    }
}
