using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.History;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The number and the age of the closed tabs that can be reopened, and picking one from the list (#226).</summary>
    public class ClosedTabsLimitTests
    {
        private static KeyValuePair<string, SavedTab> Tab(string key, string text) =>
            new KeyValuePair<string, SavedTab>(key, new SavedTab { Text = text, Server = "s", Database = "d" });

        [Fact]
        public void ATabOlderThanTheMaxAgeIsForgotten()
        {
            var now = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
            var tabs = new ClosedTabs(20, () => now) { MaxAge = TimeSpan.FromHours(1) };
            tabs.Update(new[] { Tab("a", "old"), Tab("b", "new") });
            tabs.Update(new[] { Tab("b", "new") });
            now = now.AddMinutes(50);
            tabs.Update(new KeyValuePair<string, SavedTab>[0]);
            now = now.AddMinutes(20);

            Assert.Equal(new[] { "new" }, tabs.Items.Select(t => t.Text).ToArray());
        }

        [Fact]
        public void ZeroMaxAgeKeepsTabsForever()
        {
            var now = DateTime.UtcNow;
            var tabs = new ClosedTabs(20, () => now);
            tabs.Update(new[] { Tab("a", "x") });
            tabs.Update(new KeyValuePair<string, SavedTab>[0]);
            now = now.AddDays(400);

            Assert.Equal(1, tabs.Count);
        }

        [Fact]
        public void LoweringTheCapacityDropsTheOldest()
        {
            var tabs = new ClosedTabs(5);
            tabs.Update(new[] { Tab("a", "1"), Tab("b", "2"), Tab("c", "3") });
            tabs.Update(new KeyValuePair<string, SavedTab>[0]);
            tabs.Capacity = 2;

            Assert.Equal(2, tabs.Count);
        }

        [Fact]
        public void AChosenTabCanBeTakenOutOfTheList()
        {
            var tabs = new ClosedTabs();
            tabs.Update(new[] { Tab("a", "1"), Tab("b", "2") });
            tabs.Update(new KeyValuePair<string, SavedTab>[0]);
            var first = tabs.Items.Last();

            Assert.True(tabs.Remove(first));
            Assert.False(tabs.Remove(first));
            Assert.Equal(1, tabs.Count);
        }

        [Fact]
        public void TheNameOfATabIsPartOfTheSavedState() =>
            Assert.Equal("Orders check", new SavedTab { Name = "Orders check" }.Name);

        [Fact]
        public void TheSettingsHaveDefaults()
        {
            var behavior = new BehaviorSettings();

            Assert.Equal(20, behavior.ClosedTabsKept);
            Assert.Equal(24, behavior.ClosedTabsHours);
        }
    }
}
