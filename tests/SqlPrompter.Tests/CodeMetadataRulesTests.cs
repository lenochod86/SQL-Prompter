using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Metadata;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The code analysis rules that need the metadata: an ambiguous column and an implicit conversion (#208).</summary>
    public class CodeMetadataRulesTests
    {
        private static DatabaseSnapshot Db(bool columnsLoaded = true) =>
            new DatabaseSnapshot("t", "dbo", new[] { "dbo" },
                new[] { new DbObject(1, "dbo", "Orders", DbObjectKind.Table, false), new DbObject(2, "dbo", "Users", DbObjectKind.Table, false) },
                "v1")
                .WithColumns(columnsLoaded
                    ? new Dictionary<int, IReadOnlyList<DbColumn>>
                    {
                        [1] = new[] { new DbColumn("IdOrder", "int", false), new DbColumn("IdUser", "int", false), new DbColumn("Name", "nvarchar(50)", true), new DbColumn("Code", "varchar(10)", true) },
                        [2] = new[] { new DbColumn("IdUser", "int", false), new DbColumn("Name", "nvarchar(50)", true), new DbColumn("Login", "varchar(20)", false) },
                    }
                    : new Dictionary<int, IReadOnlyList<DbColumn>>());

        private static List<CodeIssue> Find(string sql, DatabaseSnapshot db, CodeIssueKind kind) =>
            CodeAnalyzer.Analyze(sql, new AnalysisSettings(), db).Where(i => i.Kind == kind).ToList();

        private static string Underlined(string sql, CodeIssue issue) => sql.Substring(issue.Start, issue.Length);

        [Fact]
        public void AnUnqualifiedColumnOfTwoTablesIsAmbiguous()
        {
            const string sql = "SELECT IdOrder, Name FROM dbo.Orders o JOIN dbo.Users u ON u.IdUser = o.IdUser WHERE Name = 'x'";

            var issues = Find(sql, Db(), CodeIssueKind.AmbiguousColumn);

            Assert.Equal(new[] { "Name", "Name" }, issues.Select(i => Underlined(sql, i)).ToArray());
            Assert.Contains("o.Name or u.Name", issues[0].Message);
        }

        [Fact]
        public void QualifiedColumnsAliasesAndUniqueColumnsAreFine()
        {
            Assert.Empty(Find("SELECT o.Name, u.Name, IdOrder, Login FROM dbo.Orders o JOIN dbo.Users u ON u.IdUser = o.IdUser", Db(), CodeIssueKind.AmbiguousColumn));
            Assert.Empty(Find("SELECT o.Name AS Name FROM dbo.Orders o JOIN dbo.Users u ON u.IdUser = o.IdUser ORDER BY Name", Db(), CodeIssueKind.AmbiguousColumn));
            Assert.Empty(Find("SELECT Name FROM dbo.Orders", Db(), CodeIssueKind.AmbiguousColumn));
        }

        [Fact]
        public void NothingIsReportedWhileTheMetadataIsMissingOrTheSourcesAreNotPlain()
        {
            const string sql = "SELECT Name FROM dbo.Orders o JOIN dbo.Users u ON u.IdUser = o.IdUser";
            Assert.DoesNotContain(CodeAnalyzer.Analyze(sql, new AnalysisSettings(), null), i => i.Kind == CodeIssueKind.AmbiguousColumn);
            Assert.Empty(Find(sql, Db(false), CodeIssueKind.AmbiguousColumn));
            Assert.Empty(Find("SELECT Name FROM dbo.Orders o JOIN (SELECT 1 AS x) d ON 1 = 1", Db(), CodeIssueKind.AmbiguousColumn));
        }

        [Fact]
        public void TheRuleCanBeSwitchedOff()
        {
            var settings = new AnalysisSettings { AmbiguousColumn = false };

            Assert.DoesNotContain(CodeAnalyzer.Analyze("SELECT Name FROM dbo.Orders o JOIN dbo.Users u ON u.IdUser = o.IdUser", settings, Db()), i => i.Kind == CodeIssueKind.AmbiguousColumn);
        }

        [Fact]
        public void AnAnsiColumnWithAnNLiteralIsConverted()
        {
            const string sql = "SELECT * FROM dbo.Users WHERE Login = N'abc'";

            var issue = Find(sql, Db(), CodeIssueKind.ImplicitConversion).Single();

            Assert.Equal("N'abc'", Underlined(sql, issue));
            Assert.Equal(string.Empty, issue.Fix.Replacement);
            Assert.Equal(1, issue.Fix.Length);
        }

        [Fact]
        public void NoRepairWhenTheLiteralHasNonAsciiCharacters()
        {
            var issue = Find("SELECT * FROM dbo.Users WHERE Login = N'čau'", Db(), CodeIssueKind.ImplicitConversion).Single();

            Assert.Null(issue.Fix);
        }

        [Fact]
        public void ANumberOrAStringAgainstTheWrongFamilyIsReported()
        {
            Assert.Single(Find("SELECT * FROM dbo.Users WHERE Login = 5", Db(), CodeIssueKind.ImplicitConversion));
            Assert.Single(Find("SELECT * FROM dbo.Users WHERE Name = 5", Db(), CodeIssueKind.ImplicitConversion));
            var number = Find("SELECT * FROM dbo.Orders WHERE IdOrder = '12'", Db(), CodeIssueKind.ImplicitConversion).Single();
            Assert.Equal("12", number.Fix.Replacement);
        }

        [Fact]
        public void MatchingTypesAndOtherShapesAreFine()
        {
            Assert.Empty(Find("SELECT * FROM dbo.Users WHERE Login = 'abc'", Db(), CodeIssueKind.ImplicitConversion));
            Assert.Empty(Find("SELECT * FROM dbo.Users WHERE Name = N'abc'", Db(), CodeIssueKind.ImplicitConversion));
            Assert.Empty(Find("SELECT * FROM dbo.Orders WHERE IdOrder = 5", Db(), CodeIssueKind.ImplicitConversion));
            Assert.Empty(Find("SELECT * FROM dbo.Orders WHERE IdOrder = 5 + 1", Db(), CodeIssueKind.ImplicitConversion));
            // The same name in two tables: not known which one is meant (qualified, it is).
            Assert.DoesNotContain(Find("SELECT * FROM dbo.Orders o JOIN dbo.Users u ON u.IdUser = o.IdUser WHERE Code = N'x' AND IdUser = 1", Db(), CodeIssueKind.ImplicitConversion), i => i.Message.Contains("IdUser"));
            Assert.Single(Find("SELECT * FROM dbo.Orders o JOIN dbo.Users u ON u.IdUser = o.IdUser WHERE u.Login = N'x'", Db(), CodeIssueKind.ImplicitConversion));
        }

        [Fact]
        public void AnIssueIsReportedOnceEvenWithSeveralStatementsWithoutSemicolons()
        {
            const string sql = "SELECT 1\nSELECT * FROM dbo.Users WHERE Login = N'x'\nSELECT * FROM dbo.Orders WHERE IdOrder = '3'";

            var issues = CodeAnalyzer.Analyze(sql, new AnalysisSettings(), Db()).Where(i => i.Kind == CodeIssueKind.ImplicitConversion).ToList();

            Assert.Equal(2, issues.Count);
        }
    }
}
