using System.Linq;
using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>INSERT columns and rows, CASE expressions and the contents of IN (#305).</summary>
    public class InsertCaseInTests
    {
        private static string[] Lines(string sql, FormatStyle style)
        {
            style.UseTabs = false;
            return SqlFormatter.Format(sql, style).Text.Replace("\r\n", "\n").Split('\n');
        }

        private const string Insert = "insert into dbo.Accounts (Id,Name,City) values (1,'a','b'),(2,'c','d');";

        [Fact]
        public void InsertColumnsStayOnOneLineByDefault()
        {
            Assert.Equal("INSERT INTO dbo.Accounts (Id, Name, City)", Lines(Insert, new FormatStyle { CollapseStatementsShorterThan = 0 })[0]);
        }

        [Fact]
        public void InsertColumnsCanBeBrokenAlwaysOrOnlyWhenLong()
        {
            var always = Lines(Insert, new FormatStyle { CollapseStatementsShorterThan = 0, InsertColumnsBreakLength = 1 });
            var longOnly = Lines(Insert, new FormatStyle { CollapseStatementsShorterThan = 0, InsertColumnsBreakLength = 120 });
            var shortLimit = Lines(Insert, new FormatStyle { CollapseStatementsShorterThan = 0, InsertColumnsBreakLength = 20 });

            Assert.Equal(new[] { "INSERT INTO dbo.Accounts (", "      Id", "    , Name", "    , City", ")" }, always.Take(5).ToArray());
            Assert.Equal("INSERT INTO dbo.Accounts (Id, Name, City)", longOnly[0]);
            Assert.Equal("INSERT INTO dbo.Accounts (", shortLimit[0]);
        }

        [Fact]
        public void TheInsertParenthesisCanHaveALineOfItsOwn()
        {
            var lines = Lines(Insert, new FormatStyle { CollapseStatementsShorterThan = 0, InsertColumnsBreakLength = 1, InsertParenOnNewLine = true });

            Assert.Equal(new[] { "INSERT INTO dbo.Accounts", "(", "      Id" }, lines.Take(3).ToArray());
        }

        [Fact]
        public void ValueRowsStayOnTheValuesLineWhenTurnedOff()
        {
            var off = Lines(Insert, new FormatStyle { CollapseStatementsShorterThan = 0, InsertValuesOnNewLines = false });
            var on = Lines(Insert, new FormatStyle { CollapseStatementsShorterThan = 0 });

            Assert.Equal(2, off.Length);
            Assert.Contains("(1, 'a', 'b'), (2, 'c', 'd')", off[1]);
            Assert.Equal(3, on.Length);
        }

        private const string Case = "select case when a.Rating>5 then 'high' when a.Rating>2 then 'medium' else 'low' end as Level from dbo.Accounts a;";

        [Fact]
        public void ALongCaseGetsALineForEveryWhenAndElse()
        {
            var lines = Lines(Case, new FormatStyle { CollapseStatementsShorterThan = 0 });

            Assert.Equal("SELECT  CASE", lines[0]);
            Assert.Equal("            WHEN a.Rating > 5 THEN 'high'", lines[1]);
            Assert.Equal("            ELSE 'low'", lines[3]);
            Assert.Equal("        END AS Level", lines[4]);
        }

        [Fact]
        public void AShortCaseOrAHighLimitKeepsItOnOneLine()
        {
            Assert.Contains("CASE WHEN a.X = 1 THEN 1 END", Lines("select case when a.X=1 then 1 end as S from dbo.A a;", new FormatStyle { CollapseStatementsShorterThan = 0 })[0]);
            Assert.Contains("CASE WHEN a.Rating > 5", Lines(Case, new FormatStyle { CollapseStatementsShorterThan = 0, CollapseCaseShorterThan = 500 })[0]);
        }

        [Fact]
        public void ZeroBreaksEveryCase()
        {
            var lines = Lines("select case when a.X=1 then 1 end as S from dbo.A a;", new FormatStyle { CollapseStatementsShorterThan = 0, CollapseCaseShorterThan = 0 });

            Assert.Equal("SELECT  CASE", lines[0]);
        }

        [Fact]
        public void TheExpressionOfASimpleCaseCanBeOnTheNextLine()
        {
            var sql = "select case a.City when 'Prague' then 'capital' when 'Brno' then 'big city' else 'other' end as K from dbo.A a;";

            var same = Lines(sql, new FormatStyle { CollapseStatementsShorterThan = 0 });
            var next = Lines(sql, new FormatStyle { CollapseStatementsShorterThan = 0, CaseExpressionOnNewLine = true });

            Assert.Equal("SELECT  CASE a.City", same[0]);
            Assert.Equal("SELECT  CASE", next[0]);
            Assert.Equal("            a.City", next[1]);
        }

        [Fact]
        public void InCanHaveSpacesInsideItsParentheses()
        {
            var sql = "select 1 from dbo.A a where a.City in ('x','y');";

            Assert.Contains("a.City IN ( 'x', 'y' )", Lines(sql, new FormatStyle { SpaceAroundInContents = true, CollapseStatementsShorterThan = 0 })[2]);
            Assert.Contains("a.City IN ('x', 'y')", Lines(sql, new FormatStyle { CollapseStatementsShorterThan = 0 })[2]);
            Assert.Contains("IN ( 'x', 'y' )", SqlFormatter.Format(sql, new FormatStyle { SpaceAroundInContents = true }).Text);
        }

        [Fact]
        public void TheImportSetsTheOptions()
        {
            var style = SqlPromptStyleImporter.Import(@"{ ""insertStatements"": { ""columns"": { ""parenthesisStyle"": ""expandedToStatement"", ""placeSubsequentColumnsOnNewLines"": ""always"" }, ""values"": { ""placeSubsequentValuesOnNewLines"": ""never"" } },
                ""caseExpressions"": { ""placeExpressionOnNewLine"": true, ""collapseShortCaseExpressions"": true, ""collapseCaseExpressionsShorterThan"": 50 },
                ""operators"": { ""in"": { ""addSpaceAroundInContents"": true } } }").Style;

            Assert.True(style.InsertParenOnNewLine);
            Assert.Equal(1, style.InsertColumnsBreakLength);
            Assert.False(style.InsertValuesOnNewLines);
            Assert.True(style.CaseExpressionOnNewLine);
            Assert.Equal(50, style.CollapseCaseShorterThan);
            Assert.True(style.SpaceAroundInContents);
            Assert.Equal(style.MaxLineLength, SqlPromptStyleImporter.Import(@"{ ""insertStatements"": { ""columns"": { ""placeSubsequentColumnsOnNewLines"": ""ifLongerThanMaxLineLength"" } } }").Style.InsertColumnsBreakLength);
            Assert.Equal(0, SqlPromptStyleImporter.Import(@"{ ""insertStatements"": { ""columns"": { ""placeSubsequentColumnsOnNewLines"": ""never"" } } }").Style.InsertColumnsBreakLength);
            Assert.Equal(0, SqlPromptStyleImporter.Import(@"{ ""caseExpressions"": { ""collapseShortCaseExpressions"": false } }").Style.CollapseCaseShorterThan);
        }
    }
}
