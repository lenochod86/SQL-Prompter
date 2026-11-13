using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>A short SELECT with an IN list is formatted by every style, and the result says when the style kept it on one line (#441).</summary>
    public class ShortSelectFormattingTests
    {
        private const string Query = "SELECT * FROM Cil.CilEntita AS ce WHERE ce.IdCil IN (1159622,1163744,1165077,1166632,1168964,1168983,1186932,1195297,1206314,1209103)";

        private const string SpacedList = "IN (1159622, 1163744, 1165077, 1166632, 1168964, 1168983, 1186932, 1195297, 1206314, 1209103)";

        [Fact]
        public void TheDefaultStyleSplitsTheClausesAndSpacesTheList()
        {
            var result = SqlFormatter.Format(Query, new FormatStyle(), "\n");

            Assert.Equal("SELECT\t*\nFROM\tCil.CilEntita AS ce\nWHERE\tce.IdCil " + SpacedList, result.Text);
            Assert.Equal(1, result.Formatted);
            Assert.Equal(0, result.Collapsed);
            Assert.NotEqual(Query, result.Text);
        }

        [Fact]
        public void AStyleThatDoesNotCollapseSplitsTheClausesToo()
        {
            var result = SqlFormatter.Format(Query, new FormatStyle { CollapseStatementsShorterThan = 0 }, "\n");

            Assert.Equal("SELECT\t*\nFROM\tCil.CilEntita AS ce\nWHERE\tce.IdCil " + SpacedList, result.Text);
            Assert.Equal(0, result.Collapsed);
        }

        [Fact]
        public void AShortStatementStaysOnOneLineButTheSpacingAndTheCaseStillApply()
        {
            var result = SqlFormatter.Format(Query.ToLowerInvariant().Replace("ce.idcil", "ce.IdCil"), new FormatStyle { CollapseStatementsShorterThan = 200 }, "\n");

            Assert.Equal("SELECT * FROM cil.cilentita AS ce WHERE ce.IdCil " + SpacedList.ToLowerInvariant().Replace("in (", "IN ("), result.Text);
            Assert.Equal(1, result.Formatted);
            Assert.Equal(1, result.Collapsed);
        }

        [Fact]
        public void ACollapsedStatementWithoutSpacesAfterTheCommasIsNotLeftUnchanged()
        {
            var result = SqlFormatter.Format(Query, new FormatStyle { CollapseStatementsShorterThan = 1000 }, "\n");

            Assert.NotEqual(Query, result.Text);
            Assert.Equal("SELECT * FROM Cil.CilEntita AS ce WHERE ce.IdCil " + SpacedList, result.Text);
            Assert.Equal(1, result.Collapsed);
        }

        [Fact]
        public void TheStyleOfTheUserWithAlignedAliasesFormatsTheQuery()
        {
            var result = SqlFormatter.Format(Query, new FormatStyle { AlignAliases = true, NewLineAfterDistinctAndTop = true }, "\n");

            Assert.Equal("SELECT\t*\nFROM\tCil.CilEntita AS ce\nWHERE\tce.IdCil " + SpacedList, result.Text);
        }

        [Fact]
        public void ALongerListAndAMissingSpaceAfterTheCommaGetTheSpacing()
        {
            var list = string.Join(",", System.Linq.Enumerable.Range(1000, 30));
            var result = SqlFormatter.Format("SELECT a,b FROM t AS x WHERE x.a IN (" + list + ")", new FormatStyle(), "\n");

            Assert.Contains("IN (" + string.Join(", ", System.Linq.Enumerable.Range(1000, 30)) + ")", result.Text);
            Assert.Contains("SELECT\ta\n", result.Text);
            Assert.Contains(", b", result.Text);
        }

        [Fact]
        public void TheCountOfCollapsedStatementsIsPerStatement()
        {
            var result = SqlFormatter.Format("SELECT 1; SELECT a,b FROM t WHERE x IN (1,2)", new FormatStyle(), "\n");

            Assert.Equal(2, result.Formatted);
            Assert.Equal(2, result.Collapsed);
        }
    }
}
