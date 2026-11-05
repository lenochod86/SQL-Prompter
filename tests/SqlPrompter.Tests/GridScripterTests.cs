using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class GridScripterTests
    {
        [Fact]
        public void RowsBecomeAMultiRowInsert()
        {
            var text = GridScripter.ToInsert("Id\tName\tNote\r\n1\tAnna\tNULL\r\n2\tO'Neil\t\r\n", "dbo.Users");

            Assert.Equal(
                "INSERT INTO dbo.Users (Id, Name, Note)\r\nVALUES\r\n    (1, N'Anna', NULL),\r\n    (2, N'O''Neil', N'');\r\n",
                text);
        }

        [Theory]
        [InlineData("0", "0")]
        [InlineData("42", "42")]
        [InlineData("-7", "-7")]
        [InlineData("3.14", "3.14")]
        [InlineData("007", "N'007'")]
        [InlineData("1e5", "N'1e5'")]
        [InlineData("1.", "N'1.'")]
        [InlineData(".5", "N'.5'")]
        [InlineData("2026-11-05 10:00:00.000", "N'2026-11-05 10:00:00.000'")]
        [InlineData("NULL", "NULL")]
        [InlineData("12345678901234567890", "N'12345678901234567890'")]
        [InlineData("", "N''")]
        public void ValuesAreWrittenByTheirLook(string value, string expected) => Assert.Equal(expected, GridScripter.Literal(value));

        [Fact]
        public void ColumnNamesThatNeedBracketsGetThem() =>
            Assert.Contains("([Order Id], [select])", GridScripter.ToInsert("Order Id\tselect\n1\t2", "t"));

        [Fact]
        public void BigResultsAreSplitIntoStatements()
        {
            var rows = string.Join("\n", System.Linq.Enumerable.Range(1, 1500));
            var text = GridScripter.ToInsert("a\n" + rows, "t");

            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, "INSERT INTO").Count);
            Assert.EndsWith("(1500);\r\n", text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Id\tName")]
        [InlineData(null)]
        public void WithoutRowsThereIsNothingToScript(string grid) => Assert.Null(GridScripter.ToInsert(grid, "t"));

        [Fact]
        public void AMissingTableGetsAPlaceholder() => Assert.StartsWith("INSERT INTO dbo.TableName (a)", GridScripter.ToInsert("a\n1", null));

        [Theory]
        [InlineData("SELECT * FROM dbo.Orders o WHERE 1 = 1", "dbo.Orders")]
        [InlineData("select a from [dbo].[Order Details] d", "[dbo].[Order Details]")]
        [InlineData("SELECT * FROM Orders", "Orders")]
        [InlineData("SELECT * FROM (SELECT 1 AS a) t", null)]
        [InlineData("SELECT 1", null)]
        [InlineData("-- FROM x\nSELECT * FROM db.dbo.T", "db.dbo.T")]
        public void TheTableIsTheFirstOneAfterFrom(string query, string expected) => Assert.Equal(expected, GridScripter.GuessTable(query));
    }
}
