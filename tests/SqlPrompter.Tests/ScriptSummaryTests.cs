using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class ScriptSummaryTests
    {
        private static string Build(string sql) => ScriptSummary.Build(sql, "\n");

        [Fact]
        public void CountsBatchesAndStatements()
        {
            var summary = Build("SELECT 1\nGO\nSELECT 2\nUPDATE t SET a = 1");

            Assert.Contains("Batches: 2, statements: 3", summary);
            Assert.Contains("SELECT 2, UPDATE 1", summary);
        }

        [Fact]
        public void ListsReadAndChangedObjects()
        {
            var summary = Build("INSERT INTO dbo.Log (a) SELECT a FROM dbo.Users u JOIN Orders o ON o.Id = u.Id;\nDELETE FROM dbo.Old;\nUPDATE x SET a = 1");

            Assert.Contains("Reads (2):\n  dbo.Users\n  Orders\n", summary);
            Assert.Contains("Changes (3):\n  dbo.Log\n  dbo.Old\n  x\n", summary);
        }

        [Fact]
        public void CtesAreNotObjectsAndTemporaryTablesAreListedApart()
        {
            var summary = Build("WITH c AS (SELECT 1 a) SELECT * INTO #t FROM c;\nSELECT * FROM #t, dbo.Real");

            Assert.Contains("Reads (1):\n  dbo.Real\n", summary);
            Assert.Contains("Temporary tables (1):\n  #t\n", summary);
            Assert.DoesNotContain("Changes", summary);
        }

        [Fact]
        public void ProceduresVariablesAndCreatedObjects()
        {
            var summary = Build("DECLARE @a int, @b int\nEXEC dbo.DoIt @a\nDROP TABLE IF EXISTS dbo.Tmp\nCREATE VIEW dbo.V AS SELECT @@ROWCOUNT r");

            Assert.Contains("Executes (1):\n  dbo.DoIt\n", summary);
            Assert.Contains("Drops (1):\n  table dbo.Tmp\n", summary);
            Assert.Contains("Creates or alters (1):\n  view dbo.V\n", summary);
            Assert.Contains("Variables (2):\n  @a\n  @b\n", summary);
        }

        [Fact]
        public void AnEmptyScriptHasAnEmptySummary() => Assert.Contains("Batches: 0, statements: 0", Build(string.Empty));
    }
}
