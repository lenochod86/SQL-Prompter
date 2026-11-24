using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>A window opened from the history continues its tab record instead of making a duplicate (#375).</summary>
    public sealed class TabRebindTests : IDisposable
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterRebind" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        private TabHistoryStore Store() => new TabHistoryStore(Path.Combine(folder, "tabs.jsonl")) { MinVersionInterval = TimeSpan.Zero };

        private static string Key(string name) => Path.Combine("x", name);

        private static void Make(TabHistoryStore store, string key, string name, string text)
        {
            store.Track(key, name, "s", "d", text, Now);
            store.Track(key, name, "s", "d", text, Now.AddSeconds(5));
        }

        [Fact]
        public void ARebindContinuesTheSameRecord()
        {
            var store = Store();
            Make(store, Key("SQLQuery3.sql"), "Reports", "SELECT 1");
            store.Close(Key("SQLQuery3.sql"), Now.AddMinutes(1));
            var id = store.Entries().Single().Id;

            var name = store.Rebind(id, Key("SQLQuery7.sql"), Now.AddMinutes(2));
            store.Track(Key("SQLQuery7.sql"), "Reports", "s", "d", "SELECT 1", Now.AddMinutes(2));
            store.Track(Key("SQLQuery7.sql"), "Reports", "s", "d", "SELECT 1 -- more", Now.AddMinutes(3));
            store.Track(Key("SQLQuery7.sql"), "Reports", "s", "d", "SELECT 1 -- more", Now.AddMinutes(4));

            Assert.Equal("Reports", name);
            var entry = store.Entries().Single();
            Assert.Equal(id, entry.Id);
            Assert.Equal(Key("SQLQuery7.sql"), entry.Key);
            Assert.False(entry.ClosedAtUtc.HasValue);
            Assert.Equal(new[] { "SELECT 1", "SELECT 1 -- more" }, entry.Versions.Select(v => v.Text).ToArray());
            Assert.Equal("Reports", entry.Tab);
        }

        [Fact]
        public void AClosedAndReopenedRecordStaysOneRecordAndKeepsItsName()
        {
            var store = Store();
            Make(store, Key("SQLQuery3.sql"), "Reports", "SELECT 1");
            store.Close(Key("SQLQuery3.sql"), Now.AddMinutes(1));
            var id = store.Entries().Single().Id;

            for (var round = 0; round < 3; round++)
            {
                var key = Key("SQLQuery" + (10 + round) + ".sql");
                store.Rebind(id, key, Now.AddMinutes(2 + round * 3));
                store.Track(key, "Reports", "s", "d", "SELECT 1", Now.AddMinutes(2 + round * 3));
                store.Close(key, Now.AddMinutes(3 + round * 3));
            }

            var entry = store.Entries().Single();
            Assert.Equal("Reports", entry.Tab);
            Assert.True(entry.ClosedAtUtc.HasValue);
            Assert.Equal(string.Empty, entry.Key);
        }

        [Fact]
        public void ARebindSurvivesSavingAndLoading()
        {
            var store = Store();
            Make(store, Key("SQLQuery3.sql"), "Reports", "SELECT 1");
            store.Close(Key("SQLQuery3.sql"), Now.AddMinutes(1));
            var id = store.Entries().Single().Id;
            store.Rebind(id, Key("SQLQuery7.sql"), Now.AddMinutes(2));

            var loaded = Store();
            loaded.Load();

            Assert.Equal(id, loaded.Entries().Single().Id);
        }

        [Fact]
        public void AnUnknownIdOrAnEmptyKeyChangesNothing()
        {
            var store = Store();
            Make(store, Key("SQLQuery3.sql"), "Reports", "SELECT 1");

            Assert.Null(store.Rebind("nope", Key("SQLQuery7.sql"), Now));
            Assert.Null(store.Rebind(store.Entries().Single().Id, string.Empty, Now));
            Assert.Equal(Key("SQLQuery3.sql"), store.Entries().Single().Key);
        }

        [Fact]
        public void ARunOfTheHistoryFindsItsTabRecord()
        {
            var store = Store();
            Make(store, Key("SQLQuery3.sql"), "SQLQuery3.sql", "SELECT 1");
            Make(store, Key("SQLQuery4.sql"), "SQLQuery4.sql", "SELECT 1");
            store.Close(Key("SQLQuery3.sql"), Now.AddMinutes(1));
            var id = store.Entries().Single(e => e.Tab == "SQLQuery3.sql").Id;

            Assert.Equal(id, store.FindForRun("SQLQuery3.sql", "SELECT 1", "s", "d"));
            Assert.Null(store.FindForRun("SQLQuery3.sql", "SELECT 2", "s", "d"));
            Assert.Null(store.FindForRun("SQLQuery9.sql", "SELECT 1", "s", "d"));
            Assert.Null(store.FindForRun("SQLQuery3.sql", "SELECT 1", "other", "d"));
        }

        [Fact]
        public void AnOpenTabEntryKnowsItsDocument()
        {
            var store = Store();
            Make(store, Key("SQLQuery3.sql"), "SQLQuery3.sql", "SELECT 1");

            Assert.Equal(Key("SQLQuery3.sql"), store.Entries().Single().Key);
        }

        // A tab that was restored at start continues its history entry (#529): its id is kept with the saved tab.

        [Fact]
        public void TheIdOfTheOpenTabIsKnownByItsDocument()
        {
            var store = Store();
            Make(store, Key("SQLQuery5.sql"), "", "SELECT 1");

            Assert.Equal(store.Entries().Single().Id, store.IdOfOpen(Key("SQLQuery5.sql")));
            Assert.Null(store.IdOfOpen(Key("SQLQuery9.sql")));
        }

        [Fact]
        public void ARestoredTabAfterANormalCloseContinuesItsEntryWithTheNewName()
        {
            var store = Store();
            Make(store, Key("SQLQuery5.sql"), "", "SELECT 1");
            var id = store.IdOfOpen(Key("SQLQuery5.sql"));
            store.Close(Key("SQLQuery5.sql"), Now.AddMinutes(1));

            // SSMS restores the unsaved tab under another number.
            Assert.Equal(string.Empty, store.Rebind(id, Key("SQLQuery4.sql"), Now.AddMinutes(2)));
            store.Track(Key("SQLQuery4.sql"), "", "s", "d", "SELECT 1", Now.AddMinutes(2));
            store.Track(Key("SQLQuery4.sql"), "", "s", "d", "SELECT 1 FROM t", Now.AddMinutes(3));
            store.Track(Key("SQLQuery4.sql"), "", "s", "d", "SELECT 1 FROM t", Now.AddMinutes(4));

            var entry = Assert.Single(store.Entries());
            Assert.Equal(id, entry.Id);
            Assert.False(entry.ClosedAtUtc.HasValue);
            Assert.Equal("SELECT 1 FROM t", entry.Text);
        }

        [Fact]
        public void ARestoredTabAfterAKilledProcessContinuesItsEntryAndKeepsItsVersions()
        {
            var store = Store();
            Make(store, Key("SQLQuery5.sql"), "", "SELECT 1");
            store.Track(Key("SQLQuery5.sql"), "", "s", "d", "SELECT 2", Now.AddSeconds(10));
            store.Track(Key("SQLQuery5.sql"), "", "s", "d", "SELECT 2", Now.AddSeconds(20));
            var id = store.IdOfOpen(Key("SQLQuery5.sql"));

            // The process was killed: the new run loads the file, where the tab is still open.
            var next = Store();
            next.Load();
            next.Rebind(id, Key("SQLQuery4.sql"), Now.AddMinutes(5));
            next.Track(Key("SQLQuery4.sql"), "", "s", "d", "SELECT 2", Now.AddMinutes(5));

            var entry = Assert.Single(next.Entries());
            Assert.Equal(id, entry.Id);
            Assert.False(entry.ClosedAtUtc.HasValue);
            Assert.Equal(2, next.Tabs.Single().Versions.Count);
        }

        [Fact]
        public void TheHistoryIdIsSavedWithTheTab()
        {
            var path = Path.Combine(folder, "tabs.json");
            new TabSessionStore(path).Save(new[] { new SavedTab { Text = "SELECT 1", HistoryId = "abc123" }, new SavedTab { Text = "SELECT 2" } });

            var tabs = new TabSessionStore(path).Load();

            Assert.Equal("abc123", tabs[0].HistoryId);
            Assert.Null(tabs[1].HistoryId);
        }
    }
}
