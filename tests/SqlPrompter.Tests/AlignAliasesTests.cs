using System.Linq;
using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The AS of the aliases of a SELECT list in one column (#305).</summary>
    public class AlignAliasesTests
    {
        private static string[] Lines(string sql, bool align, bool commaBefore = true)
        {
            var style = new FormatStyle { UseTabs = false, AlignAliases = align, CommaBeforeItems = commaBefore, CollapseStatementsShorterThan = 0 };
            return SqlFormatter.Format(sql, style).Text.Replace("\r\n", "\n").Split('\n');
        }

        private const string Sql = "select a.City as CityName,count(*) as Total,max(a.Created) as LastCreated,a.Id from dbo.Accounts a;";

        private static int AsColumn(string line) => line.IndexOf(" AS ", System.StringComparison.Ordinal);

        [Fact]
        public void TheAliasesStartInOneColumn()
        {
            var lines = Lines(Sql, true).Where(l => l.Contains(" AS ")).ToArray();

            Assert.Equal(3, lines.Length);
            Assert.Single(lines.Select(AsColumn).Distinct());
        }

        [Fact]
        public void WithoutTheOptionTheyFollowTheirExpressions()
        {
            var lines = Lines(Sql, false).Where(l => l.Contains(" AS ")).ToArray();

            Assert.True(lines.Select(AsColumn).Distinct().Count() > 1);
        }

        [Fact]
        public void ItemsWithoutAnAliasAreLeftAlone()
        {
            var lines = Lines(Sql, true);

            Assert.Contains(lines, l => l.TrimEnd().EndsWith(", a.Id", System.StringComparison.Ordinal));
        }

        [Fact]
        public void TheShortestExpressionIsPaddedToTheLongestPlusOneSpace()
        {
            var lines = Lines("select a.Id as X,a.LongerName as Y from dbo.T a;", true).Where(l => l.Contains(" AS ")).ToArray();

            Assert.Contains(lines, l => l.EndsWith("a.Id         AS X", System.StringComparison.Ordinal) || l.Contains("a.Id         AS X"));
        }

        [Fact]
        public void CommasAtTheEndOfTheLineWorkToo()
        {
            var lines = Lines(Sql, true, commaBefore: false).Where(l => l.Contains(" AS ")).ToArray();

            Assert.Single(lines.Select(AsColumn).Distinct());
            Assert.EndsWith(",", lines[0].TrimEnd());
        }

        [Fact]
        public void AnItemWithoutAnExplicitAsOrASubqueryIsNotTouchedAndNothingBreaks()
        {
            var text = string.Join("\n", Lines("select a.Id x,(select max(b.Id) from dbo.B b) as M,a.Name as N from dbo.A a;", true));

            Assert.Contains("a.Id x", text);
            Assert.Contains("AS M", text);
            Assert.Contains("AS N", text);
        }

        [Fact]
        public void TheImportSetsTheOption()
        {
            Assert.True(SqlPromptStyleImporter.Import(@"{ ""lists"": { ""alignAliases"": true } }").Style.AlignAliases);
            Assert.False(SqlPromptStyleImporter.Import(@"{ ""lists"": { ""alignAliases"": false } }").Style.AlignAliases);
        }
    }
}
