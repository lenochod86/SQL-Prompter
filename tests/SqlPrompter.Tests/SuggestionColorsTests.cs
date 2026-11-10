using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The editor color a suggestion of the list takes (#418).</summary>
    public class SuggestionColorsTests
    {
        [Theory]
        [InlineData(SuggestionKind.Keyword, SnippetHighlighter.Keyword)]
        [InlineData(SuggestionKind.DataType, SnippetHighlighter.Keyword)]
        [InlineData(SuggestionKind.Function, SnippetHighlighter.Function)]
        [InlineData(SuggestionKind.Variable, SnippetHighlighter.Variable)]
        public void KeywordsFunctionsTypesAndVariablesTakeTheirEditorColor(SuggestionKind kind, int expected) =>
            Assert.Equal(expected, SuggestionColors.ClassOf(kind));

        [Theory]
        [InlineData(SuggestionKind.Table)]
        [InlineData(SuggestionKind.Column)]
        [InlineData(SuggestionKind.Alias)]
        [InlineData(SuggestionKind.Schema)]
        [InlineData(SuggestionKind.Snippet)]
        [InlineData(SuggestionKind.ScalarFunction)]
        [InlineData(SuggestionKind.Procedure)]
        public void OtherKindsKeepThePlainTextColor(SuggestionKind kind) => Assert.Null(SuggestionColors.ClassOf(kind));
    }
}
