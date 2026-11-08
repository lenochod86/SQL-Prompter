using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>An entry opened from the history goes to the top of the list, below the pinned ones (#378).</summary>
    public sealed class HistoryOpenedOnTopTests : IDisposable
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterOnTop" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        private static string[] Order(HistoryStore store) => HistoryStore.Filter(store.Entries, string.Empty).Select(e => e.Text).ToArray();

        [Fact]
        public void AnOpenedRunComesFirstBelowThePinnedOnes()
        {
            var store = new HistoryStore(Path.Combine(folder, "history.jsonl"));
            var old = store.Add("old", "s", "d", null, Now);
            store.Add("middle", "s", "d", null, Now.AddMinutes(1));
            store.Add("newest", "s", "d", null, Now.AddMinutes(2));
            var pinned = store.Add("pinned", "s", "d", null, Now.AddMinutes(-60));
            store.SetPinned(new[] { pinned.Id }, true);
            Assert.Equal(new[] { "pinned", "newest", "middle", "old" }, Order(store));

            store.Touch(new[] { old.Id }, Now.AddMinutes(10));

            Assert.Equal(new[] { "pinned", "old", "newest", "middle" }, Order(store));
        }

        [Fact]
        public void TheTimeShownIsNotChangedByOpening()
        {
            var store = new HistoryStore(Path.Combine(folder, "history.jsonl"));
            var entry = store.Add("a", "s", "d", null, Now);

            store.Touch(new[] { entry.Id }, Now.AddHours(3));

            Assert.Equal(Now, store.Entries.Single().RanAtUtc);
            Assert.Equal(Now.AddHours(3), store.Entries.Single().SortUtc);
        }

        [Fact]
        public void TheOrderSurvivesSavingLoadingAndTheCleanup()
        {
            var path = Path.Combine(folder, "history.jsonl");
            var store = new HistoryStore(path);
            var first = store.Add("first", "s", "d", null, Now);
            store.Add("second", "s", "d", null, Now.AddMinutes(1));
            store.Touch(new[] { first.Id }, Now.AddMinutes(5));

            var loaded = new HistoryStore(path);
            loaded.Load();
            Assert.Equal(new[] { "first", "second" }, Order(loaded));

            loaded.Delete(new string[0]);
            loaded.Prune(100000, Now.AddMinutes(10));
            var again = new HistoryStore(path);
            again.Load();
            Assert.Equal(new[] { "first", "second" }, Order(again));
        }

        [Fact]
        public void AnOpenedTabComesFirstToo()
        {
            var path = Path.Combine(folder, "tabs.jsonl");
            var tabs = new TabHistoryStore(path) { MinVersionInterval = TimeSpan.Zero };
            tabs.Track("a.sql", "A", "s", "d", "SELECT a", Now);
            tabs.Track("a.sql", "A", "s", "d", "SELECT a", Now.AddSeconds(5));
            tabs.Track("b.sql", "B", "s", "d", "SELECT b", Now.AddMinutes(1));
            tabs.Track("b.sql", "B", "s", "d", "SELECT b", Now.AddMinutes(1).AddSeconds(5));
            Assert.Equal("SELECT b", HistoryStore.Filter(tabs.Entries(), string.Empty).First().Text);

            tabs.Touch(new[] { tabs.Entries().Single(e => e.Tab == "A").Id }, Now.AddMinutes(30));

            Assert.Equal("SELECT a", HistoryStore.Filter(tabs.Entries(), string.Empty).First().Text);
            var loaded = new TabHistoryStore(path);
            loaded.Load();
            Assert.Equal("SELECT a", HistoryStore.Filter(loaded.Entries(), string.Empty).First().Text);
        }
    }
}
