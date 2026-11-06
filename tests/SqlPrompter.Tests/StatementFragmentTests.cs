using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>INSERT ... VALUES / SELECT and the MERGE clauses offered as whole fragments (#205).</summary>
    public class StatementFragmentTests
    {
        private static DatabaseSnapshot Database()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Staging", DbObjectKind.Table, false),
            };
            return new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, objects, "v1", primaryKeys: new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "Id" } })
                .WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
                {
                    [1] = new[]
                    {
                        new DbColumn("Id", "int", false, isIdentity: true),
                        new DbColumn("Name", "nvarchar(50)", false),
                        new DbColumn("Email", "varchar(100)", true),
                        new DbColumn("Total", "int", false, isComputed: true),
                    },
                    [2] = new[]
                    {
                        new DbColumn("Id", "int", false),
                        new DbColumn("Name", "nvarchar(50)", false),
                        new DbColumn("Email", "varchar(100)", true),
                        new DbColumn("Total", "int", false),
                        new DbColumn("Extra", "int", false),
                    },
                });
        }

        private static IReadOnlyList<Suggestion> Items(string sql, SuggestionOptions options = null) =>
            SuggestionBuilder.Build(Analyze(sql), Database(), null, options);

        private static readonly SuggestionOptions OneLine = new SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.MatchTyped, new SqlPrompter.Core.Settings.AliasSettings(),
            inserts: new SqlPrompter.Core.Settings.InsertSettings { Layout = SqlPrompter.Core.Settings.ListLayout.OneLine });

        private static Suggestion Item(string sql, string filter, SuggestionOptions options = null) => Items(sql, options).First(i => i.FilterText == filter && i.IsBlock);

        [Fact]
        public void InsertValuesHasOneValuePerListedColumnByType()
        {
            var item = Item("INSERT INTO dbo.Users (Name, Email) |", "VALUES", OneLine);

            Assert.Equal("VALUES (N'', NULL)", item.InsertText);
            Assert.True(item.IsBlock);
        }

        [Fact]
        public void InsertValuesFollowsTheLayoutOfTheStyle()
        {
            var options = new SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.MatchTyped, new SqlPrompter.Core.Settings.AliasSettings(),
                inserts: new SqlPrompter.Core.Settings.InsertSettings { Layout = SqlPrompter.Core.Settings.ListLayout.EachOnItsOwnLine },
                editor: new EditorLayout { NewLine = "\n" });

            Assert.Equal("VALUES\n(\n    N'',\n    NULL\n)", Item("INSERT INTO dbo.Users (Name, Email) |", "VALUES", options).InsertText);
        }

        [Fact]
        public void InsertSelectListsTheColumnsAndAddsFrom()
        {
            var item = Item("INSERT INTO dbo.Users (Name, Email) |", "SELECT");

            Assert.Equal("SELECT Name,\r\n       Email\r\nFROM ", item.InsertText);
        }

        [Fact]
        public void NoFragmentsWithoutAColumnList()
        {
            Assert.DoesNotContain(Items("INSERT INTO dbo.Users |"), i => i.IsBlock && (i.FilterText == "VALUES" || i.FilterText == "SELECT"));
        }

        private const string MergeHead = "MERGE INTO dbo.Users AS t USING dbo.Staging AS s ";

        [Fact]
        public void MergeOffersTheOnConditionFromTheSharedPrimaryKey()
        {
            var items = Items(MergeHead + "|");

            Assert.Contains(items, i => i.Kind == SuggestionKind.JoinCondition && i.InsertText == "ON t.Id = s.Id");
        }

        [Fact]
        public void MergeMatchedUpdatesTheSharedColumnsExceptTheKeyAndTheGenerated()
        {
            var item = Item(MergeHead + "ON t.Id = s.Id |", "WHEN MATCHED");

            Assert.Equal("WHEN MATCHED THEN\r\n    UPDATE SET t.Name = s.Name,\r\n               t.Email = s.Email", item.InsertText);
        }

        [Fact]
        public void MergeNotMatchedInsertsTheSharedInsertableColumns()
        {
            var item = Item(MergeHead + "ON t.Id = s.Id |", "WHEN NOT MATCHED", OneLine);

            Assert.Equal("WHEN NOT MATCHED THEN\r\n    INSERT (Name, Email)\r\n    VALUES (s.Name, s.Email)", item.InsertText);
        }

        [Fact]
        public void MergeNotMatchedBySourceDeletes()
        {
            Assert.Equal("WHEN NOT MATCHED BY SOURCE THEN DELETE", Item(MergeHead + "ON t.Id = s.Id |", "WHEN NOT MATCHED BY SOURCE").InsertText);
        }

        [Fact]
        public void TheClausesAreOfferedAfterAnActionToo()
        {
            Assert.Contains(Items(MergeHead + "ON t.Id = s.Id WHEN MATCHED THEN DELETE |"), i => i.IsBlock && i.FilterText == "WHEN NOT MATCHED");
        }
    }
}
