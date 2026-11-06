using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Favourite runs of the query history (#225).</summary>
    public sealed class HistoryPinTests : IDisposable
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterHistoryPin" + Guid.NewGuid().ToString("N"));

        private string FilePath => Path.Combine(folder, "history.jsonl");

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void APinnedRunSurvivesPruningAndReloading()
        {
            var store = new HistoryStore(FilePath);
            var old = store.Add("SELECT old", "s", "d", null, Now.AddDays(-100));
            store.Add("SELECT old unpinned", "s", "d", null, Now.AddDays(-90));
            store.SetPinned(new[] { old.Id }, true);

            Assert.Equal(1, store.Prune(30, Now));

            var again = new HistoryStore(FilePath);
            again.Load();
            var kept = Assert.Single(again.Entries);
            Assert.True(kept.Pinned);
            Assert.Equal("SELECT old", kept.Text);
        }

        [Fact]
        public void PinnedRunsComeFirstThenNewestFirst()
        {
            var store = new HistoryStore(FilePath);
            var first = store.Add("SELECT 1", "s", "d", null, Now);
            store.Add("SELECT 2", "s", "d", null, Now.AddMinutes(1));
            store.Add("SELECT 3", "s", "d", null, Now.AddMinutes(2));
            store.SetPinned(new[] { first.Id }, true);

            Assert.Equal(new[] { "SELECT 1", "SELECT 3", "SELECT 2" }, HistoryStore.Filter(store.Entries, string.Empty).Select(e => e.Text).ToArray());
        }

        [Fact]
        public void UnpinningIsRememberedAfterReloading()
        {
            var store = new HistoryStore(FilePath);
            var entry = store.Add("SELECT 1", "s", "d", null, Now);
            store.SetPinned(new[] { entry.Id }, true);
            store.SetPinned(new[] { entry.Id }, false);

            var again = new HistoryStore(FilePath);
            again.Load();

            Assert.False(Assert.Single(again.Entries).Pinned);
        }

        [Fact]
        public void ThePinSurvivesARewriteOfTheFile()
        {
            var store = new HistoryStore(FilePath);
            var keep = store.Add("SELECT keep", "s", "d", null, Now);
            var drop = store.Add("SELECT drop", "s", "d", null, Now);
            store.SetPinned(new[] { keep.Id }, true);
            store.Delete(new[] { drop.Id });

            var again = new HistoryStore(FilePath);
            again.Load();

            Assert.True(Assert.Single(again.Entries).Pinned);
        }
    }
}
