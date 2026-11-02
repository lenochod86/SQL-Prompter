using System.Linq;
using SqlPrompter.Core.Analysis;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class ContextAnalyzerTests
    {
        [Theory]
        [InlineData("SELECT * FROM |")]
        [InlineData("SELECT * FROM Us|")]
        [InlineData("SELECT * FROM dbo.Users u JOIN |")]
        [InlineData("SELECT * FROM dbo.Users u LEFT OUTER JOIN Or|")]
        [InlineData("SELECT * FROM dbo.Users u, |")]
        [InlineData("UPDATE |")]
        [InlineData("INSERT INTO |")]
        [InlineData("DELETE FROM |")]
        [InlineData("MERGE INTO |")]
        [InlineData("TRUNCATE TABLE |")]
        [InlineData("SELECT * FROM a CROSS APPLY |")]
        [InlineData("SELECT 1\nSELECT * FROM |")]
        public void ObjectNamePositions(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.ObjectName, context.Kind);
            Assert.Empty(context.Qualifier);
        }

        [Fact]
        public void ObjectNameWithSchema()
        {
            var context = Analyze("SELECT * FROM sales.Cu|");

            Assert.Equal(ContextKind.ObjectName, context.Kind);
            Assert.Equal(new[] { "sales" }, context.Qualifier);
            Assert.Equal("Cu", context.TypedPrefix);
        }

        [Fact]
        public void ThreePartNameIsLeftToBuiltIn()
        {
            Assert.Equal(ContextKind.None, Analyze("SELECT * FROM other.dbo.|").Kind);
        }

        [Fact]
        public void DeleteAndInsertOfferTheirKeyword()
        {
            Assert.Contains("FROM", Analyze("DELETE |").PreferredKeywords);
            Assert.Contains("INTO", Analyze("INSERT |").PreferredKeywords);
        }

        [Fact]
        public void ColumnOfAliasBeforeFrom()
        {
            var context = Analyze("SELECT u.| FROM dbo.Users u");

            Assert.Equal(ContextKind.Expression, context.Kind);
            Assert.NotNull(context.QualifiedSource);
            Assert.Equal(new[] { "dbo", "Users" }, context.QualifiedSource.NameParts);
            Assert.Equal("u", context.QualifiedSource.Alias);
        }

        [Fact]
        public void ColumnOfAliasInJoinCondition()
        {
            var context = Analyze("SELECT * FROM dbo.Users AS u INNER JOIN dbo.Orders o ON o.UserId = u.|");

            Assert.Equal("Users", context.QualifiedSource.ObjectName);
        }

        [Fact]
        public void ColumnOfTableWithoutAlias()
        {
            var context = Analyze("SELECT * FROM dbo.Users WHERE Users.N|");

            Assert.Equal("Users", context.QualifiedSource.ObjectName);
            Assert.Equal("N", context.TypedPrefix);
        }

        [Fact]
        public void ColumnOfSchemaQualifiedTable()
        {
            var context = Analyze("SELECT sales.Customers.| FROM sales.Customers");

            Assert.Equal("Customers", context.QualifiedSource.ObjectName);
        }

        [Fact]
        public void UnknownQualifierIsLeftToBuiltIn()
        {
            Assert.Equal(ContextKind.None, Analyze("SELECT x.| FROM dbo.Users u").Kind);
        }

        [Fact]
        public void UnqualifiedExpressionSeesAllSourcesOfTheBlock()
        {
            var context = Analyze("SELECT | FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id");

            Assert.Equal(ContextKind.Expression, context.Kind);
            Assert.Equal(new[] { "u", "o" }, context.Sources.Select(s => s.Alias).ToArray());
            Assert.Null(context.QualifiedSource);
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Users u WHERE |")]
        [InlineData("SELECT * FROM dbo.Users u WHERE u.Id = 1 AND |")]
        [InlineData("SELECT * FROM dbo.Users u ORDER BY |")]
        [InlineData("SELECT * FROM dbo.Users u GROUP BY |")]
        [InlineData("SELECT Name, COUNT(*) FROM dbo.Users u GROUP BY Name HAVING |")]
        [InlineData("SELECT a, | FROM dbo.Users u")]
        [InlineData("SELECT * FROM dbo.Users u WHERE u.Id IN (1, |)")]
        [InlineData("SELECT COUNT(|) FROM dbo.Users u")]
        [InlineData("UPDATE dbo.Users SET |")]
        public void ExpressionPositions(string sql)
        {
            Assert.Equal(ContextKind.Expression, Analyze(sql).Kind);
        }

        [Fact]
        public void UpdateTargetIsSource()
        {
            var context = Analyze("UPDATE dbo.Users SET Name = 'x' WHERE |");

            Assert.Equal("Users", Assert.Single(context.Sources).ObjectName);
        }

        [Fact]
        public void UpdateAliasIsNotExtraSource()
        {
            var context = Analyze("UPDATE u SET u.| FROM dbo.Users u");

            Assert.Equal("Users", Assert.Single(context.Sources).ObjectName);
            Assert.Equal("Users", context.QualifiedSource.ObjectName);
        }

        [Fact]
        public void SubqueryInWhereSeesOuterSources()
        {
            var context = Analyze("SELECT * FROM dbo.Users u WHERE EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.UserId = |)");

            Assert.Equal(new[] { ("o", 0), ("u", 1) }, context.Sources.Select(s => (s.Alias, s.ScopeLevel)).ToArray());
        }

        [Fact]
        public void CorrelatedQualifierResolvesToOuterSource()
        {
            var context = Analyze("SELECT * FROM dbo.Users u WHERE EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.UserId = u.|)");

            Assert.Equal("Users", context.QualifiedSource.ObjectName);
        }

        [Fact]
        public void DerivedTableDoesNotSeeOuterSources()
        {
            var context = Analyze("SELECT * FROM dbo.Users u JOIN (SELECT | FROM dbo.Orders) o ON o.UserId = u.Id");

            Assert.Equal("Orders", Assert.Single(context.Sources).ObjectName);
        }

        [Fact]
        public void OuterQuerySeesDerivedTableAlias()
        {
            var context = Analyze("SELECT | FROM (SELECT Id FROM dbo.Orders) AS d JOIN dbo.Users u ON u.Id = d.Id");

            Assert.Equal(new[] { TableSourceKind.Derived, TableSourceKind.Object }, context.Sources.Select(s => s.Kind).ToArray());
            Assert.Equal("d", context.Sources[0].Alias);
        }

        [Fact]
        public void UnionBlocksHaveOwnSources()
        {
            var context = Analyze("SELECT Id FROM dbo.Users UNION ALL SELECT | FROM dbo.Orders");

            Assert.Equal("Orders", Assert.Single(context.Sources).ObjectName);
        }

        [Fact]
        public void CteBodyDoesNotSeeMainQuery()
        {
            var context = Analyze("WITH c AS (SELECT | FROM dbo.Orders) SELECT * FROM c JOIN dbo.Users u ON 1 = 1");

            Assert.Equal("Orders", Assert.Single(context.Sources).ObjectName);
        }

        [Fact]
        public void TempTablesAndVariablesAreRecognized()
        {
            var context = Analyze("SELECT | FROM #tmp t JOIN @list l ON 1 = 1");

            Assert.Equal(new[] { TableSourceKind.TempTable, TableSourceKind.Variable }, context.Sources.Select(s => s.Kind).ToArray());
        }

        [Fact]
        public void StatementsWithoutSemicolonAreSeparated()
        {
            var context = Analyze("SELECT * FROM dbo.Orders\nSELECT | FROM dbo.Users");

            Assert.Equal("Users", Assert.Single(context.Sources).ObjectName);
        }

        [Fact]
        public void InsertSelectIsOneStatement()
        {
            var context = Analyze("INSERT INTO dbo.Orders (Id) SELECT | FROM dbo.Users");

            Assert.Equal("Users", Assert.Single(context.Sources).ObjectName);
        }

        [Fact]
        public void CaseEndDoesNotSplitStatement()
        {
            var context = Analyze("SELECT CASE WHEN 1 = 1 THEN 1 ELSE 0 END, | FROM dbo.Users");

            Assert.Equal("Users", Assert.Single(context.Sources).ObjectName);
        }

        [Fact]
        public void ProcedureBodyIsSeparateStatement()
        {
            var context = Analyze("CREATE PROCEDURE dbo.p @id int AS SELECT | FROM dbo.Users WHERE Id = @id");

            Assert.Equal(ContextKind.Expression, context.Kind);
            Assert.Equal("Users", Assert.Single(context.Sources).ObjectName);
        }

        [Fact]
        public void TableHintIsNotAlias()
        {
            var context = Analyze("SELECT | FROM dbo.Users WITH (NOLOCK) JOIN dbo.Orders o WITH (NOLOCK) ON 1 = 1");

            Assert.Equal(new string[] { null, "o" }, context.Sources.Select(s => s.Alias).ToArray());
        }

        [Fact]
        public void QuotedNamesAndAliases()
        {
            var context = Analyze("SELECT d.| FROM [dbo].[Order Details] AS [d]");

            Assert.Equal(new[] { "dbo", "Order Details" }, context.QualifiedSource.NameParts);
        }

        [Fact]
        public void StatementStartOffersStatementKeywords()
        {
            var context = Analyze("SEL|");

            Assert.Equal(ContextKind.Keyword, context.Kind);
            Assert.Contains("SELECT", context.PreferredKeywords);
        }

        [Fact]
        public void AfterSemicolonIsStatementStart()
        {
            Assert.Contains("SELECT", Analyze("SELECT 1; |").PreferredKeywords);
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Users |")]
        [InlineData("SELECT * FROM dbo.Users u |")]
        [InlineData("SELECT * FROM dbo.Users u W|")]
        [InlineData("SELECT * FROM (SELECT 1 AS x) d |")]
        public void AfterTableSourceOffersKeywordsWithSoftSelection(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.Keyword, context.Kind);
            Assert.Contains("WHERE", context.PreferredKeywords);
            Assert.True(context.SoftSelection);
        }

        [Fact]
        public void AfterJoinModifierOffersJoin()
        {
            var context = Analyze("SELECT * FROM dbo.Users u LEFT |");

            Assert.Equal(ContextKind.Keyword, context.Kind);
            Assert.Contains("JOIN", context.PreferredKeywords);
        }

        [Fact]
        public void AfterOperandSelectionIsSoft()
        {
            Assert.True(Analyze("SELECT Name | FROM dbo.Users").SoftSelection);
            Assert.False(Analyze("SELECT Name, | FROM dbo.Users").SoftSelection);
        }

        [Theory]
        [InlineData("SELECT '|' FROM dbo.Users")]
        [InlineData("SELECT 'abc|")]
        [InlineData("-- SELECT * FROM |")]
        [InlineData("/* SELECT * FROM | */")]
        [InlineData("SELECT * FROM dbo.Users AS |")]
        [InlineData("SELECT Name AS | FROM dbo.Users")]
        [InlineData("SELECT CAST(Id AS |) FROM dbo.Users")]
        [InlineData("SELECT @|")]
        [InlineData("SELECT @va|")]
        [InlineData("SELECT * FROM #t|")]
        [InlineData("SELECT 12|")]
        [InlineData("INSERT INTO dbo.Users (|")]
        [InlineData("INSERT INTO dbo.Users VALUES (|")]
        [InlineData("DECLARE @x |")]
        [InlineData("EXEC |")]
        [InlineData("SET @x = |")]
        public void LeftToBuiltIn(string sql)
        {
            Assert.Equal(ContextKind.None, Analyze(sql).Kind);
        }

        [Fact]
        public void ReplaceSpanCoversWholeWord()
        {
            var context = Analyze("SELECT * FROM Us|ers");

            Assert.Equal(14, context.ReplaceStart);
            Assert.Equal(5, context.ReplaceLength);
            Assert.Equal("Us", context.TypedPrefix);
        }

        [Fact]
        public void BracketStartForcesQuotedInsert()
        {
            var context = Analyze("SELECT * FROM [Us|");

            Assert.True(context.QuoteIdentifiers);
            Assert.Equal("Us", context.TypedPrefix);
        }

        [Fact]
        public void CaretRightAfterOperatorIsExpression()
        {
            Assert.Equal(ContextKind.Expression, Analyze("SELECT * FROM dbo.Users WHERE Id =|").Kind);
        }

        [Fact]
        public void MergeSourcesAndSetClause()
        {
            var context = Analyze("MERGE dbo.Users AS t USING dbo.Orders AS s ON t.Id = s.UserId WHEN MATCHED THEN UPDATE SET t.Name = s.|;");

            Assert.Equal(ContextKind.Expression, context.Kind);
            Assert.Equal("Orders", context.QualifiedSource.ObjectName);
            Assert.Equal(new[] { "t", "s" }, context.Sources.Select(s => s.Alias).ToArray());
        }
    }
}
