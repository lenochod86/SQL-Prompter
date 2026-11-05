using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    public class SnippetHighlighterTests
    {
        private static string Kinds(string text) => string.Concat(SnippetHighlighter.Classify(text));

        [Fact]
        public void KeywordsStringsCommentsAndVariablesAreTold()
        {
            Assert.Equal("111111022203333", Kinds("SELECT 'a' -- c"));
            Assert.Equal("111111044", Kinds("SELECT @v"));
        }
        [Fact]
        public void FieldsWinOverSql()
        {
            var kinds = SnippetHighlighter.Classify("SELECT $CURSOR$, $DATE(yyyy-MM-dd)$ FROM $table$");

            Assert.Equal(SnippetHighlighter.Keyword, kinds[0]);
            Assert.Equal(SnippetHighlighter.Field, kinds[7]);
            Assert.Equal(SnippetHighlighter.Field, kinds[14]);
            Assert.Equal(SnippetHighlighter.Plain, kinds[15]);
            Assert.Equal(SnippetHighlighter.Field, kinds[17]);
            Assert.Equal(SnippetHighlighter.Keyword, kinds["SELECT $CURSOR$, $DATE(yyyy-MM-dd)$ FROM $table$".IndexOf("FROM")]);
        }

        [Fact]
        public void EmptyAndNullAreFine()
        {
            Assert.Empty(SnippetHighlighter.Classify(null));
            Assert.Empty(SnippetHighlighter.Classify(string.Empty));
        }
    }
}
