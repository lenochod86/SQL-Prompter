using System.Linq;
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

        [Fact]
        public void TheSelectionBecomesACreateInsertDropScript()
        {
            var text = GridScripter.ToTemporaryTable("Id\tName\tNote\r\n1\tAnna\tNULL\r\n2\tO'Neil\t\r\n");

            Assert.Equal(
                "CREATE TABLE #temptable (\r\n    Id int NULL,\r\n    Name nvarchar(50) NULL,\r\n    Note nvarchar(max) NULL\r\n);\r\n\r\n" +
                "INSERT INTO #temptable (Id, Name, Note)\r\nVALUES\r\n    (1, N'Anna', NULL),\r\n    (2, N'O''Neil', N'');\r\n\r\n" +
                "-- Work with the data here.\r\n\r\nDROP TABLE #temptable;\r\n",
                text);
        }

        [Theory]
        [InlineData("1|2|-3", "int")]
        [InlineData("1|3000000000", "bigint")]
        [InlineData("1.5|22.25", "decimal(4, 2)")]
        [InlineData("-10.5|2", "decimal(3, 1)")]
        [InlineData("2026-11-05|2026-01-01", "date")]
        [InlineData("2026-11-05 10:00:00|2026-11-05 10:00:00.123", "datetime2(3)")]
        [InlineData("0e0e8c2e-1f5a-4a2e-9a39-3c2b3c0e9a11|NULL", "uniqueidentifier")]
        [InlineData("007|8", "nvarchar(50)")]
        [InlineData("1|abc", "nvarchar(50)")]
        [InlineData("NULL|", "nvarchar(max)")]
        public void TypesFitTheValues(string values, string expected) => Assert.Equal(expected, GridScripter.InferType(values.Split('|')));

        [Fact]
        public void LongTextsGetRoomOrMax()
        {
            Assert.Equal("nvarchar(100)", GridScripter.InferType(new[] { new string('x', 51) }));
            Assert.Equal("nvarchar(max)", GridScripter.InferType(new[] { new string('x', 2001) }));
        }

        [Fact]
        public void ColumnNamesAreUniqueAndNeverEmpty()
        {
            var text = GridScripter.ToTemporaryTable("IdCountry\tName\tIdCountry\t(No column name)\t\r\n1\ta\t2\tb\tc\r\n");

            Assert.Contains("IdCountry int NULL,\r\n    Name nvarchar(50) NULL,\r\n    IdCountry_2 int NULL,\r\n    Column4 nvarchar(50) NULL,\r\n    Column5 nvarchar(50) NULL\r\n);", text);
            Assert.Contains("INSERT INTO #temptable (IdCountry, Name, IdCountry_2, Column4, Column5)", text);
        }

        [Fact]
        public void EmptyCellsInNumberColumnsAreNull()
        {
            var text = GridScripter.ToTemporaryTable("a\tb\r\n1\tx\r\n\ty\r\n");

            Assert.Contains("    (1, N'x'),\r\n    (NULL, N'y');", text);
        }

        [Fact]
        public void ManyRowsAreSplitAndTheTableIsDroppedOnce()
        {
            var rows = string.Join("\r\n", System.Linq.Enumerable.Range(1, GridScripter.RowsPerStatement + 5));
            var text = GridScripter.ToTemporaryTable("a\r\n" + rows);

            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, "INSERT INTO #temptable").Count);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "DROP TABLE"));
        }

        [Fact]
        public void NothingToScriptWithoutRows() => Assert.Null(GridScripter.ToTemporaryTable("a"));
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

        // Numbers copied with the decimal comma of the user's culture (#530).

        [Fact]
        public void NumbersWithADecimalCommaBecomeDecimalColumnsWrittenWithAPoint()
        {
            var text = GridScripter.ToTemporaryTable("Fl\tMn\r\n1,25\t12,50\r\n100000,5\t3,00\r\n", ",");

            Assert.Contains("Fl decimal(8, 2) NULL", text);
            Assert.Contains("Mn decimal(4, 2) NULL", text);
            Assert.Contains("(1.25, 12.50)", text);
            Assert.Contains("(100000.5, 3.00)", text);
            Assert.DoesNotContain("N'1,25'", text);
        }

        [Fact]
        public void TheDecimalCommaIsOnlyAnumberWhenEveryValueOfTheColumnIsOne()
        {
            var text = GridScripter.ToTemporaryTable("Note\r\n1,5\r\nabc, def\r\n", ",");

            Assert.Contains("Note nvarchar(50) NULL", text);
            Assert.Contains("N'1,5'", text);
        }

        [Fact]
        public void TheDefaultSeparatorLeavesACommaAlone()
        {
            var text = GridScripter.ToTemporaryTable("A\r\n1,25\r\n");

            Assert.Contains("A nvarchar(50) NULL", text);
            Assert.Contains("N'1,25'", text);
        }

        [Theory]
        [InlineData("1,5E+20|2", ",", "float")]
        [InlineData("1.5E-05|0.25", ".", "float")]
        [InlineData("-3,25|4", ",", "decimal(3, 2)")]
        public void NumbersWithAnExponentAreFloat(string values, string separator, string expected) =>
            Assert.Equal(expected, GridScripter.InferType(values.Split('|').Select(v => GridScripter.Normalize(v, separator))));

        [Fact]
        public void FloatValuesKeepTheirExponentInTheScript()
        {
            var text = GridScripter.ToTemporaryTable("F\r\n1,5E+20\r\n2\r\n", ",");

            Assert.Contains("F float NULL", text);
            Assert.Contains("(1.5E+20)", text);
            Assert.Contains("(2)", text);
        }

        [Fact]
        public void BinaryValuesBecomeVarbinaryWithBinaryLiterals()
        {
            var text = GridScripter.ToTemporaryTable("B\r\n0x0102\r\nNULL\r\n");

            Assert.Contains("B varbinary(max) NULL", text);
            Assert.Contains("(0x0102)", text);
            Assert.Contains("(NULL)", text);
        }

        [Fact]
        public void NormalizeChangesOnlyNumbersWithOneSeparator()
        {
            Assert.Equal("1.25", GridScripter.Normalize("1,25", ","));
            Assert.Equal("-0.5", GridScripter.Normalize("-0,5", ","));
            Assert.Equal("1,2,3", GridScripter.Normalize("1,2,3", ","));
            Assert.Equal("a,b", GridScripter.Normalize("a,b", ","));
            Assert.Equal("1,25", GridScripter.Normalize("1,25", "."));
        }
    }
}
