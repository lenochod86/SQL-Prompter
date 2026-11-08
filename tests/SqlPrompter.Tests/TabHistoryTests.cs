using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The history tracks query tabs and the versions of their text, not only the runs (#302).</summary>
    public sealed class TabHistoryTests : IDisposable
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterTabHistory" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        private string FilePath => Path.Combine(folder, "tabhistory.jsonl");

        private TabHistoryStore NewStore() => new TabHistoryStore(FilePath);

        [Fact]
        public void ATabThatWasNeverRunIsOpen()
        {
            var store = NewStore();

            store.Track("SQLQuery1.sql", "SQLQuery1.sql", "srv", "db", "SELECT 'lalala'", T0);

            var entry = Assert.Single(store.Entries());
            Assert.Equal(HistoryKind.Tab, entry.Kind);
            Assert.Null(entry.ClosedAtUtc);
            Assert.Equal("srv", entry.Server);
            Assert.Single(HistoryStore.Filter(store.Entries(), string.Empty, HistoryView.Open));
            Assert.Empty(HistoryStore.Filter(store.Entries(), string.Empty, HistoryView.Closed));
        }

        [Fact]
        public void AnEmptyTabIsNotStored()
        {
            var store = NewStore();

            Assert.False(store.Track("a.sql", "a", "s", "d", "   \r\n", T0));

            Assert.Empty(store.Entries());
            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public void ATextBecomesAVersionOnlyAfterItStayedUnchangedForOneCheck()
        {
            var store = NewStore();

            store.Track("a.sql", "a", "s", "d", "SELECT 1", T0);
            store.Track("a.sql", "a", "s", "d", "SELECT 12", T0.AddSeconds(5));
            Assert.Empty(store.Tabs.Single().Versions);

            store.Track("a.sql", "a", "s", "d", "SELECT 12", T0.AddSeconds(10));
            Assert.Equal(new[] { "SELECT 12" }, store.Tabs.Single().Versions.Select(v => v.Text).ToArray());
        }

        [Fact]
        public void AVersionIsNotStoredMoreOftenThanTheMinimumInterval()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "s", "d", "v1", T0);
            store.Track("a.sql", "a", "s", "d", "v1", T0.AddSeconds(5));
            store.Track("a.sql", "a", "s", "d", "v2", T0.AddSeconds(6));
            store.Track("a.sql", "a", "s", "d", "v2", T0.AddSeconds(7));

            Assert.Single(store.Tabs.Single().Versions);

            store.Track("a.sql", "a", "s", "d", "v2", T0.AddSeconds(10));
            Assert.Equal(new[] { "v1", "v2" }, store.Tabs.Single().Versions.Select(v => v.Text).ToArray());
        }

        [Fact]
        public void AnUnchangedTabAddsNothingToTheFile()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "s", "d", "v1", T0);
            store.Track("a.sql", "a", "s", "d", "v1", T0.AddSeconds(5));
            var size = new FileInfo(FilePath).Length;

            for (var i = 2; i < 20; i++)
            {
                store.Track("a.sql", "a", "s", "d", "v1", T0.AddSeconds(5 * i));
            }

            Assert.Equal(size, new FileInfo(FilePath).Length);
        }

        [Fact]
        public void FlushStoresTheTextAtOnceOnCloseSaveOrRun()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "s", "d", "SELECT 1", T0);

            Assert.True(store.Flush("a.sql", "SELECT 1 + 1", T0.AddSeconds(1)));
            Assert.False(store.Flush("a.sql", "SELECT 1 + 1", T0.AddSeconds(2)));
            Assert.False(store.Flush("a.sql", "  ", T0.AddSeconds(3)));
            Assert.False(store.Flush("unknown.sql", "x", T0.AddSeconds(3)));

            Assert.Equal("SELECT 1 + 1", store.Tabs.Single().LatestText);
        }

        [Fact]
        public void ClosingMovesATabToClosedAndItKeepsItsLastText()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "s", "d", "SELECT 1", T0);
            store.Flush("a.sql", "SELECT 1", T0.AddSeconds(1));

            store.Close("a.sql", T0.AddMinutes(3));

            var entry = Assert.Single(HistoryStore.Filter(store.Entries(), string.Empty, HistoryView.Closed));
            Assert.Equal(T0.AddMinutes(3), entry.ClosedAtUtc);
            Assert.Equal("SELECT 1", entry.Text);
            Assert.Empty(HistoryStore.Filter(store.Entries(), string.Empty, HistoryView.Open));
        }

        [Fact]
        public void ClosingStoresTheTextTheLastCheckSaw()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "s", "d", "first", T0);
            store.Track("a.sql", "a", "s", "d", "first", T0.AddSeconds(5));
            store.Track("a.sql", "a", "s", "d", "typed a moment ago", T0.AddSeconds(10));

            store.Close("a.sql", T0.AddSeconds(11));

            Assert.Equal(new[] { "first", "typed a moment ago" }, store.Tabs.Single().Versions.Select(v => v.Text).ToArray());
        }

        [Fact]
        public void ANewTabWithTheSameDocumentNameIsAnotherEntry()
        {
            var store = NewStore();
            store.Track("SQLQuery1.sql", "SQLQuery1.sql", "s", "d", "first", T0);
            store.Close("SQLQuery1.sql", T0.AddMinutes(1));

            store.Track("SQLQuery1.sql", "SQLQuery1.sql", "s", "d", "second", T0.AddMinutes(2));

            Assert.Equal(2, store.Tabs.Count);
            Assert.Single(HistoryStore.Filter(store.Entries(), string.Empty, HistoryView.Open));
        }

        [Fact]
        public void TheFileIsReadBackAndATabOfAnEndedSessionIsClosedAtItsLastChange()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "srv", "db", "SELECT 1", T0);
            store.Flush("a.sql", "SELECT 1", T0.AddMinutes(2));

            var again = NewStore();
            again.Load();

            var tab = again.Tabs.Single();
            Assert.Equal(T0.AddMinutes(2), tab.ClosedAtUtc);
            Assert.Equal("SELECT 1", tab.LatestText);
            Assert.Equal("srv", tab.Server);

            var third = NewStore();
            third.Load();
            Assert.Equal(T0.AddMinutes(2), third.Tabs.Single().ClosedAtUtc);
        }

        [Fact]
        public void ARestoredTabWithTheSameTextTakesTheOldEntryOver()
        {
            var store = NewStore();
            store.Track("SQLQuery1.sql", "SQLQuery1.sql", "srv", "db", "SELECT 1", T0);
            store.Flush("SQLQuery1.sql", "SELECT 1", T0.AddMinutes(1));
            var again = NewStore();
            again.Load();

            again.Track("SQLQuery7.sql", "SQLQuery7.sql", "srv", "db", "SELECT 1", T0.AddDays(1));

            var tab = Assert.Single(again.Tabs);
            Assert.Null(tab.ClosedAtUtc);
            Assert.Equal("SQLQuery7.sql", tab.Name);
            var third = NewStore();
            third.Load();
            Assert.Single(third.Tabs);
        }

        [Fact]
        public void ARestoredTabWithOtherTextIsANewEntry()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "srv", "db", "SELECT 1", T0);
            store.Flush("a.sql", "SELECT 1", T0.AddMinutes(1));
            var again = NewStore();
            again.Load();

            again.Track("b.sql", "b", "srv", "db", "SELECT 2", T0.AddDays(1));

            Assert.Equal(2, again.Tabs.Count);
        }

        [Fact]
        public void TheSqlLoginOfATabIsKeptAndGivenToTheEntry()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "srv2", "db", "SELECT 1", T0, "claude");
            store.Close("a.sql", T0.AddMinutes(1));

            Assert.Equal("claude", store.Entries().Single().Login);
            var again = NewStore();
            again.Load();
            Assert.Equal("claude", again.Entries().Single().Login);
        }

        [Fact]
        public void ChangingTheConnectionUpdatesTheTab()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "srv", "db1", "SELECT 1", T0);

            store.Track("a.sql", "a", "srv", "db2", "SELECT 1", T0.AddSeconds(5));

            Assert.Equal("db2", store.Tabs.Single().Database);
            var again = NewStore();
            again.Load();
            Assert.Equal("db2", again.Tabs.Single().Database);
        }

        [Fact]
        public void ThinningKeepsRecentVersionsThenOnePerHourThenOnePerDay()
        {
            var now = T0.AddDays(10);
            var versions = Enumerable.Range(0, 24).Select(i => new TabVersion("recent" + i, now.AddMinutes(-i * 4))).ToList();
            versions.AddRange(Enumerable.Range(0, 6).Select(i => new TabVersion("hourly" + i, now.AddHours(-5).AddMinutes(-i * 10))));
            versions.AddRange(Enumerable.Range(0, 6).Select(i => new TabVersion("daily" + i, now.AddDays(-5).AddMinutes(-i * 30))));

            var thinned = VersionThinning.Thin(versions, 50, now);

            Assert.Equal(24, thinned.Count(v => v.Text.StartsWith("recent", StringComparison.Ordinal)));
            Assert.InRange(thinned.Count(v => v.Text.StartsWith("hourly", StringComparison.Ordinal)), 1, 2);
            Assert.Equal(1, thinned.Count(v => v.Text.StartsWith("daily", StringComparison.Ordinal)));
            Assert.True(thinned.SequenceEqual(thinned.OrderBy(v => v.AtUtc)));
        }

        [Fact]
        public void TheLimitDropsTheOldestVersionsAndKeepsTheNewest()
        {
            var now = T0;
            var versions = Enumerable.Range(0, 80).Select(i => new TabVersion("v" + i, now.AddMinutes(-80 + i))).ToList();

            var thinned = VersionThinning.Thin(versions, 50, now);

            Assert.Equal(50, thinned.Count);
            Assert.Equal("v79", thinned.Last().Text);
            Assert.Equal("v30", thinned.First().Text);
        }

        [Fact]
        public void TheStoreStaysWithinTheVersionLimit()
        {
            var store = NewStore();
            store.MaxVersions = 10;
            store.MinVersionInterval = TimeSpan.Zero;
            store.Track("a.sql", "a", "s", "d", "start", T0);
            for (var i = 0; i < 60; i++)
            {
                store.Flush("a.sql", "version " + i, T0.AddSeconds(i));
            }

            var versions = store.Tabs.Single().Versions;
            Assert.True(versions.Count <= 20);
            Assert.Equal("version 59", versions.Last().Text);
            var again = NewStore();
            again.Load();
            Assert.Equal("version 59", again.Tabs.Single().LatestText);
        }

        [Fact]
        public void PruneRemovesOldClosedTabsOnly()
        {
            var store = NewStore();
            store.Track("old.sql", "old", "s", "d", "old", T0);
            store.Flush("old.sql", "old", T0);
            store.Close("old.sql", T0.AddMinutes(1));
            store.Track("open.sql", "open", "s", "d", "open", T0);
            store.Flush("open.sql", "open", T0);

            Assert.Equal(1, store.Prune(30, T0.AddDays(60)));

            Assert.Equal(new[] { "open" }, store.Tabs.Select(t => t.Name).ToArray());
            var again = NewStore();
            again.Load();
            Assert.Equal(new[] { "open" }, again.Tabs.Select(t => t.Name).ToArray());
        }

        [Fact]
        public void ATabCanBePinnedAndMarkedAsAFavouriteAndKeepsBothOverARestart()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "s", "d", "x", T0);
            store.Close("a.sql", T0.AddMinutes(1));
            var id = store.Tabs.Single().Id;

            store.SetPinned(new[] { id }, true);
            store.SetFavourite(new[] { id }, true);

            var entry = store.Entries().Single();
            Assert.True(entry.Pinned);
            Assert.True(entry.Favourite);
            Assert.Single(HistoryStore.Filter(store.Entries(), string.Empty, HistoryView.Pinned));
            Assert.Single(HistoryStore.Filter(store.Entries(), string.Empty, HistoryView.Favourites));
            var again = NewStore();
            again.Load();
            Assert.True(again.Entries().Single().Pinned);
            Assert.True(again.Entries().Single().Favourite);

            again.SetPinned(new[] { id }, false);
            var third = NewStore();
            third.Load();
            Assert.False(third.Entries().Single().Pinned);
            Assert.True(third.Entries().Single().Favourite);
        }

        [Fact]
        public void PinnedAndFavouriteTabsSurviveThePruneAndARewrite()
        {
            var store = NewStore();
            foreach (var name in new[] { "old", "pinned", "favourite" })
            {
                store.Track(name + ".sql", name, "s", "d", name, T0);
                store.Flush(name + ".sql", name, T0);
                store.Close(name + ".sql", T0.AddMinutes(1));
            }

            store.SetPinned(new[] { store.Tabs.Single(t => t.Name == "pinned").Id }, true);
            store.SetFavourite(new[] { store.Tabs.Single(t => t.Name == "favourite").Id }, true);

            Assert.Equal(1, store.Prune(30, T0.AddDays(60)));

            var again = NewStore();
            again.Load();
            Assert.Equal(new[] { "favourite", "pinned" }, again.Tabs.Select(t => t.Name).OrderBy(n => n).ToArray());
            Assert.True(again.Tabs.Single(t => t.Name == "pinned").Pinned);
        }

        [Fact]
        public void DeleteRemovesATabAndItsVersions()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "s", "d", "x", T0);
            var id = store.Tabs.Single().Id;

            store.Delete(new[] { id });

            Assert.Empty(store.Tabs);
            var again = NewStore();
            again.Load();
            Assert.Empty(again.Tabs);
        }

        [Fact]
        public void AnEntryOfATabCarriesItsVersions()
        {
            var store = NewStore();
            store.Track("a.sql", "a", "s", "d", "one", T0);
            store.Flush("a.sql", "one", T0);
            store.Flush("a.sql", "two", T0.AddMinutes(1));

            var entry = store.Entries().Single();

            Assert.Equal(new[] { "one", "two" }, entry.Versions.Select(v => v.Text).ToArray());
            Assert.Equal("two", entry.Text);
            Assert.Equal(T0.AddMinutes(1), entry.RanAtUtc);
        }
    }
}
