using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Formatting;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The case of the suggestions follows the active formatting style (#354).</summary>
    public class SuggestionCaseTests
    {
        private static SuggestionOptions Follow(CaseStyle keyword, CaseStyle function = CaseStyle.Upper, CaseStyle dataType = CaseStyle.Upper, KeywordCase mode = KeywordCase.FollowStyle) =>
            new SuggestionOptions(mode, new AliasSettings(), null, new JoinSettings { NewTableOnLeft = true }, null, null,
                new EditorLayout { Style = new FormatStyle { KeywordCase = keyword, FunctionCase = function, DataTypeCase = dataType } });

        private static DatabaseSnapshot Shop()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Countries", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Cities", DbObjectKind.Table, false),
            };
            var keys = new[] { new DbForeignKey("FK_Cities_Countries", 2, 1, new[] { new DbForeignKeyColumn("IdCountry", "IdCountry") }) };
            return new DatabaseSnapshot("shop", "dbo", new[] { "dbo" }, objects, "v1", null, null, null, keys,
                new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "IdCountry" }, [2] = new[] { "IdCity" } });
        }

        [Fact]
        public void AnUpperCaseStyleGivesUpperCaseKeywordsAndFunctionsEvenWhenTypedInLowerCase()
        {
            var keywords = SuggestionBuilder.Build(Analyze("fro|"), null, null, Follow(CaseStyle.Upper));
            Assert.Contains(keywords, i => i.InsertText == "FROM");
            Assert.Contains(keywords, i => i.InsertText == "DELETE FROM");
            Assert.DoesNotContain(keywords, i => i.Kind == SuggestionKind.Keyword && i.InsertText == i.InsertText.ToLowerInvariant() && !i.IsBlock);

            var functions = SuggestionBuilder.Build(Analyze("SELECT datef|"), null, null, Follow(CaseStyle.Upper));
            Assert.Contains(functions, i => i.InsertText == "DATEFROMPARTS()");
        }

        [Fact]
        public void KeywordsFunctionsAndTypesEachFollowTheirOwnCase()
        {
            var options = Follow(CaseStyle.Upper, CaseStyle.Lower, CaseStyle.Lower);

            Assert.Contains(SuggestionBuilder.Build(Analyze("SELECT datef|"), null, null, options), i => i.InsertText == "datefromparts()");
            Assert.Contains(SuggestionBuilder.Build(Analyze("DECLARE @a varc|"), null, null, options), i => i.InsertText == "varchar");
            Assert.Contains(SuggestionBuilder.Build(Analyze("sel|"), null, null, options), i => i.InsertText == "SELECT");
        }

        [Fact]
        public void ALowerCaseStyleGivesLowerCase()
        {
            var options = Follow(CaseStyle.Lower, CaseStyle.Lower, CaseStyle.Lower);

            var keywords = SuggestionBuilder.Build(Analyze("SEL|"), null, null, options);
            Assert.Contains(keywords, i => i.InsertText == "select");
            Assert.DoesNotContain(keywords, i => i.InsertText == "SELECT");
        }

        [Fact]
        public void AStyleThatKeepsTheCaseFallsBackToTheTypedText()
        {
            var options = Follow(CaseStyle.Keep);

            Assert.Contains(SuggestionBuilder.Build(Analyze("sel|"), null, null, options), i => i.InsertText == "select");
            Assert.Contains(SuggestionBuilder.Build(Analyze("SEL|"), null, null, options), i => i.InsertText == "SELECT");
        }

        [Fact]
        public void MatchTypedStillFollowsTheTypedText()
        {
            var options = Follow(CaseStyle.Upper, mode: KeywordCase.MatchTyped);

            Assert.Contains(SuggestionBuilder.Build(Analyze("sel|"), null, null, options), i => i.InsertText == "select");
            Assert.Contains(SuggestionBuilder.Build(Analyze("SEL|"), null, null, options), i => i.InsertText == "SELECT");
        }

        [Fact]
        public void TheKeywordsOfAJoinFragmentFollowTheStyle()
        {
            var upper = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Countries c JOIN |"), Shop(), null, Follow(CaseStyle.Upper));
            Assert.Contains(upper, i => i.InsertText == "dbo.Cities AS c1 ON c1.IdCountry = c.IdCountry");

            var lower = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Countries c JOIN |"), Shop(), null, Follow(CaseStyle.Lower));
            Assert.Contains(lower, i => i.InsertText == "dbo.Cities as c1 on c1.IdCountry = c.IdCountry");
        }

        [Fact]
        public void NamesOfObjectsAreNeverChangedByTheStyle()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.|"), Shop(), null, Follow(CaseStyle.Lower, CaseStyle.Lower, CaseStyle.Lower));

            Assert.Contains(items, i => i.InsertText == "Cities as c");
            Assert.Contains(items, i => i.InsertText == "Countries as c");
        }

        [Fact]
        public void AWindowFunctionSnippetKeepsItsFieldsWhenCased()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT row_n|"), null, null, Follow(CaseStyle.Lower, CaseStyle.Lower));

            var window = items.First(i => i.Kind == SuggestionKind.Function && i.Tag is SqlPrompter.Core.Snippets.Snippet && i.InsertText.StartsWith("row_number"));
            var body = ((SqlPrompter.Core.Snippets.Snippet)window.Tag).Body;
            Assert.Contains("$partition$", body);
            Assert.Contains("over (partition by", body);
        }

        [Fact]
        public void TheChangedStyleTakesEffectWithoutARestart()
        {
            var settings = new PrompterSettings();
            settings.Formatting.KeywordCase = CaseStyle.Lower;
            Assert.Contains(SuggestionBuilder.Build(Analyze("SEL|"), null, null, settings.ToSuggestionOptions()), i => i.InsertText == "select");

            settings.Formatting.KeywordCase = CaseStyle.Upper;
            Assert.Contains(SuggestionBuilder.Build(Analyze("sel|"), null, null, settings.ToSuggestionOptions()), i => i.InsertText == "SELECT");
        }

        [Fact]
        public void FollowingTheStyleIsTheDefaultAndAnOldValueStillLoads()
        {
            Assert.Equal(KeywordCase.FollowStyle, new PrompterSettings().Behavior.KeywordCase);
            Assert.Equal(KeywordCase.FollowStyle, SettingsJson.Deserialize("{}").Behavior.KeywordCase);
            Assert.Equal(KeywordCase.MatchTyped, SettingsJson.Deserialize("{ \"behavior\": { \"keywordCase\": \"MatchTyped\" } }").Behavior.KeywordCase);
        }
    }
}
