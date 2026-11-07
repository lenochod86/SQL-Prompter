using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The views of the query history: all, pinned, open and closed runs (#284).</summary>
    public sealed class HistoryViewTests : IDisposable
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterHistoryView" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        private HistoryStore Store()
        {
            var store = new HistoryStore(Path.Combine(folder, "history.jsonl"));
            var open = store.Add("SELECT open", "s", "d", null, Now);
            var closed = store.Add("SELECT closed", "s", "d", null, Now.AddMinutes(1));
            var pinned = store.Add("SELECT pinned and closed", "s", "d", null, Now.AddMinutes(2));
            store.MarkClosed(new[] { closed.Id, pinned.Id }, Now.AddMinutes(5));
            store.SetPinned(new[] { pinned.Id }, true);
            Assert.NotNull(open);
            return store;
        }

        private static string[] Texts(HistoryStore store, string filter, HistoryView view) =>
            HistoryStore.Filter(store.Entries, filter, view).Select(e => e.Text).ToArray();

        [Fact]
        public void AllShowsEverythingPinnedFirst() =>
            Assert.Equal(new[] { "SELECT pinned and closed", "SELECT closed", "SELECT open" }, Texts(Store(), string.Empty, HistoryView.All));

        [Fact]
        public void PinnedShowsOnlyPinnedRuns() =>
            Assert.Equal(new[] { "SELECT pinned and closed" }, Texts(Store(), string.Empty, HistoryView.Pinned));

        [Fact]
        public void RunsShowsTheExecutedQueriesAndNoTabs()
        {
            var store = Store();
            var all = store.Entries.Concat(TabEntries()).ToList();

            Assert.Equal(new[] { "SELECT pinned and closed", "SELECT closed", "SELECT open" }, HistoryStore.Filter(all, string.Empty, HistoryView.Runs).Select(e => e.Text).ToArray());
            Assert.Equal(5, HistoryStore.Filter(all, string.Empty, HistoryView.All).Count);
        }

        private IReadOnlyList<HistoryEntry> TabEntries()
        {
            var tabs = new TabHistoryStore(Path.Combine(folder, "tabs.jsonl")) { MinVersionInterval = TimeSpan.Zero };
            tabs.Track("a.sql", "A", "s", "d", "SELECT tab open", Now);
            tabs.Track("a.sql", "A", "s", "d", "SELECT tab open", Now.AddSeconds(5));
            tabs.Track("b.sql", "B", "s", "d", "SELECT tab closed", Now.AddMinutes(1));
            tabs.Track("b.sql", "B", "s", "d", "SELECT tab closed", Now.AddMinutes(1).AddSeconds(5));
            tabs.Close("b.sql", Now.AddMinutes(9));
            return tabs.Entries();
        }

        [Fact]
        public void OpenAndClosedShowTabsNotRuns()
        {
            var all = Store().Entries.Concat(TabEntries()).ToList();

            Assert.Equal(new[] { "SELECT tab open" }, HistoryStore.Filter(all, string.Empty, HistoryView.Open).Select(e => e.Text).ToArray());
            Assert.Equal(new[] { "SELECT tab closed" }, HistoryStore.Filter(all, string.Empty, HistoryView.Closed).Select(e => e.Text).ToArray());
        }

        [Fact]
        public void TheTextFilterCombinesWithTheView()
        {
            var all = Store().Entries.Concat(TabEntries()).ToList();

            Assert.Equal(new[] { "SELECT tab closed" }, HistoryStore.Filter(all, "closed", HistoryView.Closed).Select(e => e.Text).ToArray());
            Assert.Empty(HistoryStore.Filter(all, "open", HistoryView.Closed));
            Assert.Equal(new[] { "SELECT tab open" }, HistoryStore.Filter(all, "open", HistoryView.Open).Select(e => e.Text).ToArray());
        }

        [Fact]
        public void AViewWithoutRowsIsEmptyNotAnError() =>
            Assert.Empty(HistoryStore.Filter(new HistoryEntry[0], string.Empty, HistoryView.Pinned));

        [Fact]
        public void TheOldFilterIsTheAllView()
        {
            var store = Store();

            Assert.Equal(Texts(store, "select", HistoryView.All), HistoryStore.Filter(store.Entries, "select").Select(e => e.Text).ToArray());
        }
    }
}
