using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The history remembers the file of a tab and decides whether to open the file or the text (#372).</summary>
    public sealed class HistoryFileTests : IDisposable
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterHistoryFile" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void TheFileOfARunSurvivesSavingAndLoading()
        {
            var path = Path.Combine(folder, "history.jsonl");
            var store = new HistoryStore(path);
            store.Add("SELECT 1", "s", "d", null, Now, "report.sql", @"C:\work\report.sql");
            store.Add("SELECT 2", "s", "d", null, Now.AddSeconds(1), "SQLQuery1.sql");

            var loaded = new HistoryStore(path);
            loaded.Load();

            Assert.Equal(@"C:\work\report.sql", loaded.Entries.Single(e => e.Text == "SELECT 1").File);
            Assert.Equal(string.Empty, loaded.Entries.Single(e => e.Text == "SELECT 2").File);
            Assert.DoesNotContain("\"file\":\"\"", File.ReadAllText(path));
        }

        [Fact]
        public void AnOldHistoryFileWithoutTheFieldLoads()
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "history.jsonl");
            File.WriteAllText(path, "{\"id\":\"a\",\"text\":\"SELECT 1\",\"server\":\"s\",\"database\":\"d\",\"ran\":\"2026-11-08T12:00:00.0000000Z\",\"tab\":\"SQLQuery1.sql\"}\n");

            var store = new HistoryStore(path);
            store.Load();

            Assert.Equal(string.Empty, store.Entries.Single().File);
        }

        [Fact]
        public void TheFileOfATabSurvivesAndStaysAfterTheTabIsClosed()
        {
            var path = Path.Combine(folder, "tabs.jsonl");
            var tabs = new TabHistoryStore(path);
            tabs.Track(@"C:\work\report.sql", "report.sql", "s", "d", "SELECT 1", Now, null, @"C:\work\report.sql");
            tabs.Track(@"C:\work\report.sql", "report.sql", "s", "d", "SELECT 1", Now.AddSeconds(5), null, @"C:\work\report.sql");
            tabs.Track(@"C:\Temp\SQLQuery2.sql", "SQLQuery2.sql", "s", "d", "SELECT 2", Now);
            tabs.Close(@"C:\work\report.sql", Now.AddMinutes(1));

            var loaded = new TabHistoryStore(path);
            loaded.Load();
            var entries = loaded.Entries();

            Assert.Equal(@"C:\work\report.sql", entries.Single(e => e.Tab == "report.sql").File);
            Assert.Equal(string.Empty, entries.Single(e => e.Tab == "SQLQuery2.sql").File);
        }

        [Fact]
        public void TheFilterAlsoSearchesThePathOfTheFile()
        {
            var store = new HistoryStore(Path.Combine(folder, "history.jsonl"));
            store.Add("SELECT 1", "s", "d", null, Now, "a.sql", @"C:\projects\billing\a.sql");
            store.Add("SELECT 2", "s", "d", null, Now, "b.sql", @"C:\projects\crm\b.sql");

            var found = HistoryStore.Filter(store.Entries, "billing");

            Assert.Equal(new[] { "SELECT 1" }, found.Select(e => e.Text).ToArray());
        }

        [Fact]
        public void OnlyARealFileIsTheFileOfATab()
        {
            // Paths of this machine, so that the test also runs where the path separator is not a backslash.
            var root = Path.Combine(Path.GetTempPath(), "SqlPrompterFileRule");
            var temp = Path.Combine(root, "Temp");
            var work = Path.Combine(root, "work", "report.sql");
            var temporary = Path.Combine(temp, "SQLQuery3.sql");
            var notes = Path.Combine(temp, "notes.sql");

            Assert.Equal(work, HistoryFile.OfDocument(work, true, temp));
            Assert.Equal(string.Empty, HistoryFile.OfDocument(temporary, true, temp));
            Assert.Equal(string.Empty, HistoryFile.OfDocument(Path.Combine(root, "work", "gone.sql"), false, temp));
            Assert.Equal(string.Empty, HistoryFile.OfDocument("SQLQuery1.sql", true, temp));
            Assert.Equal(string.Empty, HistoryFile.OfDocument(null, true, temp));
            // A saved script that lives in the temp folder under another name is a file.
            Assert.Equal(notes, HistoryFile.OfDocument(notes, true, temp));
        }
        [Fact]
        public void TheDecisionIsFileTextOrBoth()
        {
            Assert.Equal(HistoryOpenAction.OpenText, HistoryFile.Decide(string.Empty, null, "SELECT 1"));
            Assert.Equal(HistoryOpenAction.OpenText, HistoryFile.Decide(null, "SELECT 1", "SELECT 1"));
            Assert.Equal(HistoryOpenAction.OpenFile, HistoryFile.Decide(@"C:\a.sql", "SELECT 1\r\nFROM t\r\n", "SELECT 1\nFROM t"));
            Assert.Equal(HistoryOpenAction.OpenFileOrText, HistoryFile.Decide(@"C:\a.sql", "SELECT 2", "SELECT 1"));
            Assert.Equal(HistoryOpenAction.FileMissingOpenText, HistoryFile.Decide(@"C:\a.sql", null, "SELECT 1"));
        }
    }
}
