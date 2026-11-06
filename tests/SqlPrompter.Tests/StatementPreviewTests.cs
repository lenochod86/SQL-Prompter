using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The preview of UPDATE and DELETE as a SELECT and as a dry run (#210).</summary>
    public class StatementPreviewTests
    {
        private static string Select(string sql)
        {
            var result = StatementPreview.ToSelect(sql, "\n");
            Assert.True(result.Succeeded, result.Reason);
            return result.Text;
        }

        [Fact]
        public void AnUpdateWithoutFromBecomesASelectWithTheNewValues()
        {
            Assert.Equal("SELECT *, 'x' AS [New Name]\nFROM dbo.Users\nWHERE Id = 1", Select("UPDATE dbo.Users SET Name = 'x' WHERE Id = 1"));
        }

        [Fact]
        public void SeveralAssignmentsKeepTheirExpressions()
        {
            Assert.Equal("SELECT *, 'x' AS [New Name], COALESCE(a, b) AS [New Email]\nFROM dbo.Users\nWHERE Id > 1", Select("UPDATE dbo.Users SET Name = 'x', Email = COALESCE(a, b) WHERE Id > 1"));
        }

        [Fact]
        public void AnUpdateWithoutWhereSelectsAllRows()
        {
            Assert.Equal("SELECT *, 0 AS [New Active]\nFROM dbo.Users", Select("UPDATE dbo.Users SET Active = 0;"));
        }

        [Fact]
        public void AnUpdateWithJoinSelectsTheTargetAliasAndKeepsTheFromClause()
        {
            var sql = "UPDATE e SET Name = 'x' FROM dbo.Employees e JOIN dbo.Departments d ON d.Id = e.IdDepartment WHERE d.Name = 'A'";

            Assert.Equal("SELECT e.*, 'x' AS [New Name]\nFROM dbo.Employees e JOIN dbo.Departments d ON d.Id = e.IdDepartment WHERE d.Name = 'A'", Select(sql));
        }

        [Fact]
        public void TopIsKept()
        {
            Assert.Equal("SELECT TOP (10) *, 1 AS [New a]\nFROM t\nWHERE x = 1", Select("UPDATE TOP (10) t SET a = 1 WHERE x = 1"));
            Assert.Equal("SELECT TOP 5 *\nFROM t\nWHERE x = 1", Select("DELETE TOP 5 FROM t WHERE x = 1"));
        }

        [Fact]
        public void ADeleteBecomesASelect()
        {
            Assert.Equal("SELECT *\nFROM dbo.Users\nWHERE Id = 1", Select("DELETE FROM dbo.Users WHERE Id = 1"));
            Assert.Equal("SELECT *\nFROM dbo.Users\nWHERE Id = 1", Select("DELETE dbo.Users WHERE Id = 1"));
            Assert.Equal("SELECT *\nFROM dbo.Users", Select("DELETE FROM dbo.Users"));
        }

        [Fact]
        public void ADeleteWithJoinSelectsTheTarget()
        {
            Assert.Equal("SELECT o.*\nFROM dbo.Orders o JOIN dbo.Users u ON u.Id = o.UserId WHERE u.Active = 0", Select("DELETE o FROM dbo.Orders o JOIN dbo.Users u ON u.Id = o.UserId WHERE u.Active = 0"));
        }

        [Fact]
        public void OptionIsKept()
        {
            Assert.Equal("SELECT *\nFROM t\nWHERE a = 1 OPTION (RECOMPILE)", Select("DELETE FROM t WHERE a = 1 OPTION (RECOMPILE)"));
        }

        [Fact]
        public void CommentsInsideTheKeptPartAreKept()
        {
            Assert.Equal("SELECT *\nFROM t\nWHERE a = 1 /* x */ AND b = 2", Select("DELETE FROM t WHERE a = 1 /* x */ AND b = 2 -- done"));
        }

        [Theory]
        [InlineData("SELECT 1", "not an UPDATE or DELETE")]
        [InlineData("WITH c AS (SELECT 1 AS a) UPDATE c SET a = 2", "CTE")]
        [InlineData("UPDATE t SET a = 1 OUTPUT inserted.a WHERE b = 1", "OUTPUT")]
        [InlineData("DELETE FROM t OUTPUT deleted.* WHERE b = 1", "OUTPUT")]
        [InlineData("UPDATE t SET @x = a WHERE b = 1", "only variables")]
        [InlineData("UPDATE t SET a = 1; DELETE FROM u", "more than one")]
        [InlineData("UPDATE t WITH (ROWLOCK) SET a = 1", "hints or an alias")]
        [InlineData("", "no statement")]
        public void WhatCannotBeConvertedGivesAReason(string sql, string reason)
        {
            var result = StatementPreview.ToSelect(sql);

            Assert.False(result.Succeeded);
            Assert.Contains(reason, result.Reason);
        }

        [Fact]
        public void TheDryRunWrapsTheStatementInATransactionThatIsRolledBack()
        {
            var result = StatementPreview.ToDryRun("UPDATE dbo.Users SET Name = 'x' WHERE Id = 1;", "\n");

            Assert.Equal("BEGIN TRANSACTION;\nUPDATE dbo.Users SET Name = 'x' WHERE Id = 1;\nSELECT @@ROWCOUNT AS [Rows affected];\nROLLBACK TRANSACTION;\n", result.Text);
        }

        [Fact]
        public void TheDryRunKeepsAnOutputClause()
        {
            var result = StatementPreview.ToDryRun("DELETE FROM t OUTPUT deleted.* WHERE a = 1", "\n");

            Assert.Contains("OUTPUT deleted.*", result.Text);
        }

        [Theory]
        [InlineData("SELECT 1")]
        [InlineData("UPDATE t SET a = 1; UPDATE u SET b = 2;")]
        public void TheDryRunRefusesWhatIsNoSingleDataChange(string sql)
        {
            Assert.False(StatementPreview.ToDryRun(sql).Succeeded);
        }
    }
}
