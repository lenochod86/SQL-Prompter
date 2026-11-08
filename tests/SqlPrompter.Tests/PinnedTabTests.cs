using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>A pinned tab is pinned again when the tabs are restored.</summary>
    public sealed class PinnedTabTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterPinned" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void ThePinnedStateOfATabSurvivesSavingAndLoading()
        {
            var path = Path.Combine(folder, "tabs.json");
            var store = new TabSessionStore(path);
            store.Save(new[] { new SavedTab { Text = "SELECT 1", Pinned = true }, new SavedTab { Text = "SELECT 2" } });

            var loaded = new TabSessionStore(path).Load();

            Assert.True(loaded.Single(t => t.Text == "SELECT 1").Pinned);
            Assert.False(loaded.Single(t => t.Text == "SELECT 2").Pinned);
        }

        [Fact]
        public void AnOldSessionFileWithoutTheFieldLoadsAndAnUnpinnedTabWritesNothing()
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "tabs.json");
            File.WriteAllText(path, "{\"version\":1,\"tabs\":[{\"file\":\"\",\"text\":\"SELECT 1\",\"active\":false}]}");

            var loaded = new TabSessionStore(path).Load();
            Assert.False(loaded.Single().Pinned);

            new TabSessionStore(path).Save(loaded);
            Assert.DoesNotContain("pinned", File.ReadAllText(path));
        }
    }
}
