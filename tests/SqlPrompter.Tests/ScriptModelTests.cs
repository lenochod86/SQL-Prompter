using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class ScriptModelTests
    {
        private static ScriptModel Model(string sqlWithCaret)
        {
            var caret = sqlWithCaret.IndexOf('|');
            return ScriptModel.Build(sqlWithCaret.Remove(caret, 1), caret);
        }

        private static string[] Names(ScriptColumns columns) => columns.Columns.Select(c => c.Name).ToArray();

        private static ScriptColumns ResolveTable(ScriptModel model, string name, IDatabaseMetadata metadata = null) =>
            model.Resolve(model.Tables.Last(t => t.Name == name), metadata);

        [Fact]
        public void CteColumnsComeFromItsSelectList()
        {
            var model = Model("WITH c AS (SELECT u.Id, u.Name AS UserName, COUNT(*) cnt, Total * 2 AS Double, x = 1, 5 FROM dbo.Users u) SELECT | FROM c");

            var cte = Assert.Single(model.Tables);
            Assert.Equal(ScriptTableKind.Cte, cte.Kind);
            Assert.Equal(new[] { "Id", "UserName", "cnt", "Double", "x" }, Names(model.Resolve(cte, null)));
        }

        [Fact]
        public void CteWithColumnListUsesTheList()
        {
            var model = Model("WITH c (a, b) AS (SELECT 1, 2) SELECT | FROM c");

            Assert.Equal(new[] { "a", "b" }, Names(ResolveTable(model, "c")));
        }

        [Fact]
        public void SeveralCtesSeeEachOther()
        {
            var model = Model("WITH a AS (SELECT Id FROM dbo.Users), b AS (SELECT * FROM a) SELECT | FROM b");

            Assert.Equal(new[] { "a", "b" }, model.Tables.Select(t => t.Name).ToArray());
            Assert.Equal(new[] { "Id" }, Names(ResolveTable(model, "b")));
        }

        [Fact]
        public void CteDefinedAfterTheCaretIsNotVisible()
        {
            var model = Model("WITH a AS (SELECT 1 AS x), b AS (SELECT | AS y) SELECT * FROM b");

            Assert.Equal(new[] { "a", "b" }, model.Tables.Select(t => t.Name).ToArray());
            Assert.Equal(new[] { "x" }, Names(ResolveTable(model, "a")));

            var before = Model("WITH a AS (SELECT | AS x), b AS (SELECT 2 AS y) SELECT * FROM b");
            Assert.Equal(new[] { "a" }, before.Tables.Select(t => t.Name).ToArray());
        }

        [Fact]
        public void CtesOfOtherStatementsAreNotVisible()
        {
            var model = Model("WITH a AS (SELECT 1 AS x) SELECT * FROM a;\nSELECT | FROM dbo.Users");

            Assert.Empty(model.Tables);
        }

        [Fact]
        public void StarIsExpandedFromDatabaseObjects()
        {
            var model = Model("WITH c AS (SELECT * FROM dbo.Users) SELECT | FROM c");

            var result = ResolveTable(model, "c", SampleDatabase());

            Assert.Equal(new[] { "Id", "Name", "Email" }, Names(result));
            Assert.True(result.IsComplete);
        }

        [Fact]
        public void QualifiedStarExpandsOnlyThatSource()
        {
            var model = Model("WITH c AS (SELECT o.*, u.Name FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id) SELECT | FROM c");

            Assert.Equal(new[] { "Id", "UserId", "Total", "Name" }, Names(ResolveTable(model, "c", SampleDatabase())));
        }

        [Fact]
        public void StarWithoutLoadedColumnsReportsMissingObjects()
        {
            var model = Model("WITH c AS (SELECT * FROM dbo.Users) SELECT | FROM c");

            var result = ResolveTable(model, "c", SampleDatabase(withColumns: false));

            Assert.Empty(result.Columns);
            Assert.False(result.IsComplete);
            Assert.Equal("Users", Assert.Single(result.MissingColumns).Name);
        }

        [Fact]
        public void OnlyFirstUnionBlockNamesColumns()
        {
            var model = Model("WITH c AS (SELECT Id AS a FROM dbo.Users UNION ALL SELECT Id AS b FROM dbo.Orders) SELECT | FROM c");

            Assert.Equal(new[] { "a" }, Names(ResolveTable(model, "c")));
        }

        [Fact]
        public void TopAndDistinctAreSkipped()
        {
            var model = Model("WITH c AS (SELECT DISTINCT TOP (5) Id, Name FROM dbo.Users) SELECT | FROM c");

            Assert.Equal(new[] { "Id", "Name" }, Names(ResolveTable(model, "c")));
        }

        [Fact]
        public void CreateTableDefinesTempTable()
        {
            var model = Model("CREATE TABLE #t (Id int NOT NULL PRIMARY KEY, Name nvarchar(50), Note varchar(max) NULL, CONSTRAINT pk PRIMARY KEY (Id))\nSELECT | FROM #t");

            var table = Assert.Single(model.Tables);
            Assert.Equal(ScriptTableKind.TempTable, table.Kind);
            Assert.Equal("#t", table.Name);
            var columns = model.Resolve(table, null).Columns;
            Assert.Equal(new[] { "Id", "Name", "Note" }, columns.Select(c => c.Name).ToArray());
            Assert.Equal(new[] { "int", "nvarchar(50)", "varchar(max)" }, columns.Select(c => c.TypeName).ToArray());
            Assert.Equal(new[] { false, true, true }, columns.Select(c => c.IsNullable).ToArray());
        }

        [Fact]
        public void SelectIntoDefinesTempTable()
        {
            var model = Model("SELECT u.Id, o.Total AS Amount INTO #r FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id\nSELECT | FROM #r");

            var table = Assert.Single(model.Tables);
            Assert.Equal("#r", table.Name);
            Assert.Equal(new[] { "Id", "Amount" }, Names(model.Resolve(table, null)));
        }

        [Fact]
        public void SelectIntoWithStar()
        {
            var model = Model("SELECT * INTO #r FROM dbo.Users\nSELECT | FROM #r");

            Assert.Equal(new[] { "Id", "Name", "Email" }, Names(ResolveTable(model, "#r", SampleDatabase())));
        }

        [Fact]
        public void TempTableCreatedAfterTheCaretIsUnknown()
        {
            var model = Model("SELECT | FROM #t\nCREATE TABLE #t (Id int)");

            Assert.Empty(model.Tables);
        }

        [Fact]
        public void LaterTempTableDefinitionReplacesEarlier()
        {
            var model = Model("CREATE TABLE #t (a int)\nDROP TABLE #t\nCREATE TABLE #t (b int)\nSELECT | FROM #t");

            Assert.Equal(new[] { "b" }, Names(ResolveTable(model, "#t")));
        }

        [Fact]
        public void TableVariableColumns()
        {
            var model = Model("DECLARE @t TABLE (Id int, Name varchar(10) NOT NULL)\nSELECT | FROM @t");

            var table = Assert.Single(model.Tables);
            Assert.Equal(ScriptTableKind.TableVariable, table.Kind);
            Assert.Equal("@t", table.Name);
            Assert.Equal(new[] { "Id", "Name" }, Names(model.Resolve(table, null)));
            Assert.Empty(model.Variables);
        }

        [Fact]
        public void DeclaredVariables()
        {
            var model = Model("DECLARE @a int = 5, @b varchar(10), @c AS decimal(18, 2)\nDECLARE @d datetime\nSELECT |");

            Assert.Equal(new[] { "@a", "@b", "@c", "@d" }, model.Variables.Select(v => v.Name).ToArray());
            Assert.Equal(new[] { "int", "varchar(10)", "decimal(18, 2)", "datetime" }, model.Variables.Select(v => v.TypeName).ToArray());
            Assert.All(model.Variables, v => Assert.False(v.IsParameter));
        }

        [Fact]
        public void VariableDeclaredAfterTheCaretIsUnknown()
        {
            var model = Model("DECLARE @a int\nSELECT |\nDECLARE @b int");

            Assert.Equal(new[] { "@a" }, model.Variables.Select(v => v.Name).ToArray());
        }

        [Fact]
        public void VariablesFromAnotherBatchAreIgnored()
        {
            // The IDE passes one batch (between GO lines) at a time.
            var model = ScriptModel.Build("SELECT ", 7);

            Assert.Empty(model.Variables);
        }

        [Theory]
        [InlineData("CREATE PROCEDURE dbo.p @a int, @b varchar(20) = 'x', @c int OUTPUT AS\nSELECT |", new[] { "@a", "@b", "@c" }, new[] { "int", "varchar(20)", "int" })]
        [InlineData("CREATE OR ALTER PROC dbo.p (@a int = 1, @b decimal(10, 2) READONLY) AS\nBEGIN\nSELECT |\nEND", new[] { "@a", "@b" }, new[] { "int", "decimal(10, 2)" })]
        [InlineData("ALTER FUNCTION dbo.f (@x nvarchar(max)) RETURNS int AS BEGIN RETURN | END", new[] { "@x" }, new[] { "nvarchar(max)" })]
        public void ProcedureAndFunctionParameters(string sql, string[] names, string[] types)
        {
            var model = Model(sql);

            Assert.Equal(names, model.Variables.Select(v => v.Name).ToArray());
            Assert.Equal(types, model.Variables.Select(v => v.TypeName).ToArray());
            Assert.All(model.Variables, v => Assert.True(v.IsParameter));
        }

        [Fact]
        public void DerivedTableColumns()
        {
            var context = Analyze("SELECT d.| FROM (SELECT u.Id, Name n, 1 AS one FROM dbo.Users u) d");
            var model = Model("SELECT d.| FROM (SELECT u.Id, Name n, 1 AS one FROM dbo.Users u) d");

            Assert.Equal(new[] { "Id", "n", "one" }, Names(model.Resolve(context.QualifiedSource, null)));
        }

        [Fact]
        public void DerivedTableWithColumnListAndStar()
        {
            var model = Model("SELECT | FROM (SELECT * FROM dbo.Users) d");
            var context = Analyze("SELECT | FROM (SELECT * FROM dbo.Users) d");

            Assert.Equal(new[] { "Id", "Name", "Email" }, Names(model.Resolve(context.Sources.Single(), SampleDatabase())));
        }

        [Fact]
        public void NestedDerivedTablesAndCtesResolve()
        {
            var sql = "WITH c AS (SELECT Id, Name FROM dbo.Users) SELECT | FROM (SELECT * FROM c) d";
            var model = Model(sql);
            var context = Analyze(sql);

            Assert.Equal(new[] { "Id", "Name" }, Names(model.Resolve(context.Sources.Single(), SampleDatabase())));
        }

        [Fact]
        public void SelfReferencingCteDoesNotLoopForever()
        {
            var model = Model("WITH c AS (SELECT * FROM c) SELECT | FROM c");

            var result = ResolveTable(model, "c", SampleDatabase());

            Assert.False(result.IsComplete);
        }

        [Fact]
        public void EmptyAndBrokenInputDoNotThrow()
        {
            Assert.Empty(ScriptModel.Build(string.Empty, 0).Tables);
            Assert.Empty(ScriptModel.Build("WITH", 4).Tables);
            Assert.DoesNotContain(ScriptModel.Build("WITH c AS (", 11).Tables, t => t.Name != "c");
            Assert.DoesNotContain(ScriptModel.Build("CREATE PROC p (@a", 17).Variables, v => v.Name != "@a");
            ScriptModel.Build("DECLARE @t TABLE (", 18);
            ScriptModel.Build("SELECT * INTO #t", 16);
        }

    }
}
