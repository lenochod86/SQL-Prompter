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
    public class InsertAndExecTests
    {
        private static readonly EditorLayout Editor = new EditorLayout { NewLine = "\n", LineWidth = 40 };

        private static DatabaseSnapshot Database()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Plain", DbObjectKind.Table, false),
            };

            var procedures = new[]
            {
                new DbProcedure(10, "dbo", "AddOrder", new[]
                {
                    new DbParameter("@IdCustomer", "int", false, false),
                    new DbParameter("@Note", "nvarchar(50)", false, true, "NULL"),
                    new DbParameter("@Qty", "int", false, true, "1"),
                    new DbParameter("@IdOrder", "int", true, false),
                }),
                new DbProcedure(11, "dbo", "Cleanup", new DbParameter[0]),
                new DbProcedure(12, "sales", "Report", new[] { new DbParameter("@Year", "int", false, false) }),
            };

            var snapshot = new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales" }, objects, "v1", procedures: procedures);
            return snapshot.WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
            {
                [1] = new List<DbColumn>
                {
                    new DbColumn("IdOrders", "int", false, isIdentity: true),
                    new DbColumn("IdCustomers", "int", false),
                    new DbColumn("Note", "nvarchar(50)", true),
                    new DbColumn("Code", "varchar(10)", false),
                    new DbColumn("Total", "money", false),
                    new DbColumn("Created", "datetime", false),
                    new DbColumn("Computed", "int", false, isComputed: true),
                    new DbColumn("RowVer", "timestamp", false, isRowVersion: true),
                },
                [2] = new List<DbColumn> { new DbColumn("A", "int", false), new DbColumn("B", "int", false) },
            });
        }

        private static IReadOnlyList<Suggestion> Build(string sql, InsertSettings inserts = null, EditorLayout editor = null) =>
            SuggestionBuilder.Build(Analyze(sql), Database(), null, new SuggestionOptions(KeywordCase.MatchTyped, new AliasSettings(), null, null, null, inserts, editor ?? Editor));

        private static InsertSettings Settings(InsertColumnsMode columns = InsertColumnsMode.Automatic, ListLayout layout = ListLayout.EachOnItsOwnLine, InsertValuesMode values = InsertValuesMode.None) =>
            new InsertSettings { ColumnsMode = columns, Layout = layout, ValuesMode = values };

        [Theory]
        [InlineData("INSERT INTO |")]
        [InlineData("INSERT |")]
        public void TheTableAfterInsertIsAnInsertTarget(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.ObjectName, context.Kind);
            Assert.True(context.InsertTarget);
        }

        [Fact]
        public void OtherTableNamesAreNotInsertTargets()
        {
            Assert.False(Analyze("SELECT * FROM |").InsertTarget);
            Assert.False(Analyze("MERGE INTO |").InsertTarget);
            Assert.False(Analyze("SELECT 1 INTO |").InsertTarget);
        }

        [Fact]
        public void ColumnListAfterTheTableName()
        {
            var first = Analyze("INSERT INTO dbo.Orders (|");
            Assert.Equal(ContextKind.InsertColumns, first.Kind);
            Assert.True(first.FirstInsertColumn);
            Assert.Equal(new[] { "dbo", "Orders" }, first.Sources.Single().NameParts);

            var later = Analyze("INSERT INTO Orders (IdCustomers, |");
            Assert.Equal(ContextKind.InsertColumns, later.Kind);
            Assert.False(later.FirstInsertColumn);

            Assert.Equal(ContextKind.InsertColumns, Analyze("INSERT Orders(Id|").Kind);
        }

        [Fact]
        public void ParenthesesOfOtherStatementsAreNotColumnLists()
        {
            Assert.NotEqual(ContextKind.InsertColumns, Analyze("INSERT INTO Orders VALUES (|").Kind);
            Assert.NotEqual(ContextKind.InsertColumns, Analyze("SELECT * FROM Orders WHERE Id IN (|").Kind);
            Assert.NotEqual(ContextKind.InsertColumns, Analyze("INSERT INTO Orders (Id) SELECT (|").Kind);
        }

        [Fact]
        public void TheTableAfterInsertIntoComesWithItsInsertableColumnsOnSeparateLines()
        {
            var items = Build("INSERT INTO |");

            var orders = items.Single(i => i.DisplayText == "Orders");
            Assert.Equal("dbo.Orders\n(\n    IdCustomers,\n    Note,\n    Code,\n    Total,\n    Created\n)\n", orders.InsertText);
            Assert.True(orders.IsBlock);
        }

        [Fact]
        public void ColumnsAreLaidOutInOneLine()
        {
            var orders = Build("INSERT INTO |", Settings(layout: ListLayout.OneLine)).Single(i => i.DisplayText == "Orders");

            Assert.Equal("dbo.Orders (IdCustomers, Note, Code, Total, Created)\n", orders.InsertText);
        }

        [Fact]
        public void ColumnsFitTheWidthOfTheEditor()
        {
            var orders = Build("INSERT INTO |", Settings(layout: ListLayout.FitWidth)).Single(i => i.DisplayText == "Orders");

            Assert.Equal("dbo.Orders (IdCustomers, Note, Code,\n    Total, Created)\n", orders.InsertText);
        }

        [Fact]
        public void ValuesHaveAPlaceholderPerColumnByType()
        {
            var orders = Build("INSERT INTO |", Settings(values: InsertValuesMode.Values)).Single(i => i.DisplayText == "Orders");

            Assert.Equal(
                "dbo.Orders\n(\n    IdCustomers,\n    Note,\n    Code,\n    Total,\n    Created\n)\nVALUES\n(\n    0,\n    NULL,\n    '',\n    0,\n    '1900-01-01'\n)",
                orders.InsertText);
        }

        [Fact]
        public void OneLineValues()
        {
            var plain = Build("INSERT INTO |", Settings(layout: ListLayout.OneLine, values: InsertValuesMode.Values)).Single(i => i.DisplayText == "Plain");

            Assert.Equal("dbo.Plain (A, B)\nVALUES (0, 0)", plain.InsertText);
        }

        [Fact]
        public void NewLinesFollowTheIndentOfTheLine()
        {
            var editor = new EditorLayout { NewLine = "\n", LineIndent = "  ", LinePrefix = "  INSERT INTO " };
            var plain = Build("INSERT INTO |", Settings(), editor).Single(i => i.DisplayText == "Plain");

            Assert.Equal("dbo.Plain\n  (\n      A,\n      B\n  )\n  ", plain.InsertText);
        }

        [Fact]
        public void ItemModeKeepsTheTableAndAddsTheOneWithColumns()
        {
            var items = Build("INSERT INTO |", Settings(InsertColumnsMode.Item)).Where(i => i.Kind == SuggestionKind.Table && i.DisplayText.StartsWith("Orders")).ToList();

            Assert.Equal(2, items.Count);
            Assert.Equal("dbo.Orders", items.Single(i => i.DisplayText == "Orders").InsertText);
            var all = items.Single(i => i.DisplayText == "Orders (columns)");
            Assert.StartsWith("dbo.Orders\n(\n    IdCustomers", all.InsertText);
            Assert.Equal("Orders", all.FilterText);
        }

        [Fact]
        public void ParenthesisModeInsertsPlainTablesAndOffersAllColumnsInsideTheParentheses()
        {
            var settings = Settings(InsertColumnsMode.AfterParenthesis, ListLayout.OneLine);

            var orders = Build("INSERT INTO |", settings).Single(i => i.DisplayText == "Orders");
            Assert.Equal("dbo.Orders", orders.InsertText);

            var items = Build("INSERT INTO dbo.Orders (|", settings);
            var all = items.Single(i => i.DisplayText == "(all columns)");
            Assert.Equal("IdCustomers, Note, Code, Total, Created)\n", all.InsertText);
            Assert.Equal(new[] { "IdCustomers", "Note", "Code", "Total", "Created" }, items.Where(i => i.Kind == SuggestionKind.Column && i != all).Select(i => i.DisplayText).ToArray());
        }

        [Fact]
        public void InsideTheParenthesesOnlyInsertableColumnsAreOffered()
        {
            var items = Build("INSERT INTO dbo.Orders (IdCustomers, |");

            Assert.DoesNotContain(items, i => i.DisplayText == "IdOrders" || i.DisplayText == "Computed" || i.DisplayText == "RowVer" || i.DisplayText == "(all columns)");
            Assert.Contains(items, i => i.DisplayText == "Total");
        }

        [Fact]
        public void ProceduresAreOfferedAfterExec()
        {
            foreach (var sql in new[] { "EXEC |", "EXECUTE |", "EXEC @r = |" })
            {
                var context = Analyze(sql);
                Assert.True(context.ProcedureName, sql);
                var items = SuggestionBuilder.Build(context, Database());
                Assert.Contains(items, i => i.Kind == SuggestionKind.Procedure && i.DisplayText == "Cleanup");
                Assert.DoesNotContain(items, i => i.DisplayText == "Orders");
            }
        }

        [Fact]
        public void ProcedureNameWithSchemaListsOnlyThatSchema()
        {
            var items = Build("EXEC sales.|");

            var report = items.Single(i => i.Kind == SuggestionKind.Procedure);
            Assert.Equal("Report", report.DisplayText);
            Assert.Equal("Report @Year = 0", report.InsertText);
        }

        [Fact]
        public void ParametersAreInsertedNamedAndAlignedWithTheFirstOne()
        {
            var editor = new EditorLayout { NewLine = "\n", LinePrefix = "EXEC " };
            var add = Build("EXEC |", null, editor).Single(i => i.DisplayText == "AddOrder");

            Assert.Equal(
                "dbo.AddOrder @IdCustomer = 0,\n" +
                "                  @Note = NULL,\n" +
                "                  @Qty = 1,\n" +
                "                  @IdOrder = @IdOrder OUTPUT",
                add.InsertText);
            Assert.True(add.IsBlock);
        }

        [Fact]
        public void OutputParametersAreDeclaredAboveTheCall()
        {
            var editor = new EditorLayout { NewLine = "\n", LineIndent = "  ", LinePrefix = "  EXEC " };
            var add = Build("EXEC |", null, editor).Single(i => i.DisplayText == "AddOrder");

            Assert.Equal("  DECLARE @IdOrder int;\n", add.BeforeLine);
            Assert.Null(Build("EXEC |").Single(i => i.DisplayText == "Cleanup").BeforeLine);
        }

        [Fact]
        public void ProcedureWithoutParametersIsInsertedAlone()
        {
            var cleanup = Build("EXEC |").Single(i => i.DisplayText == "Cleanup");

            Assert.Equal("dbo.Cleanup", cleanup.InsertText);
            Assert.False(cleanup.IsBlock);
        }

        [Theory]
        [InlineData("nvarchar(50)", false, "N''")]
        [InlineData("varchar(max)", false, "''")]
        [InlineData("int", false, "0")]
        [InlineData("decimal(18,2)", false, "0")]
        [InlineData("bit", false, "0")]
        [InlineData("datetime2(7)", false, "'1900-01-01'")]
        [InlineData("uniqueidentifier", false, "'00000000-0000-0000-0000-000000000000'")]
        [InlineData("varbinary(8)", false, "0x")]
        [InlineData("geography", false, "NULL")]
        [InlineData("int", true, "NULL")]
        public void PlaceholderValuesFollowTheType(string type, bool nullable, string expected) =>
            Assert.Equal(expected, SqlDefaults.ValueFor(type, nullable));

        [Fact]
        public void DefaultsAreReadFromTheProcedureHeader()
        {
            var defaults = ProcedureHeader.ReadDefaults(
                "CREATE PROCEDURE [dbo].[AddOrder]\n    @IdCustomer int,\n    @Note nvarchar(50) = NULL, -- optional\n    @Name varchar(20) = 'a, b',\n    @Qty int = (1),\n    @Out int = 0 OUTPUT\nAS\nBEGIN\n    SELECT @x = 5\nEND");

            Assert.False(defaults.ContainsKey("@IdCustomer"));
            Assert.Equal("NULL", defaults["@Note"]);
            Assert.Equal("'a, b'", defaults["@Name"]);
            Assert.Equal("(1)", defaults["@Qty"]);
            Assert.Equal("0", defaults["@Out"]);
            Assert.Equal(4, defaults.Count);
        }

        [Fact]
        public void ParenthesizedParameterListAndTruncatedHeader()
        {
            var wrapped = ProcedureHeader.ReadDefaults("create proc dbo.P(@a int = 7, @b varchar(5) = 'x') as select 1");
            Assert.Equal("7", wrapped["@a"]);
            Assert.Equal("'x'", wrapped["@b"]);

            var cut = ProcedureHeader.ReadDefaults("CREATE PROC dbo.P @a int = 7, @b varchar(5) = 'x");
            Assert.Equal("7", cut["@a"]);
            Assert.Empty(ProcedureHeader.ReadDefaults(null));
        }
    }
}
