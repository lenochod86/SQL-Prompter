using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Metadata;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>A column that a temporary table or table variable of the script does not have (#452).</summary>
    public class UnknownTempColumnTests
    {
        private static DatabaseSnapshot Db() =>
            new DatabaseSnapshot("t", "dbo", new[] { "dbo" }, new[] { new DbObject(1, "dbo", "Orders", DbObjectKind.Table, false) }, "v1")
                .WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
                {
                    [1] = new[] { new DbColumn("IdOrder", "int", false), new DbColumn("Total", "money", false) },
                });

        private static string[] Unknown(string sql, IDatabaseMetadata db = null) =>
            CodeAnalyzer.Analyze(sql, new AnalysisSettings(), db).Where(i => i.Kind == CodeIssueKind.UnknownColumn).Select(i => sql.Substring(i.Start, i.Length)).ToArray();

        [Fact]
        public void TheExampleOfTheIssueIsReported()
        {
            const string sql = @"SELECT p.id_spol, p.tarif_uplatneny AS tarif, p.castka
INTO #1
FROM #prov p
LEFT JOIN dbo.Orders o ON o.IdOrder = p.id_spol

UPDATE p
SET p.tarif_uplatneny = NULL
  , p.castka = NULL
FROM #1 p
JOIN dbo.Orders o ON o.IdOrder = p.id_spol
WHERE p.tarif = 1";

            // The second statement has p = #1, where the column was renamed to tarif; in the first one p is #prov (unknown).
            Assert.Equal(new[] { "tarif_uplatneny" }, Unknown(sql, Db()));
        }

        [Fact]
        public void ACreateTableColumnThatDoesNotExist()
        {
            Assert.Equal(new[] { "b" }, Unknown("CREATE TABLE #t (a int);\nSELECT t.b FROM #t t"));
            Assert.Empty(Unknown("CREATE TABLE #t (a int);\nSELECT t.a, T.A FROM #t t"));
        }

        [Fact]
        public void ATableVariableToo()
        {
            Assert.Equal(new[] { "x" }, Unknown("DECLARE @t TABLE (a int);\nSELECT t.x FROM @t t"));
            Assert.Empty(Unknown("DECLARE @t TABLE (a int);\nSELECT t.a FROM @t t"));
        }

        [Fact]
        public void BracketedAndCaseInsensitiveNames()
        {
            Assert.Empty(Unknown("CREATE TABLE #t ([a b] int);\nSELECT t.[A B] FROM #t t"));
            Assert.Equal(new[] { "[c]" }, Unknown("CREATE TABLE #t ([a b] int);\nSELECT t.[c] FROM #t t"));
        }

        [Fact]
        public void TheNamesOfASelectIntoFollowTheSelectList()
        {
            const string sql = "SELECT o.IdOrder, o.Total AS Amount, o.Total * 2 Doubled, Total INTO #r FROM dbo.Orders o;\nSELECT r.IdOrder, r.Amount, r.Doubled, r.Total, r.Missing FROM #r r";

            Assert.Equal(new[] { "Missing" }, Unknown(sql, Db()));
        }

        [Fact]
        public void AStarOverALoadedTableGivesItsColumns()
        {
            Assert.Equal(new[] { "Nope" }, Unknown("SELECT o.* INTO #r FROM dbo.Orders o;\nSELECT r.IdOrder, r.Nope FROM #r r", Db()));
        }

        [Fact]
        public void AStarOverAnUnknownSourceReportsNothing()
        {
            Assert.Empty(Unknown("SELECT * INTO #r FROM dbo.Orders;\nSELECT r.Anything FROM #r r"));
            Assert.Empty(Unknown("SELECT u.* INTO #r FROM dbo.Unknown u;\nSELECT r.Anything FROM #r r", Db()));
            Assert.Empty(Unknown("SELECT * INTO #r FROM #other;\nSELECT r.Anything FROM #r r", Db()));
        }

        [Fact]
        public void ATableThatIsNotCreatedInTheScriptReportsNothing()
        {
            Assert.Empty(Unknown("SELECT t.b FROM #t t"));
        }

        [Fact]
        public void ATableCreatedAfterTheUseIsNotKnownAtTheUse()
        {
            Assert.Empty(Unknown("SELECT t.b FROM #t t;\nCREATE TABLE #t (a int);"));
        }

        [Fact]
        public void AnAnUnnamedExpressionMakesTheColumnsIncomplete()
        {
            Assert.Empty(Unknown("SELECT o.IdOrder, o.Total + 1 INTO #r FROM dbo.Orders o;\nSELECT r.Nope FROM #r r", Db()));
        }

        [Fact]
        public void AlterTableAddMakesAColumnKnownAndDropRemovesIt()
        {
            Assert.Empty(Unknown("CREATE TABLE #t (a int);\nALTER TABLE #t ADD b int;\nSELECT t.b FROM #t t"));
            Assert.Equal(new[] { "a" }, Unknown("CREATE TABLE #t (a int, b int);\nALTER TABLE #t DROP COLUMN a;\nSELECT t.a FROM #t t"));
        }

        [Fact]
        public void AlterTableOfASelectIntoTableOrDynamicSqlReportsNothing()
        {
            Assert.Empty(Unknown("SELECT 1 AS a INTO #t;\nALTER TABLE #t ADD b int;\nSELECT t.b FROM #t t"));
            Assert.Empty(Unknown("CREATE TABLE #t (a int);\nEXEC('ALTER TABLE #t ADD b int');\nSELECT t.b FROM #t t"));
        }

        [Fact]
        public void TheAliasIsResolvedPerStatement()
        {
            const string sql = "CREATE TABLE #a (x int);\nCREATE TABLE #b (y int);\nSELECT p.x FROM #a p;\nSELECT p.y FROM #b p;\nSELECT p.x FROM #b p;";

            Assert.Equal(new[] { "x" }, Unknown(sql));
        }

        [Fact]
        public void ARegularTableOrAFunctionIsNotChecked()
        {
            Assert.Empty(Unknown("CREATE TABLE #t (a int);\nSELECT o.Whatever, dbo.Fn(1), t.a FROM dbo.Orders o JOIN #t t ON 1 = 1", Db()));
        }

        [Fact]
        public void TheRuleCanBeSwitchedOff()
        {
            var settings = new AnalysisSettings { UnknownColumn = false };

            Assert.DoesNotContain(CodeAnalyzer.Analyze("CREATE TABLE #t (a int);\nSELECT t.b FROM #t t", settings), i => i.Kind == CodeIssueKind.UnknownColumn);
        }
    }
}
