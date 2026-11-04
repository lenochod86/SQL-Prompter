using System.Linq;
using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    public class SnippetTextTests
    {
        [Fact]
        public void PlaceholdersRoundTripThroughText()
        {
            var placeholders = new[]
            {
                new SnippetPlaceholder { Name = "table", DefaultValue = "dbo.T" },
                new SnippetPlaceholder { Name = "col", DefaultValue = string.Empty },
                new SnippetPlaceholder { Name = "expr", DefaultValue = "a = b" },
            };

            var parsed = SnippetText.ParsePlaceholders(SnippetText.PlaceholdersToText(placeholders));

            Assert.Equal(new[] { "table", "col", "expr" }, parsed.Select(p => p.Name).ToArray());
            Assert.Equal(new[] { "dbo.T", string.Empty, "a = b" }, parsed.Select(p => p.DefaultValue).ToArray());
        }

        [Fact]
        public void ParsingIgnoresBlankAndRepeatedNames()
        {
            var parsed = SnippetText.ParsePlaceholders("a=1\r\n\r\n =x\r\nA=2\r\nb");

            Assert.Equal(new[] { "a", "b" }, parsed.Select(p => p.Name).ToArray());
            Assert.Equal("1", parsed[0].DefaultValue);
            Assert.Equal(string.Empty, parsed[1].DefaultValue);
        }

        [Theory]
        [InlineData("ISNULL($SELECTEDTEXT$, 0)", true)]
        [InlineData("x $selectionstart$y$selectionend$", true)]
        [InlineData("SELECT $CURSOR$", false)]
        [InlineData("$table$", false)]
        public void SelectionSnippetsAreRecognized(string body, bool expected)
        {
            Assert.Equal(expected, SnippetText.UsesSelection(Snippet.Create("x", "", body)));
        }

        [Fact]
        public void ASelectionBecomesABodyThatExpandsToTheSameText()
        {
            const string selected = "SELECT '$5' AS $x, 1";
            var snippet = Snippet.Create("keep", "", SnippetText.BodyFromSelection(selected));

            var expanded = SnippetExpander.Expand(snippet, new SnippetContext());

            Assert.Equal(selected, expanded.Text);
            Assert.Empty(expanded.Fields);
        }

        [Fact]
        public void FieldNamesAreListedInOrder()
        {
            Assert.Equal(new[] { "b", "a" }, SnippetText.FieldNames("$b$ $CURSOR$ $a$ $b$ $USER$ $$x$$").ToArray());
        }

        [Fact]
        public void ASnippetWrapsTheSelection()
        {
            var snippet = Snippet.Create("isn", "", "ISNULL($SELECTEDTEXT$, $default$)$CURSOR$", new[] { new SnippetPlaceholder { Name = "default", DefaultValue = "0" } });

            var expanded = SnippetExpander.Expand(snippet, new SnippetContext { SelectedText = "t.Amount" });

            Assert.Equal("ISNULL(t.Amount, 0)", expanded.Text);
            Assert.Equal("default", Assert.Single(expanded.Fields).Name);
        }
    }
}
