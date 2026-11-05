using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class RefactoringsTests
    {
        [Theory]
        [InlineData("SELECT [Id], [Name] FROM [dbo].[Users]", "SELECT Id, Name FROM dbo.Users")]
        [InlineData("SELECT [Order Id] FROM [dbo].[Order Details]", "SELECT [Order Id] FROM dbo.[Order Details]")]
        [InlineData("SELECT [select], [Group] FROM t", "SELECT [select], [Group] FROM t")]
        [InlineData("SELECT [a]]b] FROM t", "SELECT [a]]b] FROM t")]
        [InlineData("SELECT 1 AS[x]", "SELECT 1 AS[x]")]
        [InlineData("SELECT 'a [b]' -- [c]\n, [d]", "SELECT 'a [b]' -- [c]\n, d")]
        [InlineData("[Open]\nGO\n[Go]", "[Open]\nGO\n[Go]")]
        [InlineData("SELECT [#t].[x] FROM [#t]", "SELECT [#t].x FROM [#t]")]
        [InlineData("SELECT [Id", "SELECT [Id")]
        public void BracketsAreRemovedOnlyWhenSafe(string input, string expected) => Assert.Equal(expected, Refactorings.RemoveBrackets(input));

        [Theory]
        [InlineData("SELECT 1\nSELECT 2", "SELECT 1;\nSELECT 2;")]
        [InlineData("SELECT 1; SELECT 2", "SELECT 1; SELECT 2;")]
        [InlineData("SELECT 1 -- note\nGO\nSELECT 2", "SELECT 1; -- note\nGO\nSELECT 2;")]
        [InlineData("SELECT * FROM t\nWITH c AS (SELECT 1 x) SELECT * FROM c", "SELECT * FROM t;\nWITH c AS (SELECT 1 x) SELECT * FROM c;")]
        [InlineData("IF @a = 1\n    SELECT 1\nELSE\n    SELECT 2", "IF @a = 1\n    SELECT 1\nELSE\n    SELECT 2;")]
        [InlineData("BEGIN\n    SELECT 1\nEND", "BEGIN\n    SELECT 1;\nEND;")]
        [InlineData("BEGIN TRY\n    SELECT 1\nEND TRY\nBEGIN CATCH\n    SELECT 2\nEND CATCH", "BEGIN TRY\n    SELECT 1;\nEND TRY\nBEGIN CATCH\n    SELECT 2;\nEND CATCH;")]
        [InlineData("BEGIN TRAN\nDELETE t\nCOMMIT", "BEGIN TRAN;\nDELETE t;\nCOMMIT;")]
        [InlineData("WHILE @i < 3\nBEGIN\n    SET @i += 1\nEND", "WHILE @i < 3\nBEGIN\n    SET @i += 1;\nEND;")]
        [InlineData("CREATE PROCEDURE p @a int AS\nSELECT @a", "CREATE PROCEDURE p @a int AS\nSELECT @a;")]
        [InlineData("DECLARE @a int = 1\nSELECT @a", "DECLARE @a int = 1;\nSELECT @a;")]
        [InlineData("SELECT *", "SELECT *;")]
        [InlineData("SELECT 1 +", "SELECT 1 +")]
        [InlineData("", "")]
        public void SemicolonsEndTheStatements(string input, string expected) => Assert.Equal(expected, Refactorings.InsertSemicolons(input));

        [Fact]
        public void SemicolonsAreIdempotent()
        {
            var once = Refactorings.InsertSemicolons("IF 1 = 1\nSELECT 1\nELSE\nSELECT 2\nSELECT 3");
            Assert.Equal(once, Refactorings.InsertSemicolons(once));
        }

        [Theory]
        [InlineData("DECLARE @a int\nSELECT 1", "SELECT 1")]
        [InlineData("DECLARE @a int;\nSELECT 1", "SELECT 1")]
        [InlineData("DECLARE @a int, @b int\nSELECT @b", "DECLARE @b int\nSELECT @b")]
        [InlineData("DECLARE @a int = 1, @b varchar(10) = 'x', @c int\nSELECT @a, @c", "DECLARE @a int = 1, @c int\nSELECT @a, @c")]
        [InlineData("DECLARE @a int\nSELECT @a", "DECLARE @a int\nSELECT @a")]
        [InlineData("DECLARE @t TABLE (Id int)\nSELECT 1", "SELECT 1")]
        [InlineData("DECLARE @a int\nGO\nSELECT @a", "GO\nSELECT @a")]
        public void UnusedVariablesAreRemoved(string input, string expected) => Assert.Equal(expected, Refactorings.RemoveUnusedVariables(input));
    }
}
