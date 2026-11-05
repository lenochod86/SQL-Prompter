using SqlPrompter.Core.Editing;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class NameQualifierTests
    {
        private static string Run(string sql, out int count) => NameQualifier.Qualify(sql, 0, sql.Length, SampleDatabase(), out count);

        [Theory]
        [InlineData("SELECT * FROM Users", "SELECT * FROM dbo.Users")]
        [InlineData("SELECT * FROM Users u JOIN Orders o ON o.UserId = u.Id", "SELECT * FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id")]
        [InlineData("SELECT * FROM [Order Details]", "SELECT * FROM dbo.[Order Details]")]
        [InlineData("SELECT * FROM dbo.Users", "SELECT * FROM dbo.Users")]
        [InlineData("SELECT * FROM Users, Orders", "SELECT * FROM dbo.Users, dbo.Orders")]
        [InlineData("SELECT * FROM Nothing", "SELECT * FROM Nothing")]
        [InlineData("SELECT * FROM Customers", "SELECT * FROM Customers")]
        [InlineData("INSERT INTO Users (Name) VALUES ('a')", "INSERT INTO dbo.Users (Name) VALUES ('a')")]
        [InlineData("UPDATE Users SET Name = 'a'", "UPDATE dbo.Users SET Name = 'a'")]
        [InlineData("DELETE FROM Orders WHERE Id = 1", "DELETE FROM dbo.Orders WHERE Id = 1")]
        [InlineData("DELETE o FROM dbo.Orders o", "DELETE o FROM dbo.Orders o")]
        [InlineData("UPDATE u SET Name = 'a' FROM Users u", "UPDATE u SET Name = 'a' FROM dbo.Users u")]
        [InlineData("TRUNCATE TABLE Orders", "TRUNCATE TABLE dbo.Orders")]
        [InlineData("WITH Users AS (SELECT 1 x) SELECT * FROM Users", "WITH Users AS (SELECT 1 x) SELECT * FROM Users")]
        [InlineData("SELECT * FROM #Users, @t", "SELECT * FROM #Users, @t")]
        [InlineData("SELECT * FROM ActiveUsers -- Users\nWHERE 1 = 1", "SELECT * FROM dbo.ActiveUsers -- Users\nWHERE 1 = 1")]
        public void NamesGetTheirSchema(string input, string expected) => Assert.Equal(expected, Run(input, out _));

        [Fact]
        public void OnlyNamesInTheRangeAreQualified()
        {
            var sql = "SELECT * FROM Users\nSELECT * FROM Orders";
            var result = NameQualifier.Qualify(sql, 0, 20, SampleDatabase(), out var count);

            Assert.Equal(1, count);
            Assert.Equal("SELECT * FROM dbo.Users\nSELECT * FROM Orders", result);
        }
    }
}
