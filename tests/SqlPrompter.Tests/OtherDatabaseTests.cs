using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Navigation;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>Tables of another database of the same server, written as three-part names: their columns and tooltips (#443).</summary>
    public class OtherDatabaseTests
    {
        private static DatabaseSnapshot Other()
        {
            var objects = new[] { new DbObject(1, "dbo", "Contracts", DbObjectKind.Table, false) };
            return new DatabaseSnapshot("Archive", "dbo", new[] { "dbo" }, objects, "v1").WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
            {
                [1] = new[] { new DbColumn("ContractId", "int", false), new DbColumn("Signed", "datetime", true) },
            });
        }

        private static SuggestionOptions OptionsFor(IList<string> calls) =>
            new SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.MatchTyped, null, source =>
            {
                calls.Add(string.Join(".", source.NameParts));
                return Readiness.IsOtherDatabase(source, SampleDatabase()) ? Other().GetColumns(Readiness.Resolve(source, Other())) : null;
            });

        [Fact]
        public void TheColumnsOfAnAliasOfATableOfAnotherDatabaseAreOffered()
        {
            var calls = new List<string>();

            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Users u JOIN Archive.dbo.Contracts c ON c.ContractId = u.Id WHERE c.|"), SampleDatabase(), null, OptionsFor(calls));

            Assert.Equal(new[] { "ContractId", "Signed" }, items.Where(i => i.Kind == SuggestionKind.Column).Select(i => i.DisplayText).OrderBy(x => x).ToArray());
            Assert.Contains("Archive.dbo.Contracts", calls);
        }

        [Fact]
        public void TheColumnsOfThreePartNameOfTheOwnDatabaseComeFromItsMetadata()
        {
            var calls = new List<string>();

            var items = SuggestionBuilder.Build(Analyze("SELECT u.| FROM test.dbo.Users u"), SampleDatabase(), null, OptionsFor(calls));

            Assert.Contains(items, i => i.DisplayText == "Email");
            Assert.Empty(calls);
        }

        [Fact]
        public void NothingIsOfferedWhileTheOtherDatabaseLoads()
        {
            var options = new SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.MatchTyped, null, s => null);

            var items = SuggestionBuilder.Build(Analyze("SELECT c.| FROM Archive.dbo.Contracts c"), SampleDatabase(), null, options);

            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Column);
        }

        [Theory]
        [InlineData("Archive", true)]
        [InlineData("archive", true)]
        [InlineData("test", false)]
        [InlineData("TEST", false)]
        public void OnlyADifferentDatabaseIsAnotherDatabase(string database, bool expected)
        {
            var source = new SqlPrompter.Core.Analysis.TableSource(SqlPrompter.Core.Analysis.TableSourceKind.Object, new[] { database, "dbo", "T" }, "t", 0);

            Assert.Equal(expected, Readiness.IsOtherDatabase(source, SampleDatabase()));
        }

        private static HoverText Hover(string marked)
        {
            var caret = marked.IndexOf('|');
            return HoverInfo.At(marked.Remove(caret, 1), caret, SampleDatabase(), db => string.Equals(db, "Archive", System.StringComparison.OrdinalIgnoreCase) ? Other() : null);
        }

        [Fact]
        public void ATableOfAnotherDatabaseIsDescribedFromThatDatabase()
        {
            var info = Hover("SELECT * FROM Archive.dbo.Contr|acts");

            Assert.Equal("Table dbo.Contracts\n2 columns", info.Text);
            Assert.Equal("Archive", info.Database);
        }

        [Fact]
        public void ATableOfTheOtherDatabaseIsNotConfusedWithTheSameNameInTheOwnOne()
        {
            Assert.Null(Hover("SELECT * FROM Archive.dbo.Us|ers"));
        }

        [Fact]
        public void AColumnOfAnAliasOfATableOfAnotherDatabaseShowsItsType()
        {
            var info = Hover("SELECT c.Sig|ned FROM Archive.dbo.Contracts c");

            Assert.StartsWith("Column c.Signed: datetime NULL", info.Text);
            Assert.Equal("Archive", info.Database);
        }

        [Fact]
        public void NothingIsShownWhileTheOtherDatabaseLoads()
        {
            var caret = "SELECT * FROM Other.dbo.Con|".IndexOf('|');

            Assert.Null(HoverInfo.At("SELECT * FROM Other.dbo.Con", caret, SampleDatabase(), db => null));
        }
    }
}
