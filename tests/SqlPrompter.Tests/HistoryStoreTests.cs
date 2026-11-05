using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public sealed class HistoryStoreTests : IDisposable
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterHistory" + Guid.NewGuid().ToString("N"));

        private string FilePath => Path.Combine(folder, "history.jsonl");

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void AddedRunsSurviveReloading()
        {
            var store = new HistoryStore(FilePath);
            store.Add("SELECT 1\r\nGO\r\nSELECT 'ž'", "srv", "db1", "sa", Now);
            store.Add("SELECT 2", "srv2", "db2", null, Now.AddMinutes(1));

            var again = new HistoryStore(FilePath);
            again.Load();

            Assert.Equal(2, again.Entries.Count);
            Assert.Equal("SELECT 1\r\nGO\r\nSELECT 'ž'", again.Entries[0].Text);
            Assert.Equal("srv", again.Entries[0].Server);
            Assert.Equal("db1", again.Entries[0].Database);
            Assert.Equal("sa", again.Entries[0].Login);
            Assert.Null(again.Entries[1].Login);
            Assert.Equal(Now, again.Entries[0].RanAtUtc);
            Assert.Null(again.Entries[0].ClosedAtUtc);
        }

        [Fact]
        public void TheClosingTimeIsKeptWithTheRun()
        {
            var store = new HistoryStore(FilePath);
            var first = store.Add("SELECT 1", "s", "d", null, Now);
            var second = store.Add("SELECT 2", "s", "d", null, Now);
            store.MarkClosed(new[] { first.Id }, Now.AddHours(1));

            var again = new HistoryStore(FilePath);
            again.Load();

            Assert.Equal(Now.AddHours(1), again.Entries.Single(e => e.Id == first.Id).ClosedAtUtc);
            Assert.Null(again.Entries.Single(e => e.Id == second.Id).ClosedAtUtc);
        }

        [Fact]
        public void BlankTextsAreNotRecorded()
        {
            var store = new HistoryStore(FilePath);

            Assert.Null(store.Add("  \r\n ", "s", "d", null, Now));
            Assert.Empty(store.Entries);
            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public void BrokenLinesAreSkipped()
        {
            var store = new HistoryStore(FilePath);
            store.Add("SELECT 1", "s", "d", null, Now);
            File.AppendAllText(FilePath, "not json\n{\"id\":\"x\"}\n\n");
            store.Add("SELECT 2", "s", "d", null, Now);

            var again = new HistoryStore(FilePath);
            again.Load();

            Assert.Equal(new[] { "SELECT 1", "SELECT 2" }, again.Entries.Select(e => e.Text));
        }

        [Fact]
        public void PruningRemovesOldRunsAndRewritesTheFile()
        {
            var store = new HistoryStore(FilePath);
            store.Add("old", "s", "d", null, Now.AddDays(-40));
            store.Add("recent", "s", "d", null, Now.AddDays(-5));

            Assert.Equal(1, store.Prune(30, Now));
            Assert.Equal(0, store.Prune(0, Now));

            var again = new HistoryStore(FilePath);
            again.Load();
            Assert.Equal("recent", Assert.Single(again.Entries).Text);
        }

        [Fact]
        public void DeletingRemovesRunsFromTheFile()
        {
            var store = new HistoryStore(FilePath);
            var one = store.Add("one", "s", "d", null, Now);
            store.Add("two", "s", "d", null, Now);

            store.Delete(new[] { one.Id });

            var again = new HistoryStore(FilePath);
            again.Load();
            Assert.Equal("two", Assert.Single(again.Entries).Text);
        }

        [Fact]
        public void LoadingKeepsWhatWasAddedBeforeTheFileWasRead()
        {
            var store = new HistoryStore(FilePath);
            store.Add("old", "s", "d", null, Now);
            var other = new HistoryStore(FilePath);
            other.Add("added first", "s", "d", null, Now.AddMinutes(1));

            other.Load();

            Assert.Equal(new[] { "old", "added first" }, other.Entries.Select(e => e.Text));
        }

        [Fact]
        public void FilteringNeedsEveryWordAndComesNewestFirst()
        {
            var store = new HistoryStore(FilePath);
            store.Add("SELECT * FROM Orders", "srvA", "Shop", null, Now);
            store.Add("SELECT * FROM Users", "srvA", "Shop", null, Now.AddMinutes(1));
            store.Add("select * from orders", "srvB", "Other", null, Now.AddMinutes(2));

            Assert.Equal(new[] { "select * from orders", "SELECT * FROM Orders" }, HistoryStore.Filter(store.Entries, "orders").Select(e => e.Text));
            Assert.Equal(new[] { "SELECT * FROM Orders" }, HistoryStore.Filter(store.Entries, "ORDERS shop").Select(e => e.Text));
            Assert.Equal(3, HistoryStore.Filter(store.Entries, "  ").Count);
            Assert.Empty(HistoryStore.Filter(store.Entries, "nothing"));
        }

        [Fact]
        public void HistoryIsOnAndKeptForeverByDefault()
        {
            var behavior = new BehaviorSettings();

            Assert.True(behavior.KeepHistory);
            Assert.Equal(0, behavior.HistoryDays);
        }
    }
}
