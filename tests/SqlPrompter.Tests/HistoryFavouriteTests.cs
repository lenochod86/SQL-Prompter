using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Favourite runs of the query history: a mark apart from pinning (#285).</summary>
    public sealed class HistoryFavouriteTests : IDisposable
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterHistoryFav" + Guid.NewGuid().ToString("N"));

        private string FilePath => Path.Combine(folder, "history.jsonl");

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void AFavouriteSurvivesAReload()
        {
            var store = new HistoryStore(FilePath);
            var run = store.Add("SELECT 1", "s", "d", null, Now);
            store.SetFavourite(new[] { run.Id }, true);

            var again = new HistoryStore(FilePath);
            again.Load();

            Assert.True(Assert.Single(again.Entries).Favourite);
        }

        [Fact]
        public void UnmarkingIsRememberedToo()
        {
            var store = new HistoryStore(FilePath);
            var run = store.Add("SELECT 1", "s", "d", null, Now);
            store.SetFavourite(new[] { run.Id }, true);
            store.SetFavourite(new[] { run.Id }, false);

            var again = new HistoryStore(FilePath);
            again.Load();

            Assert.False(Assert.Single(again.Entries).Favourite);
        }

        [Fact]
        public void FavouriteAndPinnedAreIndependentMarks()
        {
            var store = new HistoryStore(FilePath);
            var run = store.Add("SELECT 1", "s", "d", null, Now);
            store.SetFavourite(new[] { run.Id }, true);
            store.SetPinned(new[] { run.Id }, true);
            store.SetPinned(new[] { run.Id }, false);

            var again = new HistoryStore(FilePath);
            again.Load();
            var loaded = Assert.Single(again.Entries);

            Assert.True(loaded.Favourite);
            Assert.False(loaded.Pinned);
        }

        [Fact]
        public void AFavouriteKeepsItsPlaceWhilePinnedRunsGoFirst()
        {
            var store = new HistoryStore(FilePath);
            var oldest = store.Add("SELECT oldest", "s", "d", null, Now);
            store.Add("SELECT middle", "s", "d", null, Now.AddMinutes(1));
            store.Add("SELECT newest", "s", "d", null, Now.AddMinutes(2));
            store.SetFavourite(new[] { oldest.Id }, true);

            Assert.Equal(new[] { "SELECT newest", "SELECT middle", "SELECT oldest" }, HistoryStore.Filter(store.Entries, string.Empty).Select(e => e.Text).ToArray());

            store.SetPinned(new[] { oldest.Id }, true);

            Assert.Equal("SELECT oldest", HistoryStore.Filter(store.Entries, string.Empty).First().Text);
        }

        [Fact]
        public void TheFavouritesViewShowsExactlyTheFavouritesAndCombinesWithTheFilter()
        {
            var store = new HistoryStore(FilePath);
            var a = store.Add("SELECT alpha", "s", "d", null, Now);
            store.Add("SELECT beta", "s", "d", null, Now.AddMinutes(1));
            var c = store.Add("SELECT gamma", "s", "d", null, Now.AddMinutes(2));
            store.SetFavourite(new[] { a.Id, c.Id }, true);

            Assert.Equal(new[] { "SELECT gamma", "SELECT alpha" }, HistoryStore.Filter(store.Entries, string.Empty, HistoryView.Favourites).Select(e => e.Text).ToArray());
            Assert.Equal(new[] { "SELECT alpha" }, HistoryStore.Filter(store.Entries, "alpha", HistoryView.Favourites).Select(e => e.Text).ToArray());
            Assert.Empty(HistoryStore.Filter(store.Entries, "beta", HistoryView.Favourites));
        }

        [Fact]
        public void AFavouriteIsKeptByTheCleanup()
        {
            var store = new HistoryStore(FilePath);
            var old = store.Add("SELECT old favourite", "s", "d", null, Now.AddDays(-100));
            store.Add("SELECT old", "s", "d", null, Now.AddDays(-90));
            store.SetFavourite(new[] { old.Id }, true);

            Assert.Equal(1, store.Prune(30, Now));

            var again = new HistoryStore(FilePath);
            again.Load();

            Assert.Equal("SELECT old favourite", Assert.Single(again.Entries).Text);
        }

        [Fact]
        public void TheMarkSurvivesARewriteOfTheFile()
        {
            var store = new HistoryStore(FilePath);
            var keep = store.Add("SELECT keep", "s", "d", null, Now);
            var drop = store.Add("SELECT drop", "s", "d", null, Now);
            store.SetFavourite(new[] { keep.Id }, true);
            store.Delete(new[] { drop.Id });

            var again = new HistoryStore(FilePath);
            again.Load();

            Assert.True(Assert.Single(again.Entries).Favourite);
        }
    }
}
