using System.Linq;
using SqlPrompter.Core.Analysis;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>What a script changes and whether it leaves a transaction open (#209).</summary>
    public class ChangeWarningsTests
    {
        private static string[] Kinds(string sql) => ChangeWarnings.FindChanges(sql).Select(c => c.Kind).ToArray();

        [Theory]
        [InlineData("UPDATE dbo.Users SET a = 1 WHERE Id = 1", "UPDATE")]
        [InlineData("DELETE FROM dbo.Users WHERE Id = 1", "DELETE")]
        [InlineData("DELETE dbo.Users", "DELETE")]
        [InlineData("TRUNCATE TABLE dbo.Users", "TRUNCATE")]
        [InlineData("DROP TABLE IF EXISTS dbo.Users", "DROP")]
        [InlineData("ALTER TABLE dbo.Users ADD x int", "ALTER")]
        [InlineData("CREATE OR ALTER PROCEDURE dbo.p AS SELECT 1", "CREATE OR ALTER")]
        [InlineData("MERGE dbo.T AS t USING dbo.S AS s ON t.Id = s.Id WHEN MATCHED THEN DELETE;", "MERGE")]
        [InlineData("EXEC sp_rename 'dbo.A', 'B'", "sp_rename")]
        [InlineData("WITH c AS (SELECT 1 AS a) UPDATE dbo.T SET a = 1", "UPDATE")]
        public void ChangesAreFound(string sql, string kind) => Assert.Equal(new[] { kind }, Kinds(sql));

        [Theory]
        [InlineData("SELECT * FROM dbo.Users")]
        [InlineData("INSERT INTO dbo.Users (a) VALUES (1)")]
        [InlineData("CREATE TABLE dbo.T (a int)")]
        [InlineData("UPDATE #t SET a = 1")]
        [InlineData("DELETE FROM @t")]
        [InlineData("DROP TABLE IF EXISTS #t")]
        [InlineData("TRUNCATE TABLE #t")]
        [InlineData("-- DELETE FROM dbo.Users")]
        [InlineData("SELECT 'DROP TABLE x'")]
        public void OtherStatementsAreNotChanges(string sql) => Assert.Empty(Kinds(sql));

        [Fact]
        public void SeveralStatementsKeepTheirLines()
        {
            var changes = ChangeWarnings.FindChanges("SELECT 1;\nUPDATE a SET x = 1;\nGO\nDROP TABLE b");

            Assert.Equal(new[] { 2, 4 }, changes.Select(c => c.Line).ToArray());
            Assert.Equal("UPDATE (line 2): UPDATE a SET x = 1;", changes[0].Message);
        }

        [Theory]
        [InlineData("BEGIN TRAN\nUPDATE t SET a = 1", 1)]
        [InlineData("SELECT 1\nBEGIN TRANSACTION;\nUPDATE t SET a = 1\nGO\nSELECT 2", 2)]
        [InlineData("BEGIN TRAN\nUPDATE t SET a = 1\nCOMMIT", 0)]
        [InlineData("BEGIN TRAN\nROLLBACK TRAN", 0)]
        [InlineData("BEGIN TRY\nSELECT 1\nEND TRY\nBEGIN CATCH\nSELECT 2\nEND CATCH", 0)]
        [InlineData("BEGIN TRAN\nGO\nCOMMIT", 1)]
        [InlineData("CREATE PROCEDURE p AS\nBEGIN\nBEGIN TRAN\nSELECT 1\nEND", 0)]
        [InlineData("", 0)]
        public void AnOpenTransactionIsFound(string sql, int line) => Assert.Equal(line, ChangeWarnings.OpenTransactionLine(sql));
    }
}

namespace SqlPrompter.Tests
{
    public class ChangeWarningsRuleTests
    {
        [Fact]
        public void TheMatchingRuleCarriesTheAskFlag()
        {
            var settings = new SqlPrompter.Core.Settings.ColorSettings { Enabled = true };
            settings.Environments.Add(new SqlPrompter.Core.Settings.ColorEnvironment { Name = "Production", Color = "#ff0000" });
            settings.Rules.Add(new SqlPrompter.Core.Settings.ColorRule { Server = "prod*", Database = string.Empty, Environment = "Production", AskBeforeChanges = true });

            Assert.True(SqlPrompter.Core.Coloring.EnvironmentMatcher.FindRule(settings, "prod01", "Db").AskBeforeChanges);
            Assert.Null(SqlPrompter.Core.Coloring.EnvironmentMatcher.FindRule(settings, "test01", "Db"));
            Assert.True(settings.Clone().Rules[0].AskBeforeChanges);
        }
    }
}
