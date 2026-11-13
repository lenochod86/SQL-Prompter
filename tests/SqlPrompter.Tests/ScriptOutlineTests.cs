using System.Linq;
using SqlPrompter.Core.Navigation;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The outline of a script (#224).</summary>
    public class ScriptOutlineTests
    {
        [Fact]
        public void BatchesStatementsCtesAndTempTablesAreListedInOrder()
        {
            var sql = "DECLARE @a int, @b int = 1;\nSELECT * INTO #t FROM dbo.A;\nGO\nWITH c1 AS (SELECT 1 AS x), c2 AS (SELECT 2 AS y)\nSELECT * FROM c1, c2;\nGO\nDROP TABLE #t;";

            var items = ScriptOutline.Build(sql);

            Assert.Equal(new[] { "Batch", "Statement", "Variable", "Variable", "Statement", "Temp table", "Batch", "Statement", "CTE", "CTE", "Batch", "Statement", "Temp table" }, items.Select(i => i.Kind).ToArray());
            Assert.Equal("#t (created)", items.First(i => i.Kind == "Temp table").Name);
            Assert.Equal("#t (dropped)", items.Last().Name);
            Assert.Equal(new[] { "c1", "c2" }, items.Where(i => i.Kind == "CTE").Select(i => i.Name).ToArray());
            Assert.Equal(new[] { "@a", "@b" }, items.Where(i => i.Kind == "Variable").Select(i => i.Name).ToArray());
        }

        [Fact]
        public void LinesAreOneBased()
        {
            var items = ScriptOutline.Build("SELECT 1;\n\nUPDATE t SET a = 1");

            Assert.Equal(new[] { 1, 1, 3 }, items.Where(i => i.Kind != "Batch" || i.Line == 1).Select(i => i.Line).ToArray());
        }

        [Fact]
        public void AModuleHeaderIsListed() =>
            Assert.Contains("Module: PROCEDURE dbo.GetUsers", ScriptOutline.Build("CREATE OR ALTER PROCEDURE dbo.GetUsers @a int AS SELECT 1").Select(i => i.Text));

        [Fact]
        public void UnfinishedCodeStillGivesAnOutline()
        {
            Assert.NotEmpty(ScriptOutline.Build("SELECT * FROM (SELECT"));
            Assert.NotEmpty(ScriptOutline.Build("WITH c AS ("));
            Assert.Empty(ScriptOutline.Build("   "));
            Assert.Empty(ScriptOutline.Build("-- only a comment"));
        }

        [Fact]
        public void AnInsertIntoATempTableIsNotACreation() =>
            Assert.DoesNotContain(ScriptOutline.Build("INSERT INTO #t SELECT 1"), i => i.Kind == "Temp table");

        [Fact]
        public void BlocksAreListed()
        {
            var items = ScriptOutline.Build("BEGIN TRY\nSELECT 1;\nEND TRY\nBEGIN CATCH\nSELECT 2;\nEND CATCH\nIF 1 = 1 SELECT 3;\nWHILE 1 = 0 SELECT 4;");

            Assert.Equal(new[] { "BEGIN TRY", "BEGIN CATCH" }, items.Where(i => i.Kind == "Block").Take(2).Select(i => i.Name).ToArray());
            Assert.Equal(4, items.Count(i => i.Kind == "Block"));
        }

        [Fact]
        public void ProcedureParametersAreListed()
        {
            var items = ScriptOutline.Build("CREATE PROCEDURE dbo.P @a int, @b varchar(10) = 'x' OUTPUT AS SELECT @a");

            Assert.Equal(new[] { "@a", "@b" }, items.Where(i => i.Kind == "Parameter").Select(i => i.Name).ToArray());
        }

        [Fact]
        public void FunctionParametersInParenthesesAreListed()
        {
            var items = ScriptOutline.Build("CREATE FUNCTION dbo.F (@a int, @b int = 1) RETURNS int AS BEGIN RETURN @a END");

            Assert.Equal(new[] { "@a", "@b" }, items.Where(i => i.Kind == "Parameter").Select(i => i.Name).ToArray());
        }

        [Fact]
        public void JumpGoesToTheNextAndPreviousStatementAndBatch()
        {
            var sql = "SELECT 1;\nSELECT 2;\nGO\nSELECT 3;";
            var items = ScriptOutline.Build(sql);
            var second = sql.IndexOf("SELECT 2");
            var third = sql.IndexOf("SELECT 3");

            Assert.Equal(second, ScriptOutline.JumpTarget(items, 3, false, true));
            Assert.Equal(0, ScriptOutline.JumpTarget(items, 3, false, false));
            Assert.Equal(0, ScriptOutline.JumpTarget(items, second, false, false));
            Assert.Equal(-1, ScriptOutline.JumpTarget(items, third, false, true));
            Assert.Equal(third, ScriptOutline.JumpTarget(items, 3, true, true));
            Assert.Equal(third, ScriptOutline.JumpTarget(items, third + 2, true, false));
            Assert.Equal(0, ScriptOutline.JumpTarget(items, third, true, false));
        }

        [Fact]
        public void TheItemAtThePositionIsTheLastOneThatStartedBeforeIt()
        {
            var sql = "SELECT 1;\nSELECT 2;";
            var items = ScriptOutline.Build(sql);

            Assert.Equal("SELECT 2;", items[ScriptOutline.IndexAt(items, sql.IndexOf("SELECT 2") + 3)].Name);
            Assert.Equal(-1, ScriptOutline.IndexAt(items, -1));
        }
    }
}
