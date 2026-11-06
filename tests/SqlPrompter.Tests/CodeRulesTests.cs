using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Editing;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The code analysis rules and quick fixes added in #208.</summary>
    public class CodeRulesTests
    {
        private static CodeIssue[] Find(string sql, CodeIssueKind kind) =>
            CodeAnalyzer.Analyze(sql, new AnalysisSettings()).Where(i => i.Kind == kind).ToArray();

        [Theory]
        [InlineData("SELECT * FROM t WHERE YEAR(d) = 2025")]
        [InlineData("SELECT * FROM t WHERE UPPER(name) = 'X'")]
        [InlineData("SELECT * FROM t WHERE a = 1 AND LTRIM(RTRIM(name)) = 'X'")]
        [InlineData("SELECT * FROM t JOIN u ON LOWER(t.a) = LOWER(u.a)")]
        [InlineData("SELECT * FROM t WHERE DATEPART(month, d) = 5")]
        [InlineData("SELECT * FROM t WHERE ISNULL(c, 0) > 3")]
        [InlineData("SELECT * FROM t WHERE LEN(c) IN (1, 2)")]
        public void AFunctionOnAComparedColumnIsFlagged(string sql)
        {
            Assert.NotEmpty(Find(sql, CodeIssueKind.FunctionOnColumn));
            Assert.All(Find(sql, CodeIssueKind.FunctionOnColumn), i => Assert.Equal(CodeIssueSeverity.Hint, i.Severity));
        }

        [Theory]
        [InlineData("SELECT YEAR(d) FROM t")]
        [InlineData("SELECT * FROM t WHERE d = YEAR(GETDATE())")]
        [InlineData("SELECT * FROM t WHERE c = UPPER('x')")]
        [InlineData("SELECT * FROM t WHERE COALESCE(1, 2) = 1")]
        [InlineData("SELECT * FROM t WHERE DATEPART(month, GETDATE()) = 5")]
        [InlineData("SELECT * FROM t WHERE c = CAST(5 AS int)")]
        [InlineData("SELECT * FROM t WHERE ABS(c) = 1")]
        [InlineData("-- WHERE YEAR(d) = 1\nSELECT 1")]
        public void OtherUsesOfFunctionsAreNotFlagged(string sql)
        {
            Assert.Empty(Find(sql, CodeIssueKind.FunctionOnColumn));
        }

        [Fact]
        public void TheDateFunctionsSuggestARange()
        {
            Assert.Contains("range", Find("SELECT * FROM t WHERE YEAR(d) = 2025", CodeIssueKind.FunctionOnColumn)[0].Message);
            Assert.DoesNotContain("range", Find("SELECT * FROM t WHERE UPPER(c) = 'X'", CodeIssueKind.FunctionOnColumn)[0].Message);
        }

        [Theory]
        [InlineData("SELECT * FROM t WHERE name LIKE '%x'")]
        [InlineData("SELECT * FROM t WHERE name NOT LIKE '%x%'")]
        [InlineData("SELECT * FROM t WHERE name LIKE N'%x'")]
        public void ALeadingWildcardIsFlagged(string sql)
        {
            Assert.Single(Find(sql, CodeIssueKind.LeadingWildcard));
        }

        [Theory]
        [InlineData("SELECT * FROM t WHERE name LIKE 'x%'")]
        [InlineData("SELECT * FROM t WHERE name LIKE @pattern")]
        [InlineData("SELECT '%x' AS a")]
        public void OtherLikesAreNotFlagged(string sql)
        {
            Assert.Empty(Find(sql, CodeIssueKind.LeadingWildcard));
        }

        [Theory]
        [InlineData("SELECT TOP 10 * FROM t")]
        [InlineData("SELECT TOP (10) * FROM t WHERE a = 1")]
        [InlineData("SELECT TOP 5 a FROM t; SELECT 1")]
        [InlineData("INSERT INTO x SELECT TOP 5 a FROM t")]
        [InlineData("SELECT a, (SELECT TOP 1 b FROM u) FROM t")]
        public void TopWithoutOrderByIsFlagged(string sql)
        {
            Assert.NotEmpty(Find(sql, CodeIssueKind.TopWithoutOrderBy));
        }

        [Theory]
        [InlineData("SELECT TOP 10 * FROM t ORDER BY a")]
        [InlineData("SELECT TOP (10) * FROM t WHERE a = 1 ORDER BY a DESC")]
        [InlineData("SELECT * FROM t WHERE EXISTS (SELECT TOP 1 1 FROM u)")]
        [InlineData("SELECT TOP 5 a FROM t UNION SELECT a FROM u ORDER BY a")]
        [InlineData("SELECT a FROM t")]
        [InlineData("SELECT a, (SELECT TOP 1 b FROM u ORDER BY c) FROM t")]
        public void TopWithOrderByIsNotFlagged(string sql)
        {
            Assert.Empty(Find(sql, CodeIssueKind.TopWithoutOrderBy));
        }

        [Fact]
        public void TheIssueOfTopCoversTopAndItsNumber()
        {
            var sql = "SELECT TOP 10 * FROM t";
            var issue = Find(sql, CodeIssueKind.TopWithoutOrderBy).Single();

            Assert.Equal("TOP 10", sql.Substring(issue.Start, issue.Length));
        }

        [Fact]
        public void AtAtIdentityIsRepaired()
        {
            var sql = "INSERT INTO t (a) VALUES (1); SELECT @@IDENTITY";

            Assert.Equal(CodeIssueSeverity.Warning, Find(sql, CodeIssueKind.AtAtIdentity).Single().Severity);
            Assert.Equal("INSERT INTO t (a) VALUES (1); SELECT SCOPE_IDENTITY()", CodeFixes.Apply(sql, new AnalysisSettings()));
        }

        [Theory]
        [InlineData("DECLARE @a varchar", "DECLARE @a varchar(100)")]
        [InlineData("DECLARE @a nvarchar, @b int", "DECLARE @a nvarchar(100), @b int")]
        [InlineData("DECLARE @a AS varchar", "DECLARE @a AS varchar(100)")]
        [InlineData("DECLARE @a char", "DECLARE @a char(10)")]
        [InlineData("SELECT CAST(x AS varchar) FROM t", "SELECT CAST(x AS varchar(100)) FROM t")]
        [InlineData("SELECT TRY_CAST(x AS nvarchar) FROM t", "SELECT TRY_CAST(x AS nvarchar(100)) FROM t")]
        [InlineData("SELECT CONVERT(varchar, x) FROM t", "SELECT CONVERT(varchar(100), x) FROM t")]
        public void ATypeWithoutALengthIsRepaired(string sql, string expected)
        {
            Assert.Single(Find(sql, CodeIssueKind.TypeWithoutLength));
            Assert.Equal(expected, CodeFixes.Apply(sql, new AnalysisSettings()));
        }

        [Theory]
        [InlineData("DECLARE @a varchar(50)")]
        [InlineData("DECLARE @a nvarchar(MAX)")]
        [InlineData("SELECT CAST(x AS varchar(10)) FROM t")]
        [InlineData("SELECT CONVERT(varchar(10), x) FROM t")]
        [InlineData("SELECT a AS varchar FROM t")]
        [InlineData("DECLARE @a int")]
        public void LengthsThatAreThereOrOtherTypesAreLeftAlone(string sql)
        {
            Assert.Empty(Find(sql, CodeIssueKind.TypeWithoutLength));
        }

        [Fact]
        public void EqualsNullIsRepaired()
        {
            var sql = "SELECT * FROM t WHERE a = NULL AND b <> NULL AND c != NULL";

            Assert.Equal("SELECT * FROM t WHERE a IS NULL AND b IS NOT NULL AND c IS NOT NULL", CodeFixes.Apply(sql, new AnalysisSettings()));
        }

        [Fact]
        public void NullOnTheLeftHasNoRepairButIsFlagged()
        {
            var sql = "SELECT * FROM t WHERE NULL = a";

            Assert.Single(Find(sql, CodeIssueKind.EqualsNull));
            Assert.Equal(sql, CodeFixes.Apply(sql, new AnalysisSettings()));
        }

        [Fact]
        public void NotInWithASubqueryIsFlagged()
        {
            Assert.Single(Find("SELECT * FROM t WHERE a NOT IN (SELECT b FROM u)", CodeIssueKind.NotInSubquery));
            Assert.Empty(Find("SELECT * FROM t WHERE a NOT IN (1, 2)", CodeIssueKind.NotInSubquery));
            Assert.Empty(Find("SELECT * FROM t WHERE a IN (SELECT b FROM u)", CodeIssueKind.NotInSubquery));
        }

        [Theory]
        [InlineData("SELECT * FROM a, b WHERE a.x = b.x")]
        [InlineData("SELECT * FROM dbo.a x, dbo.b y WHERE x.i = y.i")]
        [InlineData("UPDATE a SET v = 1 FROM a, b WHERE a.x = b.x")]
        public void ACommaBetweenTablesIsFlagged(string sql)
        {
            Assert.Single(Find(sql, CodeIssueKind.OldStyleJoin));
        }

        [Theory]
        [InlineData("SELECT * FROM a JOIN b ON a.x = b.x")]
        [InlineData("SELECT * FROM STRING_SPLIT(@s, ',')")]
        [InlineData("SELECT * FROM (SELECT a, b FROM t) q")]
        [InlineData("SELECT a, b FROM t")]
        [InlineData("DELETE FROM a WHERE x IN (1, 2)")]
        [InlineData("INSERT INTO t (a, b) SELECT a, b FROM u")]
        public void CommasThatAreNoJoinAreNotFlagged(string sql)
        {
            Assert.Empty(Find(sql, CodeIssueKind.OldStyleJoin));
        }

        [Fact]
        public void EveryRuleHasItsSwitch()
        {
            var off = new AnalysisSettings
            {
                FunctionOnColumn = false, LeadingWildcard = false, TopWithoutOrderBy = false, AtAtIdentity = false, TypeWithoutLength = false, NotInSubquery = false, OldStyleJoin = false,
            };
            var sql = "SELECT TOP 5 @@IDENTITY, CAST(x AS varchar) FROM a, b WHERE YEAR(d) = 1 AND n LIKE '%x' AND k NOT IN (SELECT 1)";

            var kinds = CodeAnalyzer.Analyze(sql, off).Select(i => i.Kind).ToArray();

            Assert.DoesNotContain(CodeIssueKind.FunctionOnColumn, kinds);
            Assert.DoesNotContain(CodeIssueKind.LeadingWildcard, kinds);
            Assert.DoesNotContain(CodeIssueKind.TopWithoutOrderBy, kinds);
            Assert.DoesNotContain(CodeIssueKind.AtAtIdentity, kinds);
            Assert.DoesNotContain(CodeIssueKind.TypeWithoutLength, kinds);
            Assert.DoesNotContain(CodeIssueKind.NotInSubquery, kinds);
            Assert.DoesNotContain(CodeIssueKind.OldStyleJoin, kinds);
        }

        [Fact]
        public void TheRulesAreOnByDefaultAndKeptInTheSettingsFile()
        {
            var defaults = new AnalysisSettings();

            Assert.True(defaults.FunctionOnColumn && defaults.LeadingWildcard && defaults.TopWithoutOrderBy && defaults.AtAtIdentity && defaults.TypeWithoutLength && defaults.NotInSubquery && defaults.OldStyleJoin);
            Assert.False(SettingsJson.Deserialize("{ \"analysis\": { \"atAtIdentity\": false } }").Analysis.AtAtIdentity);
            Assert.True(SettingsJson.Deserialize("{}").Analysis.AtAtIdentity);
        }

        [Fact]
        public void NothingFiresInStringsAndComments()
        {
            var sql = "SELECT '@@IDENTITY WHERE YEAR(d) = 1', 1 /* DECLARE @a varchar */ -- FROM a, b";

            var kinds = CodeAnalyzer.Analyze(sql, new AnalysisSettings()).Select(i => i.Kind).ToArray();

            Assert.DoesNotContain(CodeIssueKind.AtAtIdentity, kinds);
            Assert.DoesNotContain(CodeIssueKind.FunctionOnColumn, kinds);
            Assert.DoesNotContain(CodeIssueKind.TypeWithoutLength, kinds);
            Assert.DoesNotContain(CodeIssueKind.OldStyleJoin, kinds);
        }

        [Fact]
        public void FixAllLeavesAScriptWithoutRepairsAlone()
        {
            var sql = "SELECT a\r\nFROM t -- note\r\nWHERE b = 1";

            Assert.Equal(sql, CodeFixes.Apply(sql, new AnalysisSettings()));
        }

        [Fact]
        public void FixAllRespectsTheSwitchesAndWorksWithTheSquigglesOff()
        {
            var sql = "SELECT @@IDENTITY WHERE a = NULL";

            Assert.Equal("SELECT SCOPE_IDENTITY() WHERE a IS NULL", CodeFixes.Apply(sql, new AnalysisSettings { Enabled = false }));
            Assert.Equal("SELECT @@IDENTITY WHERE a IS NULL", CodeFixes.Apply(sql, new AnalysisSettings { AtAtIdentity = false }));
        }
    }
}
