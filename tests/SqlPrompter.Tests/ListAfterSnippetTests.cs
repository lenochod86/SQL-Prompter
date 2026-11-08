using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Snippets;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The list opens after a snippet is inserted where the caret stands in a context with items (#353).</summary>
    public class ListAfterSnippetTests
    {
        private static bool OpensAfter(string before, string body)
        {
            var snippet = Snippet.Create("x", string.Empty, body);
            var expanded = SnippetExpander.Expand(snippet, new SnippetContext { NewLine = "\r\n" });
            var text = before + expanded.Text.Insert(expanded.CursorOffset, "|");
            return ListAfterInsertion.ShouldOpen(Analyze(text));
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Countries AS c ", "INNER JOIN $CURSOR$")]
        [InlineData("SELECT * FROM dbo.Countries AS c ", "LEFT JOIN $CURSOR$")]
        [InlineData("", "SELECT * FROM $CURSOR$")]
        [InlineData("SELECT * FROM dbo.Countries AS c ", "WHERE $CURSOR$")]
        [InlineData("INSERT INTO dbo.Countries (", "$CURSOR$")]
        public void AContextWithItemsOpensTheList(string before, string body) => Assert.True(OpensAfter(before, body), before + body);

        [Theory]
        [InlineData("", "$CURSOR$")]
        [InlineData("SELECT * FROM dbo.Countries AS c ", "$CURSOR$")]
        [InlineData("SELECT * FROM dbo.Countries ", "AS $CURSOR$")]
        [InlineData("-- ", "note $CURSOR$")]
        public void NothingOpensWhereThereAreOnlyKeywordsOrANameOfTheUsersOwn(string before, string body) => Assert.False(OpensAfter(before, body), before + body);

        [Fact]
        public void ASnippetCanOptOutAndOldFilesLoad()
        {
            var snippet = new Snippet { Prefix = "x", Body = "y", NoSuggestions = true };
            Assert.Contains("noSuggestions", SnippetJson(snippet));
            Assert.DoesNotContain("noSuggestions", SnippetJson(new Snippet { Prefix = "x", Body = "y" }));
            Assert.False(new Snippet().NoSuggestions);
        }

        private static string SnippetJson(Snippet snippet)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Snippet)).WriteObject(stream, snippet);
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
