using System.Linq;
using SqlPrompter.Core.Editing;
using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    public class QuickActionsTests
    {
        [Fact]
        public void WithASelectionTheLineToolsAndFormatSelectionAreOffered()
        {
            var ids = QuickActionCatalog.Actions(true).Select(a => a.Id).ToList();

            Assert.Equal(1, ids.IndexOf(QuickActionCatalog.Unformat));
            Assert.Contains(QuickActionCatalog.AddCommas, ids);
            Assert.Contains(QuickActionCatalog.AddQuotesAndCommas, ids);
            Assert.Equal("Format Selection", QuickActionCatalog.Actions(true).Single(a => a.Id == QuickActionCatalog.Format).Name);
        }

        [Fact]
        public void WithoutASelectionTheActionsApplyToTheScript()
        {
            var actions = QuickActionCatalog.Actions(false);

            Assert.DoesNotContain(actions, a => a.Id == QuickActionCatalog.AddCommas);
            Assert.DoesNotContain(actions, a => a.Id == QuickActionCatalog.Unformat);
            Assert.Equal("Format Document", actions.Single(a => a.Id == QuickActionCatalog.Format).Name);
            Assert.All(actions, a => Assert.Contains("the script", a.Description));
        }

        [Fact]
        public void EveryActionHasAUniqueId() =>
            Assert.Equal(QuickActionCatalog.Actions(true).Count, QuickActionCatalog.Actions(true).Select(a => a.Id).Distinct().Count());

        [Fact]
        public void SnippetsAreSplitByWhetherTheyWrapTheSelection()
        {
            var plain = Snippet.Create("st", "", "SELECT TOP 10 *");
            var wrapper = Snippet.Create("isn", "", "ISNULL($SELECTEDTEXT$, 0)");
            var all = new[] { wrapper, plain };

            Assert.Equal(new[] { "isn" }, QuickActionCatalog.SnippetsFor(all, true).Select(s => s.Prefix).ToArray());
            Assert.Equal(new[] { "st" }, QuickActionCatalog.SnippetsFor(all, false).Select(s => s.Prefix).ToArray());
        }

        [Theory]
        [InlineData("brack", true)]
        [InlineData("remove square", true)]
        [InlineData("REMOVE  BRACKETS", true)]
        [InlineData("semicolon remove", false)]
        [InlineData("", true)]
        [InlineData(null, true)]
        public void TheFilterNeedsEveryWord(string text, bool expected) =>
            Assert.Equal(expected, QuickActionCatalog.Matches("Remove Square Brackets", "Removes brackets that are not needed.", text));
    }
}
