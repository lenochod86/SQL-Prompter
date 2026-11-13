using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The databases of the server after ALTER DATABASE, DROP DATABASE and the like (#447).</summary>
    public class DatabaseNameTests
    {
        [Theory]
        [InlineData("ALTER DATABASE |")]
        [InlineData("ALTER DATABASE Cu|")]
        [InlineData("alter database |")]
        [InlineData("ALTER DATABASE [|")]
        [InlineData("ALTER\nDATABASE\n|")]
        [InlineData("-- comment\nALTER DATABASE |")]
        [InlineData("SELECT 1;\nALTER DATABASE |")]
        [InlineData("DROP DATABASE |")]
        [InlineData("DROP DATABASE IF EXISTS |")]
        [InlineData("drop database if exists Sh|")]
        [InlineData("DROP DATABASE a, |")]
        [InlineData("DROP DATABASE IF EXISTS a, |")]
        [InlineData("USE |")]
        [InlineData("BACKUP DATABASE |")]
        [InlineData("RESTORE DATABASE |")]
        [InlineData("DBCC CHECKDB (|")]
        public void ADatabaseNameIsExpected(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.ObjectName, context.Kind);
            Assert.True(context.DatabaseName);
        }

        [Fact]
        public void CurrentIsAcceptedOnlyAfterAlterDatabase()
        {
            Assert.True(Analyze("ALTER DATABASE |").CurrentDatabaseAllowed);
            Assert.False(Analyze("DROP DATABASE |").CurrentDatabaseAllowed);
        }

        [Theory]
        [InlineData("DROP TABLE |")]
        [InlineData("SELECT * FROM |")]
        [InlineData("ALTER TABLE |")]
        public void OtherPlacesAreNotDatabaseNames(string sql) => Assert.False(Analyze(sql).DatabaseName);

        [Fact]
        public void AfterTheDatabaseOfAlterDatabaseTheNextWordsAreOffered()
        {
            var context = Analyze("ALTER DATABASE Shop |");

            Assert.Equal(ContextKind.Keyword, context.Kind);
            Assert.Contains("SET", context.PreferredKeywords);
            Assert.True(context.OnlyExpectedKeywords);
        }

        [Fact]
        public void AfterTheDatabaseOfDropDatabaseNothingIsOffered()
        {
            Assert.Equal(ContextKind.Name, Analyze("DROP DATABASE Shop |").Kind);
        }

        private static DatabaseSnapshot WithDatabases() =>
            new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, new DbObject[0], "v1", databases: new[] { "master", "model", "msdb", "Shop", "tempdb", "Archive" });

        [Fact]
        public void UserDatabasesComeFirstAndTheSystemOnesLast()
        {
            var items = SuggestionBuilder.Build(Analyze("ALTER DATABASE |"), WithDatabases(), null, SuggestionOptions.Default)
                .OrderBy(i => i.Group).ThenBy(i => i.SortText, System.StringComparer.OrdinalIgnoreCase).Select(i => i.DisplayText).ToArray();

            Assert.Equal(new[] { "Archive", "Shop", "master", "model", "msdb", "tempdb", "CURRENT" }, items);
        }

        [Fact]
        public void DropDatabaseOffersTheDatabasesWithoutCurrent()
        {
            var items = SuggestionBuilder.Build(Analyze("DROP DATABASE IF EXISTS |"), WithDatabases(), null, SuggestionOptions.Default).Select(i => i.DisplayText).ToArray();

            Assert.DoesNotContain("CURRENT", items);
            Assert.Contains("Shop", items);
            Assert.DoesNotContain("SELECT", items);
        }

        [Fact]
        public void BracketedAndSpacedNamesAreQuoted()
        {
            var snapshot = new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, new DbObject[0], "v1", databases: new[] { "My Shop" });

            var items = SuggestionBuilder.Build(Analyze("DROP DATABASE |"), snapshot, null, SuggestionOptions.Default);

            Assert.Equal("My Shop", items.Single().DisplayText);
        }

        [Fact]
        public void SnippetsAreNotOfferedWhereOnlyADatabaseGoes()
        {
            var options = SuggestionOptions.Default.WithSnippets(new[] { SqlPrompter.Core.Snippets.Snippet.Create("ssf", "select", "SELECT 1", new SqlPrompter.Core.Snippets.SnippetPlaceholder[0]) });

            var items = SuggestionBuilder.Build(Analyze("DROP DATABASE |"), WithDatabases(), null, options);

            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Snippet);
        }

        [Fact]
        public void NoMetadataGivesAnEmptyList()
        {
            Assert.Empty(SuggestionBuilder.Build(Analyze("DROP DATABASE |"), null, null, SuggestionOptions.Default));
        }
    }
}
