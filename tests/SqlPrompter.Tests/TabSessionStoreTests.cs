using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.History;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public sealed class TabSessionStoreTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "SqlPrompterTabs" + Guid.NewGuid().ToString("N"));

        private string FilePath => Path.Combine(folder, "tabs.json");

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        private static SavedTab Tab(string text, string server = "srv", string login = null, bool active = false, string file = "") =>
            new SavedTab { Text = text, Server = server, Database = "db", Login = login, Active = active, File = file };

        [Fact]
        public void SavedTabsComeBackWithTheirTextAndConnection()
        {
            new TabSessionStore(FilePath).Save(new[] { Tab("SELECT 1\r\nGO\r\nSELECT 'ž'", login: "sa", active: true), Tab("SELECT 2", file: @"C:\x\a.sql") });

            var tabs = new TabSessionStore(FilePath).Load();

            Assert.Equal(2, tabs.Count);
            Assert.Equal("SELECT 1\r\nGO\r\nSELECT 'ž'", tabs[0].Text);
            Assert.Equal("sa", tabs[0].Login);
            Assert.True(tabs[0].Active);
            Assert.Equal("db", tabs[0].Database);
            Assert.Equal(@"C:\x\a.sql", tabs[1].File);
            Assert.Null(tabs[1].Login);
        }

        [Fact]
        public void ANewSessionReplacesTheOldOne()
        {
            var store = new TabSessionStore(FilePath);
            store.Save(new[] { Tab("one"), Tab("two") });
            store.Save(new[] { Tab("three") });

            Assert.Equal("three", Assert.Single(new TabSessionStore(FilePath).Load()).Text);
            Assert.False(File.Exists(FilePath + ".tmp"));
        }

        [Fact]
        public void UnchangedTabsAreNotWrittenAgain()
        {
            var store = new TabSessionStore(FilePath);

            Assert.True(store.Save(new[] { Tab("one") }));
            Assert.False(store.Save(new[] { Tab("one") }));
            Assert.True(store.Save(new[] { Tab("one!") }));
        }

        [Fact]
        public void ClosingAllTabsIsSaved()
        {
            var store = new TabSessionStore(FilePath);
            store.Save(new[] { Tab("one") });
            store.Save(new SavedTab[0]);

            Assert.Empty(new TabSessionStore(FilePath).Load());
        }

        [Fact]
        public void AMissingOrBrokenFileGivesNoTabs()
        {
            Assert.Empty(new TabSessionStore(FilePath).Load());

            Directory.CreateDirectory(folder);
            File.WriteAllText(FilePath, "{ not json");

            Assert.Empty(new TabSessionStore(FilePath).Load());
        }

        [Fact]
        public void ATabWithoutAServerIsNotConnected()
        {
            Assert.False(Tab("x", server: null).IsConnected);
            Assert.True(Tab("x").IsConnected);
        }

        [Fact]
        public void RestoringTabsIsOnByDefault()
        {
            Assert.True(new BehaviorSettings().RestoreTabs);
        }
    }
}
