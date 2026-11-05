using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Snippets;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class SnippetSuggestionTests
    {
        private static readonly Snippet[] Snippets =
        {
            Snippet.Create("addcol", "Add column", "ALTER TABLE t ADD c INT"),
            Snippet.Create("st", "Select top", "SELECT TOP 100 *\nFROM "),
            Snippet.Create("cnt", "", "SELECT COUNT(*)"),
            Snippet.Create("ij", "Inner join", "INNER JOIN "),
        };

        private static SuggestionOptions Options() => SuggestionOptions.Default.WithSnippets(Snippets);

        [Fact]
        public void SnippetsMatchingTheTypedTextAreOffered()
        {
            var items = SuggestionBuilder.Build(Analyze("sel|"), null, null, Options());
            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Snippet);

            var added = SuggestionBuilder.Build(Analyze("add|"), null, null, Options()).Single(i => i.Kind == SuggestionKind.Snippet);

            Assert.Equal("addcol", added.DisplayText);
            Assert.Equal("addcol", added.InsertText);
            Assert.Equal("Add column", added.Detail);
            Assert.Same(Snippets[0], added.Tag);
            Assert.Contains("ALTER TABLE t ADD c INT", added.Description);
        }

        [Fact]
        public void NoSnippetsBeforeACharacterIsTyped()
        {
            Assert.DoesNotContain(SuggestionBuilder.Build(Analyze("|"), null, null, Options()), i => i.Kind == SuggestionKind.Snippet);
        }

        [Fact]
        public void ThePrefixMayBeInTheMiddleOfTheTypedWord()
        {
            Assert.Equal(new[] { "cnt" }, SuggestionBuilder.Build(Analyze("cn|"), null, null, Options()).Where(i => i.Kind == SuggestionKind.Snippet).Select(i => i.DisplayText).ToArray());
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Users AS u ij|")]
        [InlineData("SELECT *\r\nFROM dbo.Users AS u\r\nst|")]
        [InlineData("SELECT * FROM dbo.Users u st|")]
        public void SnippetsAreOfferedAfterTheFromClause(string sql)
        {
            var context = Analyze(sql);
            var items = SuggestionBuilder.Build(context, SampleDatabase(), null, Options());

            Assert.True(items.Any(i => i.Kind == SuggestionKind.Snippet), context.Kind + " " + context.TypedPrefix);
        }

        [Fact]
        public void SnippetsAreOfferedInExpressionsToo()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Users u WHERE st|"), SampleDatabase(), null, Options());

            Assert.Contains(items, i => i.Kind == SuggestionKind.Snippet && i.DisplayText == "st");
        }

        [Fact]
        public void NotAfterADotAndNotWhereNamesAreChosen()
        {
            Assert.DoesNotContain(SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.st|"), SampleDatabase(), null, Options()), i => i.Kind == SuggestionKind.Snippet);
            Assert.DoesNotContain(SuggestionBuilder.Build(Analyze("SELECT u.st| FROM dbo.Users u"), SampleDatabase(), null, Options()), i => i.Kind == SuggestionKind.Snippet);
        }

        [Fact]
        public void KeywordCaseDoesNotTouchSnippets()
        {
            var options = new SuggestionOptions(KeywordCase.Upper, null, null, null, Snippets);

            var item = SuggestionBuilder.Build(Analyze("st|"), null, null, options).Single(i => i.Kind == SuggestionKind.Snippet);

            Assert.Equal("st", item.InsertText);
        }
    }
}
