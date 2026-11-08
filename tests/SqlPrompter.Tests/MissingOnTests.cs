using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Snippets;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>A join condition without ON (#358): the keyword was deleted or is not typed yet, the condition is still recognized.</summary>
    public class MissingOnTests
    {
        private const string Head = "SELECT * FROM dbo.Countries AS c JOIN dbo.Cities AS c1 ON c.IdCountry = c1.IdCountry JOIN ";

        private static DatabaseSnapshot Db() =>
            new DatabaseSnapshot("t", "dbo", new[] { "dbo" },
                new[] { new DbObject(1, "dbo", "Countries", DbObjectKind.Table, false), new DbObject(2, "dbo", "Cities", DbObjectKind.Table, false) },
                "v1", null, new[] { "master" }, new[] { "Srv" })
                .WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>> { [1] = Columns("IdCountry", "Name"), [2] = Columns("IdCity", "IdCountry", "Name") });

        private static SuggestionOptions Options() =>
            new SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.MatchTyped, null, s => s.NameParts.Count == 4 ? Columns("IdCountry", "Region") : null, null,
                new[] { Snippet.Create("ij", "inner join", "INNER JOIN") });

        private static IReadOnlyList<Suggestion> At(string sql) => SuggestionBuilder.Build(Analyze(sql), Db(), null, Options());

        [Theory]
        [InlineData(Head + "dbo.Countries AS c2 ON c.IdCountry = |")]
        [InlineData(Head + "dbo.Countries AS c2 c.IdCountry = |")]
        [InlineData(Head + "dbo.Countries c2 c.IdCountry = |")]
        [InlineData(Head + "Srv.Shop.dbo.Countries AS c2 c.IdCountry = |")]
        public void TheConditionIsAnExpressionWithOrWithoutOn(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.Expression, context.Kind);
            Assert.True(context.AfterComparison);
            var items = At(sql);
            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.DisplayText == "IdCountry");
            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Snippet);
        }

        [Fact]
        public void TheColumnsOfTheLinkedTableComeWithTheOthers()
        {
            var items = At(Head + "Srv.Shop.dbo.Countries AS c2 c.IdCountry = |");

            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.DisplayText == "Region");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.DisplayText == "IdCity");
        }

        [Fact]
        public void NoSnippetsAfterAComparison()
        {
            Assert.DoesNotContain(At("SELECT * FROM dbo.Cities c WHERE c.IdCountry = |"), i => i.Kind == SuggestionKind.Snippet);
            Assert.Contains(At("SELECT * FROM dbo.Cities c |"), i => i.Kind == SuggestionKind.Snippet || i.Kind == SuggestionKind.Keyword);
        }

        [Fact]
        public void RightAfterTheAliasOnComesFirst()
        {
            var items = At(Head + "dbo.Countries AS c2 |").Where(i => i.Kind == SuggestionKind.Keyword).OrderBy(i => i.SortText).ToList();

            Assert.Equal("ON", items[0].DisplayText);
        }

        [Fact]
        public void ADottedNameAfterTheTypedOnIsNotChanged()
        {
            Assert.Equal(ContextKind.Expression, Analyze("SELECT * FROM dbo.Cities AS x JOIN dbo.Countries AS c ON c.|").Kind);
            Assert.Equal(ContextKind.Expression, Analyze("SELECT * FROM dbo.Cities AS x JOIN dbo.Countries AS c WHERE c.|").Kind);
        }

        [Fact]
        public void AliasesAndOtherJoinsAreNotTakenForAMissingOn()
        {
            // An alias without AS, and a following JOIN, are no conditions.
            Assert.Equal(ContextKind.ObjectName, Analyze("SELECT * FROM dbo.Cities AS x JOIN |").Kind);
            Assert.NotEqual(ContextKind.Expression, Analyze("SELECT * FROM dbo.Cities AS x JOIN dbo.Countries c |").Kind);
        }
    }
}
