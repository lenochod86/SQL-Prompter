using System.Linq;
using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The copy formats of the results grid (#211).</summary>
    public class GridFormatsTests
    {
        private const string Grid = "Id\tName\tCreated\tNote\n1\tAnna\t2025-01-02 10:00:00\tit's\n2\tBohuš\t2025-02-03 11:30:00\tNULL\n3\tAnna\tNULL\tsay \"hi\"\n";

        [Fact]
        public void TheInListQuotesStringsAndKeepsNumbersBare()
        {
            Assert.Equal("IN (1, 2, 3)", GridFormats.AsInList(Grid, newLine: "\n"));
            Assert.Equal("IN ('Anna', N'Bohuš')", GridFormats.AsInList("Name\nAnna\nBohuš\nAnna\n", newLine: "\n"));
        }

        [Fact]
        public void ValuesAreEscapedAndNullStaysNull()
        {
            var text = "Note\nit's\nNULL\n";

            Assert.Equal("IN ('it''s', NULL)", GridFormats.AsInList(text, newLine: "\n"));
        }

        [Fact]
        public void DuplicatesAreRemovedByDefaultAndKeptOnRequest()
        {
            var text = "Name\nAnna\nAnna\nBo\n";

            Assert.Equal("IN ('Anna', 'Bo')", GridFormats.AsInList(text, newLine: "\n"));
            Assert.Equal("IN ('Anna', 'Anna', 'Bo')", GridFormats.AsInList(text, distinct: false, newLine: "\n"));
        }

        [Fact]
        public void ALongListIsWrappedAtTheLineWidth()
        {
            var text = "Id\n" + string.Join("\n", Enumerable.Range(1000, 30)) + "\n";

            var lines = GridFormats.AsInList(text, lineWidth: 40, newLine: "\n").Split('\n');

            Assert.True(lines.Length > 3);
            Assert.Equal("IN (", lines[0]);
            Assert.Equal(")", lines[lines.Length - 1]);
            var inner = lines.Skip(1).Take(lines.Length - 2).ToList();
            Assert.All(inner, l => Assert.True(l.Length <= 40 && l.StartsWith("    ")));
            Assert.EndsWith(",", inner[0]);
            Assert.Equal(Enumerable.Range(1000, 30).Select(n => n.ToString()), string.Join(" ", inner).Replace(",", string.Empty).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        [Fact]
        public void ARowsAsValues()
        {
            var text = "Id\tName\n1\tAnna\n2\tNULL\n";

            Assert.Equal("VALUES\n(1, 'Anna'),\n(2, NULL)", GridFormats.AsValues(text, "\n"));
        }

        [Fact]
        public void Json()
        {
            var text = "Id\tName\tNote\n1\tAnna\tsay \"hi\"\n2\tNULL\tline\\x\n";

            Assert.Equal("[\n  {\"Id\": 1, \"Name\": \"Anna\", \"Note\": \"say \\\"hi\\\"\"},\n  {\"Id\": 2, \"Name\": null, \"Note\": \"line\\\\x\"}\n]", GridFormats.AsJson(text, "\n"));
        }

        [Fact]
        public void Csv()
        {
            var text = "Id\tName\tNote\n1\tAnna, Jr.\tsay \"hi\"\n2\tNULL\tplain\n";

            Assert.Equal("Id,Name,Note\n1,\"Anna, Jr.\",\"say \"\"hi\"\"\"\n2,,plain", GridFormats.AsCsv(text, "\n"));
        }

        [Fact]
        public void Markdown()
        {
            var text = "Id\tName\n1\ta|b\n22\tNULL\n";

            Assert.Equal("| Id | Name |\n| ---: | --- |\n| 1 | a\\|b |\n| 22 | NULL |", GridFormats.AsMarkdown(text, "\n"));
        }

        [Fact]
        public void ColumnNamesAreMadeUniqueAndNonEmpty()
        {
            Assert.StartsWith("Id,Id_2,Column3", GridFormats.AsCsv("Id\tId\t(No column name)\n1\t2\t3\n"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("OnlyAHeader\n")]
        public void NoRowsGiveNull(string text)
        {
            Assert.Null(GridFormats.AsInList(text));
            Assert.Null(GridFormats.AsValues(text));
            Assert.Null(GridFormats.AsJson(text));
            Assert.Null(GridFormats.AsCsv(text));
            Assert.Null(GridFormats.AsMarkdown(text));
        }

        [Fact]
        public void ADateColumnIsQuoted()
        {
            Assert.Equal("IN ('2025-01-02 10:00:00', 'x')", GridFormats.AsInList("D\n2025-01-02 10:00:00\nx\n", newLine: "\n"));
        }
    }
}
