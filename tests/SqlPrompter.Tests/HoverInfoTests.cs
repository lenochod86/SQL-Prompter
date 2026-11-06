using SqlPrompter.Core.Navigation;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The tooltip over a name (#220).</summary>
    public class HoverInfoTests
    {
        private static string Hover(string marked)
        {
            var caret = marked.IndexOf('|');
            var sql = marked.Remove(caret, 1);
            return HoverInfo.At(sql, caret, SampleDatabase())?.Text;
        }

        [Fact]
        public void ATableShowsItsColumnCount() =>
            Assert.Equal("Table dbo.Users\n3 columns", Hover("SELECT * FROM dbo.Us|ers"));

        [Fact]
        public void AViewIsNamedAsAView() =>
            Assert.StartsWith("View dbo.ActiveUsers", Hover("SELECT * FROM Active|Users"));

        [Fact]
        public void AnAliasShowsItsTable() =>
            Assert.Equal("Alias u for Table dbo.Users", Hover("SELECT * FROM dbo.Users |u"));

        [Fact]
        public void ACaretInAnAliasUsedInTheSelectListIsResolvedToo() =>
            Assert.StartsWith("Alias u for", Hover("SELECT |u.Id FROM dbo.Users u"));

        [Fact]
        public void AQualifiedColumnShowsTypeAndTable() =>
            Assert.Equal("Column u.Name: int NOT NULL\nof dbo.Users", Hover("SELECT u.Na|me FROM dbo.Users u"));

        [Fact]
        public void AnUnqualifiedColumnOfOneSourceIsRecognised() =>
            Assert.Equal("Column Email: int NOT NULL\nof dbo.Users", Hover("SELECT Em|ail FROM dbo.Users"));

        [Fact]
        public void AnAmbiguousColumnShowsNothing() =>
            Assert.Null(Hover("SELECT I|d FROM dbo.Users u JOIN dbo.Orders o ON 1 = 1"));

        [Fact]
        public void AVariableShowsItsDeclaration()
        {
            Assert.Equal("Variable @n: nvarchar(20)", Hover("DECLARE @n nvarchar(20) = N'x'; SELECT @|n"));
            Assert.Equal("Variable @b: int", Hover("DECLARE @a int, @b int; SELECT @|b"));
        }

        [Fact]
        public void KeywordsNumbersAndUnknownNamesShowNothing()
        {
            Assert.Null(Hover("SEL|ECT 1 FROM dbo.Users"));
            Assert.Null(Hover("SELECT |1"));
            Assert.Null(Hover("SELECT * FROM dbo.Nothi|ng"));
            Assert.Null(Hover("SELECT * FROM #te|mp"));
            Assert.Null(Hover("SELECT @unk|nown"));
        }

        [Fact]
        public void TheSpanIsTheNameUnderTheMouse()
        {
            var sql = "SELECT u.Name FROM dbo.Users u";
            var info = HoverInfo.At(sql, sql.IndexOf("Name") + 1, SampleDatabase());

            Assert.Equal("Name", sql.Substring(info.Start, info.Length));
        }

        [Fact]
        public void NoMetadataMeansNoTooltip() =>
            Assert.Null(HoverInfo.At("SELECT 1", 3, null));
    }
}
