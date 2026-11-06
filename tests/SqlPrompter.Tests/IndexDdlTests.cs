using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>Smart suggestions in CREATE / ALTER / DROP INDEX (#197).</summary>
    public class IndexDdlTests
    {
        private static IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), SampleDatabase());

        /// <summary>The keywords of the first keyword group of the list.</summary>
        private static string[] First(string sql)
        {
            var keywords = Items(sql).Where(i => i.Kind == SuggestionKind.Keyword).ToList();
            var group = keywords.Min(k => k.Group);
            return keywords.Where(k => k.Group == group).Select(k => k.InsertText).ToArray();
        }

        private static void AssertFirst(string sql, params string[] expected)
        {
            var first = First(sql);
            Assert.True(first.Length < 40, "nothing is preferred at this position");
            foreach (var keyword in expected)
            {
                Assert.Contains(first, f => string.Equals(f, keyword, System.StringComparison.OrdinalIgnoreCase));
            }
        }

        [Theory]
        [InlineData("CREATE UNIQUE |", "CLUSTERED", "NONCLUSTERED", "INDEX")]
        [InlineData("CREATE NONCLUSTERED |", "INDEX", "COLUMNSTORE")]
        [InlineData("CREATE CLUSTERED |", "INDEX", "COLUMNSTORE")]
        [InlineData("CREATE COLUMNSTORE |", "INDEX")]
        [InlineData("CREATE CLUSTERED COLUMNSTORE |", "INDEX")]
        [InlineData("CREATE INDEX ix |", "ON")]
        [InlineData("CREATE UNIQUE NONCLUSTERED INDEX ix |", "ON")]
        [InlineData("CREATE NONCLUSTERED COLUMNSTORE INDEX [ix] |", "ON")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) |", "INCLUDE", "WHERE", "WITH", "ON")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id ASC) inc|", "INCLUDE", "WHERE", "WITH", "ON")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) INCLUDE (Name) |", "WHERE", "WITH", "ON")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) WITH (FILLFACTOR = 80) |", "ON")]
        [InlineData("CREATE CLUSTERED COLUMNSTORE INDEX ix ON dbo.Users |", "WITH", "ON")]
        [InlineData("ALTER INDEX ix ON dbo.Users |", "REBUILD", "REORGANIZE", "DISABLE", "SET")]
        [InlineData("ALTER INDEX ALL ON dbo.Users |", "REBUILD", "REORGANIZE", "DISABLE", "SET")]
        [InlineData("ALTER INDEX ix |", "ON")]
        [InlineData("ALTER INDEX ix ON dbo.Users REBUILD |", "WITH", "PARTITION")]
        [InlineData("DROP INDEX ix |", "ON")]
        [InlineData("DROP INDEX IF EXISTS ix |", "ON")]
        [InlineData("DROP INDEX ix ON dbo.Users |", "WITH")]
        public void KeywordsFitThePosition(string sql, params string[] expected)
        {
            AssertFirst(sql, expected);
        }

        [Fact]
        public void CreateOffersTheIndexModifiers()
        {
            var keywords = Items("CREATE |").Where(i => i.Kind == SuggestionKind.Keyword).Select(i => i.InsertText).ToList();
            foreach (var keyword in new[] { "INDEX", "UNIQUE", "CLUSTERED", "NONCLUSTERED", "COLUMNSTORE" })
            {
                Assert.Contains(keyword, keywords);
            }
        }

        [Fact]
        public void IncludeIsFirstAndOtherKeywordsStayInTheList()
        {
            var items = Items("CREATE NONCLUSTERED INDEX ix ON dbo.Users (Id ASC) In|").Where(i => i.Kind == SuggestionKind.Keyword).OrderBy(i => i.Group).ToList();

            Assert.Equal("INCLUDE", items[0].InsertText, true);
            Assert.Contains(items, i => i.InsertText.StartsWith("INSERT", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void KeyColumnsOfferAscAndDesc()
        {
            AssertFirst("CREATE INDEX ix ON dbo.Users (Id |", "ASC", "DESC");
            AssertFirst("CREATE INDEX ix ON dbo.Users (Id, Name |", "ASC", "DESC");
        }

        [Theory]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) WITH (|", "FILLFACTOR", "ONLINE", "MAXDOP", "DATA_COMPRESSION", "XML_COMPRESSION", "PAD_INDEX")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) WITH (PAD_INDEX = ON, |", "SORT_IN_TEMPDB", "OPTIMIZE_FOR_SEQUENTIAL_KEY")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) WITH (ONLINE = |", "ON", "OFF")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) WITH (DATA_COMPRESSION = |", "NONE", "ROW", "PAGE")]
        [InlineData("ALTER INDEX ix ON dbo.Users REBUILD WITH (|", "FILLFACTOR", "ONLINE")]
        public void WithOptions(string sql, params string[] expected)
        {
            AssertFirst(sql, expected);
        }

        [Fact]
        public void OnOffersTablesAndViews()
        {
            foreach (var sql in new[] { "CREATE INDEX ix ON |", "CREATE UNIQUE CLUSTERED INDEX ix ON |", "ALTER INDEX ix ON |", "DROP INDEX ix ON |" })
            {
                var context = Analyze(sql);
                Assert.Equal(ContextKind.ObjectName, context.Kind);
                var items = SuggestionBuilder.Build(context, SampleDatabase());
                Assert.Contains(items, i => i.Kind == SuggestionKind.Table && i.InsertText == "dbo.Users");
                Assert.Contains(items, i => i.Kind == SuggestionKind.View);
            }
        }

        [Fact]
        public void FilegroupOnAfterTheColumnListIsNoObjectName()
        {
            Assert.NotEqual(ContextKind.ObjectName, Analyze("CREATE INDEX ix ON dbo.Users (Id) ON |").Kind);
        }

        [Theory]
        [InlineData("CREATE INDEX ix ON dbo.Users (|", "Id", "Name")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id, |", "Name")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) INCLUDE (|", "Name")]
        public void ColumnsOfTheTable(string sql, params string[] expected)
        {
            var items = Items(sql);
            foreach (var column in expected)
            {
                Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.InsertText == column);
            }
        }

        [Fact]
        public void KeyColumnsAreNotOfferedAgain()
        {
            Assert.DoesNotContain(Items("CREATE INDEX ix ON dbo.Users (Id, |"), i => i.Kind == SuggestionKind.Column && i.InsertText == "Id");
            Assert.DoesNotContain(Items("CREATE INDEX ix ON dbo.Users (Id) INCLUDE (|"), i => i.Kind == SuggestionKind.Column && i.InsertText == "Id");
        }

        [Theory]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) WHERE |")]
        [InlineData("CREATE INDEX ix ON dbo.Users (Id) INCLUDE (Email) WHERE Name = 'a' AND |")]
        [InlineData("CREATE UNIQUE NONCLUSTERED INDEX ix ON dbo.Users (Id) WHERE Em|")]
        public void TheFilterPredicateOffersTheColumnsOfTheTable(string sql)
        {
            var items = Items(sql);
            foreach (var column in new[] { "Id", "Name", "Email" })
            {
                Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.InsertText == column);
            }
        }

        private static ColumnPicker Picker(string marked)
        {
            var caret = marked.IndexOf('|');
            var sql = marked.Remove(caret, 1);
            var context = ContextAnalyzer.Analyze(sql, caret);
            return ColumnPicker.TryBuild(sql, caret, context, SampleDatabase(), null, SuggestionOptions.Default);
        }

        [Fact]
        public void ThePickerOffersAllColumnsOfTheKeyListWithoutThoseListed()
        {
            var picker = Picker("CREATE INDEX ix ON dbo.Users (Id, |");

            Assert.Equal(new[] { "Name", "Email" }, picker.Columns.Select(c => c.InsertText).ToArray());
        }

        [Fact]
        public void ThePickerOfIncludeLeavesOutTheKeyColumns()
        {
            var picker = Picker("CREATE INDEX ix ON dbo.Users (Id) INCLUDE (|");

            Assert.Equal(new[] { "Name", "Email" }, picker.Columns.Select(c => c.InsertText).ToArray());
        }
    }
}
