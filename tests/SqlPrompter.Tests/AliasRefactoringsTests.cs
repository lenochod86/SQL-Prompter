using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class AliasRefactoringsTests
    {
        [Theory]
        [InlineData("SELECT 'a', N'b', 'it''s' FROM t WHERE x = 'c'", "SELECT N'a', N'b', N'it''s' FROM t WHERE x = N'c'")]
        [InlineData("SELECT '' -- 'x'", "SELECT N'' -- 'x'")]
        [InlineData("SELECT 1", "SELECT 1")]
        public void StringsGetTheNPrefix(string input, string expected) => Assert.Equal(expected, AliasRefactorings.AddNPrefix(input));

        [Theory]
        [InlineData("SELECT a.Id i, Name n FROM dbo.Users u", "SELECT a.Id AS i, Name AS n FROM dbo.Users AS u")]
        [InlineData("SELECT a.Id, Name FROM dbo.Users", "SELECT a.Id, Name FROM dbo.Users")]
        [InlineData("SELECT x = a + b, @v = c, COUNT(*) cnt FROM t", "SELECT x = a + b, @v = c, COUNT(*) AS cnt FROM t")]
        [InlineData("SELECT DISTINCT a total FROM t", "SELECT DISTINCT a AS total FROM t")]
        [InlineData("SELECT TOP 5 a total FROM t", "SELECT TOP 5 a AS total FROM t")]
        [InlineData("SELECT a IS NULL, b COLLATE x FROM t", "SELECT a IS NULL, b COLLATE x FROM t")]
        [InlineData("SELECT CASE WHEN a = 1 THEN 2 END c FROM t", "SELECT CASE WHEN a = 1 THEN 2 END AS c FROM t")]
        [InlineData("SELECT a [my col] FROM t", "SELECT a AS [my col] FROM t")]
        [InlineData("SELECT * FROM t a, v c JOIN u b ON b.x = a.x", "SELECT * FROM t AS a, v AS c JOIN u AS b ON b.x = a.x")]
        [InlineData("SELECT * FROM (SELECT 1 x) d WHERE d.x = 1", "SELECT * FROM (SELECT 1 AS x) AS d WHERE d.x = 1")]
        [InlineData("SELECT * FROM t WITH (NOLOCK) WHERE 1 = 1", "SELECT * FROM t WITH (NOLOCK) WHERE 1 = 1")]
        [InlineData("SELECT * FROM dbo.f(1) x CROSS APPLY dbo.g(x.a) y", "SELECT * FROM dbo.f(1) AS x CROSS APPLY dbo.g(x.a) AS y")]
        [InlineData("SELECT a FROM t AS x", "SELECT a FROM t AS x")]
        public void AsIsWrittenBeforeAliases(string input, string expected) => Assert.Equal(expected, AliasRefactorings.AddAs(input));

        [Theory]
        [InlineData("SELECT a.Id AS i, Name AS [n] FROM dbo.Users AS u", "SELECT a.Id i, Name [n] FROM dbo.Users u")]
        [InlineData("SELECT CAST(a AS int), CONVERT(int, b) AS c FROM t", "SELECT CAST(a AS int), CONVERT(int, b) c FROM t")]
        [InlineData("WITH c AS (SELECT 1 AS x) SELECT * FROM c", "WITH c AS (SELECT 1 x) SELECT * FROM c")]
        [InlineData("DECLARE @a AS int", "DECLARE @a AS int")]
        [InlineData("CREATE PROCEDURE p @a int AS SELECT 1", "CREATE PROCEDURE p @a int AS SELECT 1")]
        [InlineData("SELECT x AS date FROM t", "SELECT x AS date FROM t")]
        [InlineData("SELECT x AS /* c */ y FROM t", "SELECT x AS /* c */ y FROM t")]
        public void AsIsRemovedOnlyFromAliases(string input, string expected) => Assert.Equal(expected, AliasRefactorings.RemoveAs(input));

        [Fact]
        public void AddingAndRemovingAsRoundTrips()
        {
            var sql = "SELECT a.Id i, COUNT(*) cnt FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id GROUP BY a.Id";

            Assert.Equal(sql, AliasRefactorings.RemoveAs(AliasRefactorings.AddAs(sql)));
        }
    }
}
