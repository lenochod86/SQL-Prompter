using System.Linq;
using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Parentheses of subqueries, CTEs, CREATE TABLE and procedure headers (#305).</summary>
    public class ParenthesisLayoutTests
    {
        private static string[] Lines(string sql, FormatStyle style)
        {
            style.UseTabs = false;
            style.CollapseSubqueriesShorterThan = 10;
            style.CollapseDdlShorterThan = 10;
            return SqlFormatter.Format(sql, style).Text.Replace("\r\n", "\n").Split('\n');
        }

        private const string Sub = "select a.Id from dbo.Accounts a where a.Id in (select b.IdAccount from dbo.Balances b where b.Amount>1000 and b.Active=1) and a.X=1;";

        [Fact]
        public void ASubqueryParenthesisEndsTheLineByDefault()
        {
            var lines = Lines(Sub, new FormatStyle { CollapseStatementsShorterThan = 0 });

            Assert.Contains(lines, l => l.EndsWith("IN ("));
            Assert.Contains(lines, l => l.StartsWith("            SELECT"));
        }

        [Fact]
        public void ASubqueryParenthesisCanHaveALineOfItsOwn()
        {
            var lines = Lines(Sub, new FormatStyle { CollapseStatementsShorterThan = 0, ParenOnNewLine = true });

            var open = System.Array.FindIndex(lines, l => l.TrimEnd() == "        (");
            Assert.True(open > 0);
            Assert.EndsWith("IN", lines[open - 1]);
        }

        [Fact]
        public void TheContentsOfASubqueryCanStayUnderTheParenthesis()
        {
            var lines = Lines(Sub, new FormatStyle { CollapseStatementsShorterThan = 0, IndentParenContents = false });

            Assert.Contains(lines, l => l.StartsWith("        SELECT") && !l.StartsWith("         "));
        }

        private const string Cte = "with c as (select a.Id,a.Name from dbo.Accounts a where a.Active=1 and a.City='x') select * from c;";

        [Fact]
        public void ACteParenthesisIsOnItsOwnLineByDefaultAndCanEndTheLine()
        {
            Assert.Contains(Lines(Cte, new FormatStyle { CollapseStatementsShorterThan = 0 }), l => l == "(");
            var compact = Lines(Cte, new FormatStyle { CollapseStatementsShorterThan = 0, CteParenOnNewLine = false });

            Assert.Equal("WITH c AS (", compact[0]);
            Assert.DoesNotContain(compact, l => l == "(");
        }

        [Fact]
        public void ACteCanHaveAsOnALineOfItsOwnAndUnindentedContents()
        {
            var lines = Lines(Cte, new FormatStyle { CollapseStatementsShorterThan = 0, CteAsOnNewLine = true, CteIndentContents = false });

            Assert.Equal("WITH c", lines[0]);
            Assert.Equal("AS", lines[1]);
            Assert.Equal("(", lines[2]);
            Assert.StartsWith("SELECT", lines[3]);
        }

        private const string Table = "create table dbo.Bigger (Id int not null,Name varchar(200) null,City varchar(100) null);";

        [Fact]
        public void CreateTableDefaultsKeepTheParenthesisOnItsOwnLineAndAlignTheTypes()
        {
            var lines = Lines(Table, new FormatStyle());

            Assert.Equal("(", lines[1]);
            Assert.Equal("      Id   INT NOT NULL", lines[2]);
        }

        [Fact]
        public void CreateTableCanHaveACompactParenthesisUnalignedTypesAndNoIndent()
        {
            var lines = Lines(Table, new FormatStyle { DdlParenOnNewLine = false, DdlIndentContents = false, AlignDdlColumns = false });

            Assert.Equal("CREATE TABLE dbo.Bigger (", lines[0]);
            Assert.Equal("  Id INT NOT NULL", lines[1]);
        }

        [Fact]
        public void ProcedureParametersGoOnLinesOfTheirOwn()
        {
            var lines = Lines("create procedure dbo.p @a int,@b int=1 out as select 1;", new FormatStyle { ProcedureParametersOnNewLines = true });

            Assert.Equal(new[] { "CREATE PROCEDURE dbo.p", "      @a INT", "    , @b INT = 1 OUT", "AS", "SELECT 1;" }, lines);
        }

        [Fact]
        public void AProcedureWithoutParametersIsNotChanged()
        {
            Assert.Equal(new[] { "CREATE PROCEDURE dbo.p AS", "SELECT 1;" }, Lines("create procedure dbo.p as select 1;", new FormatStyle { ProcedureParametersOnNewLines = true }));
        }

        [Fact]
        public void TheImportMapsTheStyles()
        {
            var style = SqlPromptStyleImporter.Import(@"{ ""parentheses"": { ""parenthesisStyle"": ""expandedToStatement"", ""indentParenthesesContents"": false },
                ""cte"": { ""parenthesisStyle"": ""compactSimple"", ""indentContents"": false, ""placeAsOnNewLine"": true },
                ""ddl"": { ""parenthesisStyle"": ""compactSimple"", ""indentParenthesesContents"": false, ""alignDataTypesAndConstraints"": false, ""placeFirstProcedureParameterOnNewLine"": ""always"" } }").Style;

            Assert.True(style.ParenOnNewLine);
            Assert.False(style.IndentParenContents);
            Assert.False(style.CteParenOnNewLine);
            Assert.False(style.CteIndentContents);
            Assert.True(style.CteAsOnNewLine);
            Assert.False(style.DdlParenOnNewLine);
            Assert.False(style.DdlIndentContents);
            Assert.False(style.AlignDdlColumns);
            Assert.True(style.ProcedureParametersOnNewLines);
            Assert.False(SqlPromptStyleImporter.Import(@"{ ""ddl"": { ""placeFirstProcedureParameterOnNewLine"": ""never"" } }").Style.ProcedureParametersOnNewLines);
        }
    }
}
