using System.Linq;
using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The comparison operators of conditions on separate lines in one column (#305).</summary>
    public class AlignComparisonsTests
    {
        private static string[] Lines(string sql, bool align, bool onOnNewLine = false)
        {
            var style = new FormatStyle { UseTabs = false, AlignComparisons = align, OnOnNewLine = onOnNewLine, CollapseStatementsShorterThan = 0 };
            return SqlFormatter.Format(sql, style).Text.Replace("\r\n", "\n").Split('\n');
        }

        private const string Where = "select a.Id from dbo.Accounts a where a.Active=1 and a.CityName<>'Prague' and a.Id>=5;";

        private static bool IsCondition(string l) => !l.StartsWith("SELECT") && !l.StartsWith("FROM");

        private static int OperatorColumn(string line)
        {
            var i = line.IndexOfAny(new[] { '=', '<', '>' });
            return i;
        }

        [Fact]
        public void TheOperatorsOfWhereStartInOneColumn()
        {
            var lines = Lines(Where, true).Where(l => IsCondition(l)).ToArray();

            Assert.Equal(3, lines.Length);
            Assert.Single(lines.Select(OperatorColumn).Distinct());
        }

        [Fact]
        public void WithoutTheOptionTheyFollowTheirLeftSides()
        {
            var lines = Lines(Where, false).Where(l => IsCondition(l)).ToArray();

            Assert.True(lines.Select(OperatorColumn).Distinct().Count() > 1);
        }

        [Fact]
        public void ConditionsWithoutAComparisonAreLeftAloneAndOneComparisonIsNotPadded()
        {
            var text = string.Join("\n", Lines("select 1 from dbo.A a where a.Id is null and a.Name like 'x%' and a.X=1;", true));

            Assert.Contains("a.Id IS NULL", text);
            Assert.Contains("a.X = 1", text);
        }

        [Fact]
        public void ComparisonsInsideParenthesesOrCaseDoNotCount()
        {
            var lines = Lines("select 1 from dbo.A a where a.Id=1 and (a.LongerName=2 or a.B=3) and case when a.C=1 then 1 else 0 end=1;", true);

            Assert.Contains(lines, l => l.Contains("a.Id = 1"));
            Assert.Contains(lines, l => l.Contains("(a.LongerName = 2 OR a.B = 3)"));
        }

        [Fact]
        public void TheConditionsOfOnAreAlignedToo()
        {
            var lines = Lines("select 1 from dbo.A a inner join dbo.B b on b.Id=a.IdB and b.LongerColumn>a.Other;", true, onOnNewLine: true)
                .Where(l => l.Contains("b.Id") || l.Contains("b.LongerColumn")).ToArray();

            Assert.Equal(2, lines.Length);
            Assert.Single(lines.Select(OperatorColumn).Distinct());
        }

        [Fact]
        public void TheImportSetsTheOption()
        {
            Assert.True(SqlPromptStyleImporter.Import(@"{ ""operators"": { ""comparison"": { ""align"": true } } }").Style.AlignComparisons);
            Assert.False(SqlPromptStyleImporter.Import(@"{ ""operators"": { ""comparison"": { ""align"": false } } }").Style.AlignComparisons);
        }
    }
}
