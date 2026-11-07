using System;
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
        public void OpenShowsRunsOfTabsThatAreNotClosed() =>
            Assert.Equal(new[] { "SELECT open" }, Texts(Store(), string.Empty, HistoryView.Open));

        [Fact]
        public void ClosedShowsRunsOfClosedTabs() =>
            Assert.Equal(new[] { "SELECT pinned and closed", "SELECT closed" }, Texts(Store(), string.Empty, HistoryView.Closed));

        [Fact]
        public void TheTextFilterCombinesWithTheView()
        {
            var store = Store();

            Assert.Equal(new[] { "SELECT closed" }, Texts(store, "closed", HistoryView.Closed).Where(t => !t.Contains("pinned")).ToArray());
            Assert.Empty(Texts(store, "open", HistoryView.Closed));
            Assert.Equal(new[] { "SELECT open" }, Texts(store, "open", HistoryView.Open));
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
