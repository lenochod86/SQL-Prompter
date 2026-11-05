using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class ExecutionWarningsTests
    {
        private static string[] Find(string sql, bool delete = true, bool update = true) =>
            ExecutionWarnings.Find(sql, delete, update).Select(w => w.Kind + "@" + w.Line).ToArray();

        [Theory]
        [InlineData("DELETE FROM dbo.Orders", "DeleteWithoutWhere@1")]
        [InlineData("delete dbo.Orders", "DeleteWithoutWhere@1")]
        [InlineData("UPDATE dbo.Orders SET Note = 'x'", "UpdateWithoutWhere@1")]
        [InlineData("DELETE o FROM dbo.Orders o JOIN dbo.Users u ON u.Id = o.IdUser", "DeleteWithoutWhere@1")]
        [InlineData("UPDATE o SET o.Note = u.Name FROM dbo.Orders o JOIN dbo.Users u ON u.Id = o.IdUser", "UpdateWithoutWhere@1")]
        [InlineData("DELETE FROM dbo.Orders WHERE Id IN (SELECT 1)", null)]
        [InlineData("UPDATE dbo.Orders SET Note = (SELECT TOP 1 Name FROM dbo.Users WHERE Id = 1)", "UpdateWithoutWhere@1")]
        public void FindsDeleteAndUpdateWithoutWhere(string sql, string expected) =>
            Assert.Equal(expected == null ? new string[0] : new[] { expected }, Find(sql));

        [Theory]
        [InlineData("DELETE FROM dbo.Orders WHERE Id = 1")]
        [InlineData("UPDATE dbo.Orders SET Note = 'x' WHERE Id = 1")]
        [InlineData("DELETE o FROM dbo.Orders o JOIN dbo.Users u ON u.Id = o.IdUser WHERE u.Name = 'a'")]
        [InlineData("DELETE TOP (100) FROM dbo.Orders")]
        [InlineData("UPDATE TOP (10) dbo.Orders SET Note = 'x'")]
        [InlineData("DELETE FROM #temp")]
        [InlineData("DELETE FROM @rows")]
        [InlineData("UPDATE #temp SET a = 1")]
        [InlineData("SELECT 'DELETE FROM dbo.Orders'")]
        [InlineData("-- DELETE FROM dbo.Orders\nSELECT 1")]
        [InlineData("/* UPDATE dbo.Orders SET a = 1 */ SELECT 1")]
        [InlineData("INSERT INTO dbo.Orders (a) SELECT a FROM dbo.Old")]
        [InlineData("MERGE dbo.T AS t USING dbo.S AS s ON t.Id = s.Id WHEN MATCHED THEN DELETE;")]
        public void SafeOrIgnoredStatementsGiveNoWarning(string sql) => Assert.Empty(Find(sql));

        [Fact]
        public void EveryStatementIsCheckedWithItsLine()
        {
            var sql = "SELECT 1;\r\nDELETE FROM dbo.A;\r\nUPDATE dbo.B SET x = 1 WHERE y = 2;\r\nUPDATE dbo.C SET x = 1";

            Assert.Equal(new[] { "DeleteWithoutWhere@2", "UpdateWithoutWhere@4" }, Find(sql));
        }

        [Fact]
        public void StatementsWithoutSemicolonsAreSeparated()
        {
            var sql = "DELETE FROM dbo.A\nUPDATE dbo.B SET x = 1 WHERE y = 2\nDELETE FROM dbo.C WHERE z = 1";

            Assert.Equal(new[] { "DeleteWithoutWhere@1" }, Find(sql));
        }

        [Fact]
        public void ACommonTableExpressionIsInspectedAfterItsDefinition()
        {
            Assert.Equal(new[] { "DeleteWithoutWhere@1" }, Find("WITH c AS (SELECT 1 AS a) DELETE FROM dbo.T"));
            Assert.Empty(Find("WITH c AS (SELECT 1 AS a) DELETE FROM dbo.T WHERE a IN (SELECT a FROM c)"));
        }

        [Theory]
        [InlineData("CREATE PROCEDURE dbo.p AS BEGIN DELETE FROM dbo.A END")]
        [InlineData("ALTER FUNCTION dbo.f() RETURNS int AS BEGIN UPDATE dbo.A SET x = 1 RETURN 1 END")]
        [InlineData("CREATE TRIGGER dbo.t ON dbo.A AFTER INSERT AS UPDATE dbo.B SET x = 1")]
        public void ScriptsThatOnlyStoreAnObjectAreNotWarnedAbout(string sql) => Assert.Empty(Find(sql));

        [Fact]
        public void AfterGoTheNextBatchIsCheckedAgain()
        {
            var sql = "CREATE PROCEDURE dbo.p AS DELETE FROM dbo.A\nGO\nDELETE FROM dbo.B";

            Assert.Equal(new[] { "DeleteWithoutWhere@3" }, Find(sql));
        }

        [Fact]
        public void EachWarningCanBeSwitchedOff()
        {
            var sql = "DELETE FROM dbo.A\nUPDATE dbo.B SET x = 1";

            Assert.Equal(new[] { "UpdateWithoutWhere@2" }, Find(sql, delete: false));
            Assert.Equal(new[] { "DeleteWithoutWhere@1" }, Find(sql, update: false));
            Assert.Empty(Find(sql, delete: false, update: false));
        }

        [Fact]
        public void TheMessageNamesTheKindTheLineAndTheText() =>
            Assert.Equal("DELETE without WHERE (line 2): DELETE FROM dbo.A", ExecutionWarnings.Find("SELECT 1\nDELETE FROM dbo.A", true, true).Single().Message);

        [Fact]
        public void BothWarningsAreOnByDefault()
        {
            var behavior = new BehaviorSettings();

            Assert.True(behavior.WarnDeleteWithoutWhere);
            Assert.True(behavior.WarnUpdateWithoutWhere);
        }
    }
}
