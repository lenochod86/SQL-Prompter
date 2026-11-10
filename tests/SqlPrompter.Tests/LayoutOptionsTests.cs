using System.Linq;
using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Layout options taken over from a SQL Prompt style (#305): items across clauses, JOIN table, equals sign of SET and BEGIN / END.</summary>
    public class LayoutOptionsTests
    {
        private static string[] Lines(string sql, FormatStyle style)
        {
            style.UseTabs = false;
            return SqlFormatter.Format(sql, style).Text.Replace("\r\n", "\n").Split('\n');
        }

        private const string Select = "select a.Id,a.Name from dbo.Accounts a inner join dbo.Cities c on c.Id=a.IdCity where a.Active=1 and c.Name='x' group by a.Id order by a.Id;";

        [Fact]
        public void ItemsOfAllClausesStartInOneColumnAcrossClauses()
        {
            var lines = Lines(Select, new FormatStyle { AlignAcrossClauses = true, CollapseStatementsShorterThan = 0 });

            Assert.StartsWith("SELECT      a.Id", lines[0]);
            Assert.Contains(lines, l => l.StartsWith("FROM        dbo.Accounts a"));
            Assert.Contains(lines, l => l.StartsWith("WHERE       a.Active"));
            Assert.Contains(lines, l => l.StartsWith("GROUP BY    a.Id"));
        }

        [Fact]
        public void WithoutTheOptionEachClauseUsesItsOwnKeyword()
        {
            var lines = Lines(Select, new FormatStyle { CollapseStatementsShorterThan = 0 });

            Assert.StartsWith("SELECT  a.Id", lines[0]);
            Assert.Contains(lines, l => l.StartsWith("FROM    dbo.Accounts a"));
        }

        [Fact]
        public void JoinIsIndentedToTheTableAfterFrom()
        {
            var lines = Lines(Select, new FormatStyle { IndentJoinTable = true, CollapseStatementsShorterThan = 0 });

            Assert.Contains(lines, l => l.StartsWith("        INNER JOIN"));
            Assert.Contains(lines, l => l.StartsWith("FROM    dbo.Accounts a"));
        }

        [Fact]
        public void TheEqualsSignOfALongSetMovesToTheNextLine()
        {
            var sql = "set @x=(select max(Id) from dbo.Accounts where Active=1 and City='Prague' and Rating>4 and Created>'2020');";

            var on = Lines(sql, new FormatStyle { EqualsOnNewLine = true });
            var off = Lines(sql, new FormatStyle());

            Assert.Equal("SET @x", on[0]);
            Assert.StartsWith("    = (SELECT", on[1]);
            Assert.Single(off);
        }

        [Fact]
        public void AShortSetStaysOnOneLine()
        {
            Assert.Equal(new[] { "SET @y = 1;" }, Lines("set @y=1;", new FormatStyle { EqualsOnNewLine = true }));
        }

        [Fact]
        public void BeginAndEndAreIndentedUnderTheIf()
        {
            var lines = Lines("if @x>0 begin select 1 end else begin select 2 end", new FormatStyle { IndentBeginEnd = true });

            Assert.Equal(new[] { "IF @x > 0", "    BEGIN", "        SELECT 1", "    END", "ELSE", "    BEGIN", "        SELECT 2", "    END" }, lines);
        }

        [Fact]
        public void AStandaloneBlockKeepsBeginAtTheLevelAndIndentsTheContents()
        {
            Assert.Equal(new[] { "BEGIN", "    SELECT 3", "END" }, Lines("begin select 3 end", new FormatStyle { IndentBeginEnd = true }));
        }

        [Fact]
        public void TheImportSetsTheOptions()
        {
            var style = SqlPromptStyleImporter.Import(@"{ ""lists"": { ""alignItemsAcrossClauses"": true }, ""joinStatements"": { ""join"": { ""indentJoinTable"": true } }, ""variables"": { ""placeEqualsSignOnNewLine"": true }, ""controlFlow"": { ""indentBeginAndEndKeywords"": true } }").Style;

            Assert.True(style.AlignAcrossClauses);
            Assert.True(style.IndentJoinTable);
            Assert.True(style.EqualsOnNewLine);
            Assert.True(style.IndentBeginEnd);
        }
    }
}
