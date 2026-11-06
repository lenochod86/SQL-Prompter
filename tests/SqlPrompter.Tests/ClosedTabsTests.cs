using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The list of closed query tabs (#226).</summary>
    public class ClosedTabsTests
    {
        private static KeyValuePair<string, SavedTab> Tab(string key, string text, string file = null) =>
            new KeyValuePair<string, SavedTab>(key, new SavedTab { Text = text, File = file, Server = "s", Database = "d" });

        [Fact]
        public void ATabThatDisappearsIsKeptWithItsLastState()
        {
            var tabs = new ClosedTabs();
            tabs.Update(new[] { Tab("a", "SELECT 1"), Tab("b", "SELECT 2") });

            var found = tabs.Update(new[] { Tab("b", "SELECT 2 + 2") });

            Assert.Equal(1, found);
            Assert.Equal("SELECT 1", tabs.Pop().Text);
            Assert.Null(tabs.Pop());
        }

        [Fact]
        public void TheLastClosedComesFirst()
        {
            var tabs = new ClosedTabs();
            tabs.Update(new[] { Tab("a", "one"), Tab("b", "two") });
            tabs.Update(new[] { Tab("b", "two") });
            tabs.Update(new KeyValuePair<string, SavedTab>[0]);

            Assert.Equal(new[] { "two", "one" }, tabs.Items.Select(t => t.Text).ToArray());
            Assert.Equal("two", tabs.Pop().Text);
        }

        [Fact]
        public void AnEmptyNewTabIsNotKeptButAFileIs()
        {
            var tabs = new ClosedTabs();
            tabs.Update(new[] { Tab("a", "  "), Tab("b", string.Empty, @"C:\x\a.sql") });
            tabs.Update(new KeyValuePair<string, SavedTab>[0]);

            Assert.Equal(@"C:\x\a.sql", Assert.Single(tabs.Items).File);
        }

        [Fact]
        public void TheListIsLimited()
        {
            var tabs = new ClosedTabs(2);
            tabs.Update(new[] { Tab("a", "1"), Tab("b", "2"), Tab("c", "3") });
            tabs.Update(new KeyValuePair<string, SavedTab>[0]);

            Assert.Equal(2, tabs.Count);
        }

        [Fact]
        public void TheSameTabsAgainChangeNothing()
        {
            var tabs = new ClosedTabs();
            tabs.Update(new[] { Tab("a", "1") });

            Assert.Equal(0, tabs.Update(new[] { Tab("A", "1 edited") }));
            Assert.Equal(0, tabs.Count);
        }
    }
}
