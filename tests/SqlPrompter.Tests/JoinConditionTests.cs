using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class JoinConditionTests
    {
        private static IReadOnlyList<DbColumn> Cols(params (string Name, string Type)[] columns) =>
            columns.Select(c => new DbColumn(c.Name, c.Type, false)).ToList();

        /// <summary>Users(IdUser) ← Orders(IdOrder, IdUser, Note) ← OrderLines(IdOrder, IdLine, Note: varchar) with a two-column key to Shipments.</summary>
        private static DatabaseSnapshot Shop()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(3, "dbo", "OrderLines", DbObjectKind.Table, false),
                new DbObject(4, "dbo", "Shipments", DbObjectKind.Table, false),
            };
            var keys = new[]
            {
                new DbForeignKey("FK_Orders_Users", 2, 1, new[] { new DbForeignKeyColumn("IdUser", "IdUser") }),
                new DbForeignKey("FK_OrderLines_Orders", 3, 2, new[] { new DbForeignKeyColumn("IdOrder", "IdOrder") }),
                new DbForeignKey("FK_Shipments_OrderLines", 4, 3, new[] { new DbForeignKeyColumn("IdOrder", "IdOrder"), new DbForeignKeyColumn("IdLine", "IdLine") }),
            };
            return new DatabaseSnapshot("shop", "dbo", new[] { "dbo" }, objects, "v1", null, null, null, keys,
                    new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "IdUser" }, [2] = new[] { "IdOrder" }, [3] = new[] { "IdOrder", "IdLine" } })
                .WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
                {
                    [1] = Cols(("IdUser", "int"), ("Name", "nvarchar(50)")),
                    [2] = Cols(("IdOrder", "int"), ("IdUser", "int"), ("Note", "nvarchar(50)")),
                    [3] = Cols(("IdOrder", "int"), ("IdLine", "int"), ("Note", "varchar(20)")),
                    [4] = Cols(("IdShipment", "int"), ("IdOrder", "int"), ("IdLine", "int")),
                });
        }

        private static SuggestionOptions Options(JoinSettings joins = null) => new SuggestionOptions(KeywordCase.MatchTyped, null, null, joins ?? new JoinSettings { NewTableOnLeft = true });

        private static IReadOnlyList<Suggestion> Conditions(string sql, JoinSettings joins = null) =>
            SuggestionBuilder.Build(Analyze(sql), Shop(), null, Options(joins)).Where(i => i.Kind == SuggestionKind.JoinCondition).OrderBy(i => i.SortText).ToList();

        [Theory]
        [InlineData("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON |")]
        [InlineData("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON o.IdUser = u.IdUser AND |")]
        [InlineData("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON 1 = 1 OR |")]
        [InlineData("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON i|")]
        public void ContextKnowsTheTableOfTheJoin(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal("Orders", context.JoinTarget.ObjectName);
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON o.IdUser = |")]
        [InlineData("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON o.|")]
        [InlineData("SELECT * FROM dbo.Users u JOIN dbo.Orders o WHERE |")]
        [InlineData("SELECT * FROM dbo.Users u WHERE |")]
        public void NoTargetElsewhere(string sql)
        {
            Assert.Null(Analyze(sql).JoinTarget);
        }

        [Fact]
        public void TheTargetIsTheTableOfTheJoinBeingEdited()
        {
            var context = Analyze("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON | JOIN dbo.OrderLines ol ON ol.IdOrder = o.IdOrder");

            Assert.Equal("Orders", context.JoinTarget.ObjectName);
        }

        [Fact]
        public void ForeignKeyConditionComesFirst()
        {
            var items = Conditions("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON |");

            var first = items.First();
            Assert.Equal("o.IdUser = u.IdUser", first.InsertText);
            Assert.Equal("FK_Orders_Users", first.Suffix);
        }

        [Fact]
        public void ConditionsRankBeforeEverythingElse()
        {
            var all = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON |"), Shop(), null, Options()).OrderBy(i => i.SortText).ToList();

            Assert.Equal(SuggestionKind.JoinCondition, all[0].Kind);
        }

        [Fact]
        public void MultiColumnKeyGivesTheWholeConditionAndEachColumn()
        {
            var items = Conditions("SELECT * FROM dbo.OrderLines ol JOIN dbo.Shipments s ON |");

            Assert.Equal("s.IdOrder = ol.IdOrder AND s.IdLine = ol.IdLine", items[0].InsertText);
            Assert.Contains(items, i => i.InsertText == "s.IdOrder = ol.IdOrder" && i.Suffix == "FK_Shipments_OrderLines");
            Assert.Contains(items, i => i.InsertText == "s.IdLine = ol.IdLine");
        }

        [Fact]
        public void SingleColumnsCanBeTurnedOff()
        {
            var items = Conditions("SELECT * FROM dbo.OrderLines ol JOIN dbo.Shipments s ON |", new JoinSettings { NewTableOnLeft = true, IncludePartialKeyConditions = false });

            Assert.Equal(new[] { "s.IdOrder = ol.IdOrder AND s.IdLine = ol.IdLine" }, items.Where(i => i.Suffix == "FK_Shipments_OrderLines").Select(i => i.InsertText).ToArray());
        }

        [Fact]
        public void ByDefaultTheNewTableIsOnTheRightLikeInTheUsersSqlPromptSettings()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON |"), Shop(), null, new SuggestionOptions(KeywordCase.MatchTyped, null))
                .Where(i => i.Kind == SuggestionKind.JoinCondition).OrderBy(i => i.SortText).ToList();

            Assert.Equal("u.IdUser = o.IdUser", items.First().InsertText);
        }

        [Fact]
        public void ColumnOrderFollowsTheOption()
        {
            var right = Conditions("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON |", new JoinSettings { NewTableOnLeft = false });

            Assert.Equal("u.IdUser = o.IdUser", right.First().InsertText);
        }

        [Fact]
        public void ColumnsWithTheSameNameAreOffered()
        {
            var items = Conditions("SELECT * FROM dbo.Orders o JOIN dbo.OrderLines ol ON |");

            // The key IdOrder is covered by the foreign key; Note exists in both and is only a name match.
            Assert.Equal("ol.IdOrder = o.IdOrder", items.First().InsertText);
            Assert.Contains(items, i => i.InsertText == "ol.Note = o.Note" && i.Suffix == "same name");
        }

        [Fact]
        public void NameMatchesCanBeTurnedOffOrNeedTheSameType()
        {
            var off = Conditions("SELECT * FROM dbo.Orders o JOIN dbo.OrderLines ol ON |", new JoinSettings { NewTableOnLeft = true, NameMatchedConditions = false });
            var typed = Conditions("SELECT * FROM dbo.Orders o JOIN dbo.OrderLines ol ON |", new JoinSettings { NewTableOnLeft = true, RequireSameTypeForNameMatches = true });

            Assert.DoesNotContain(off, i => i.Suffix == "same name");
            Assert.DoesNotContain(typed, i => i.Suffix == "same name");
            Assert.Single(off);
        }

        [Fact]
        public void ForeignKeyConditionsCanBeTurnedOff()
        {
            var sql = "SELECT * FROM dbo.Orders o JOIN dbo.OrderLines ol ON |";
            var on = Conditions(sql, new JoinSettings { NewTableOnLeft = true });
            var off = Conditions(sql, new JoinSettings { NewTableOnLeft = true, ForeignKeyConditions = false });
            var onlyNames = Conditions(sql, new JoinSettings { NewTableOnLeft = true, ForeignKeyConditions = false, NameMatchedConditions = false });

            Assert.Contains(on, i => i.Suffix != "same name");
            Assert.DoesNotContain(off, i => i.Suffix != "same name");
            Assert.Contains(off, i => i.Suffix == "same name");
            Assert.Empty(onlyNames);
        }

        [Fact]
        public void NameMatchesOfKeyColumnsComeBeforeOtherNames()
        {
            // Orders and Shipments share no foreign key; IdOrder is a key column in Orders, Note is not a key anywhere.
            var objects = new[]
            {
                new DbObject(1, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Audit", DbObjectKind.Table, false),
            };
            var db = new DatabaseSnapshot("d", "dbo", new[] { "dbo" }, objects, "v1", null, null, null, null,
                    new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "IdOrder" } })
                .WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
                {
                    [1] = Cols(("Note", "int"), ("IdOrder", "int")),
                    [2] = Cols(("Note", "int"), ("IdOrder", "int")),
                });

            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Orders o JOIN dbo.Audit a ON |"), db, null, Options())
                .Where(i => i.Kind == SuggestionKind.JoinCondition).OrderBy(i => i.SortText).Select(i => i.InsertText).ToList();

            Assert.Equal(new[] { "a.IdOrder = o.IdOrder", "a.Note = o.Note" }, items);
        }

        [Fact]
        public void EveryEarlierTableIsLinked()
        {
            var items = Conditions("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON o.IdUser = u.IdUser JOIN dbo.OrderLines ol ON |");

            Assert.Contains(items, i => i.InsertText == "ol.IdOrder = o.IdOrder");
            Assert.DoesNotContain(items, i => i.InsertText.Contains("u."));
        }

        [Fact]
        public void NothingWhileColumnsAreNotLoaded()
        {
            var db = new DatabaseSnapshot("d", "dbo", new[] { "dbo" }, Shop().Objects, "v1", null, null, null, Shop().ForeignKeys);

            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Orders o JOIN dbo.OrderLines ol ON |"), db, null, Options());

            // The foreign key does not need columns; the name matches do.
            Assert.Single(items, i => i.Kind == SuggestionKind.JoinCondition);
        }

        [Fact]
        public void ImportsTheJoinOptionsFromASqlPromptExport()
        {
            const string engine =
                "<EngineOptions version=\"1\" type=\"EngineOptions\">" +
                "<IncludePartialFKJoinConditions>false</IncludePartialFKJoinConditions>" +
                "<AlwaysIncludeNameMatchedJoinConditions>false</AlwaysIncludeNameMatchedJoinConditions>" +
                "<FlipColumnsOnJoinCondition>false</FlipColumnsOnJoinCondition>" +
                "</EngineOptions>";
            byte[] zip;
            using (var stream = new System.IO.MemoryStream())
            {
                using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
                using (var writer = new System.IO.StreamWriter(archive.CreateEntry("RedGate_SqlPrompt_Engine_EngineOptions.xml").Open()))
                {
                    writer.Write(engine);
                }

                zip = stream.ToArray();
            }

            var joins = SettingsImporter.ImportSqlPrompt(new System.IO.MemoryStream(zip), new PrompterSettings()).Settings.Joins;

            Assert.False(joins.IncludePartialKeyConditions);
            Assert.False(joins.NameMatchedConditions);
            Assert.True(joins.NewTableOnLeft);
            Assert.False(new PrompterSettings().Joins.NewTableOnLeft);
        }
    }
}
