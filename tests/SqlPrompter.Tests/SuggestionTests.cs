using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class SuggestionTests
    {
        [Fact]
        public void TablesAreInsertedWithSchema()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), SampleDatabase());

            var users = items.Single(i => i.DisplayText == "Users");
            Assert.Equal("dbo.Users", users.InsertText);
            Assert.Equal("dbo", users.Suffix);
            Assert.Equal("dbo.[Order Details]", items.Single(i => i.DisplayText == "Order Details").InsertText);
            Assert.Contains(items, i => i.Kind == SuggestionKind.Schema && i.DisplayText == "sales");
            Assert.Contains(items, i => i.Kind == SuggestionKind.View && i.DisplayText == "ActiveUsers");
        }

        [Fact]
        public void SystemObjectsOnlyWithSchema()
        {
            var unqualified = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), SampleDatabase());
            var qualified = SuggestionBuilder.Build(Analyze("SELECT * FROM sys.|"), SampleDatabase());

            Assert.DoesNotContain(unqualified, i => i.DisplayText == "objects");
            Assert.Equal("objects", Assert.Single(qualified).InsertText);
        }

        [Fact]
        public void SchemaQualifierFiltersObjects()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM sales.|"), SampleDatabase());

            Assert.Equal(new[] { "Customers" }, items.Select(i => i.InsertText).ToArray());
        }

        [Fact]
        public void QualifiedColumnsComeFromOneSource()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT u.| FROM dbo.Users u"), SampleDatabase());

            Assert.Equal(new[] { "Id", "Name", "Email" }, items.Select(i => i.InsertText).ToArray());
        }

        [Fact]
        public void AmbiguousColumnsAreQualified()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT | FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id"), SampleDatabase());
            var columns = items.Where(i => i.Kind == SuggestionKind.Column).ToList();

            Assert.Contains(columns, i => i.InsertText == "u.Id");
            Assert.Contains(columns, i => i.InsertText == "o.Id");
            Assert.Contains(columns, i => i.InsertText == "Email");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Alias && i.InsertText == "u");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Function && i.InsertText == "COUNT()");
            Assert.True(columns.Max(c => c.Group) < items.Where(i => i.Kind == SuggestionKind.Keyword).Min(k => k.Group));
        }

        [Fact]
        public void OuterColumnsAreQualifiedAndRankedLower()
        {
            var items = SuggestionBuilder.Build(
                Analyze("SELECT * FROM dbo.Users u WHERE EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.UserId = |)"),
                SampleDatabase());

            var outer = items.Single(i => i.InsertText == "u.Email");
            var inner = items.Single(i => i.InsertText == "Total");
            Assert.True(outer.Group > inner.Group);
        }

        [Fact]
        public void IdentifiersNeedingQuotesAreQuoted()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT d.| FROM dbo.[Order Details] d"), SampleDatabase());

            Assert.Contains(items, i => i.InsertText == "[Order Id]");
        }

        [Fact]
        public void KeywordCaseFollowsTypedText()
        {
            var lower = SuggestionBuilder.Build(Analyze("sel|"), null);
            var upper = SuggestionBuilder.Build(Analyze("SEL|"), null);

            Assert.Contains(lower, i => i.InsertText == "select");
            Assert.Contains(upper, i => i.InsertText == "SELECT");
        }

        [Fact]
        public void PreferredKeywordsComeFirst()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Users u |"), null);

            var where = items.Single(i => i.InsertText == "WHERE");
            var and = items.Single(i => i.InsertText == "AND");
            Assert.True(where.Group < and.Group);
        }

        [Fact]
        public void NameContextIsEmpty()
        {
            Assert.Empty(SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Users AS |"), SampleDatabase()));
        }

        [Fact]
        public void DataTypeContextOffersTypes()
        {
            var items = SuggestionBuilder.Build(Analyze("DECLARE @x nv|"), null);

            Assert.All(items, i => Assert.Equal(SuggestionKind.DataType, i.Kind));
            Assert.Contains(items, i => i.InsertText == "nvarchar");
        }

        [Fact]
        public void FallbackOffersKeywordsFunctionsAndTypes()
        {
            var items = SuggestionBuilder.Build(Analyze("SET @x = |"), null);

            Assert.Contains(items, i => i.Kind == SuggestionKind.Keyword);
            Assert.Contains(items, i => i.Kind == SuggestionKind.Function);
            Assert.Contains(items, i => i.Kind == SuggestionKind.DataType);
        }

        [Fact]
        public void KnownColumnsAreOfferedNextToUnknownSource()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT | FROM dbo.Users u JOIN #tmp t ON t.Id = u.Id"), SampleDatabase());

            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.InsertText == "Email");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Alias && i.InsertText == "t");
        }

        [Fact]
        public void UnknownQualifiedSourceGivesEmptyList()
        {
            var items = SuggestionBuilder.Build(Analyze("WITH c AS (SELECT 1 AS x) SELECT c.| FROM c"), SampleDatabase());

            Assert.Empty(items);
        }

        [Fact]
        public void ReadinessRequestsMissingColumns()
        {
            var readiness = Readiness.Evaluate(Analyze("SELECT u.| FROM dbo.Users u"), SampleDatabase(withColumns: false));

            Assert.Equal(ReadinessState.NeedsColumns, readiness.State);
            Assert.Equal("Users", Assert.Single(readiness.MissingColumns).Name);
        }

        [Theory]
        [InlineData("SELECT | FROM #tmp")]
        [InlineData("SELECT | FROM dbo.Missing")]
        [InlineData("SELECT | FROM other.dbo.Users")]
        [InlineData("WITH c AS (SELECT 1 AS x) SELECT | FROM c")]
        [InlineData("SELECT d.| FROM (SELECT 1 AS x) d")]
        [InlineData("SELECT * FROM nope.|")]
        public void UnknownSourcesAreUnsupported(string sql)
        {
            Assert.Equal(ReadinessState.Unsupported, Readiness.Evaluate(Analyze(sql), SampleDatabase()).State);
        }

        [Fact]
        public void UnknownOuterSourceDoesNotBlock()
        {
            var context = Analyze("SELECT * FROM #tmp t WHERE EXISTS (SELECT 1 FROM dbo.Orders o WHERE |)");

            Assert.Equal(ReadinessState.Ready, Readiness.Evaluate(context, SampleDatabase()).State);
        }

        [Fact]
        public void MetadataIsNeededForObjectNames()
        {
            Assert.Equal(ReadinessState.NeedsMetadata, Readiness.Evaluate(Analyze("SELECT * FROM |"), null).State);
            Assert.Equal(ReadinessState.Ready, Readiness.Evaluate(Analyze("SEL|"), null).State);
        }

        private static System.Collections.Generic.IReadOnlyList<Suggestion> BuildWithScript(string sqlWithCaret, bool withColumns = true)
        {
            var caret = sqlWithCaret.IndexOf('|');
            var sql = sqlWithCaret.Remove(caret, 1);
            return SuggestionBuilder.Build(ContextAnalyzer.Analyze(sql, caret), SampleDatabase(withColumns), ScriptModel.Build(sql, caret));
        }

        [Fact]
        public void CteColumnsAreSuggested()
        {
            var items = BuildWithScript("WITH c AS (SELECT Id, Name AS Title FROM dbo.Users) SELECT | FROM c");

            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.InsertText == "Title");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.InsertText == "Id");
        }

        [Fact]
        public void CteColumnsAfterItsAlias()
        {
            var items = BuildWithScript("WITH c AS (SELECT Id, Name FROM dbo.Users) SELECT x.| FROM c x JOIN dbo.Orders o ON o.UserId = x.Id");

            Assert.Equal(new[] { "Id", "Name" }, items.Select(i => i.InsertText).ToArray());
        }

        [Fact]
        public void ColumnsOfCteAndTableInTheSameQueryAreQualifiedWhenAmbiguous()
        {
            var items = BuildWithScript("WITH c AS (SELECT Id FROM dbo.Users) SELECT | FROM c JOIN dbo.Orders o ON o.UserId = c.Id");

            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.InsertText == "c.Id");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.InsertText == "o.Id");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.InsertText == "Total");
        }

        [Fact]
        public void TempTableColumnsHaveTypes()
        {
            var items = BuildWithScript("CREATE TABLE #t (Id int NOT NULL, Name nvarchar(20))\nSELECT t.| FROM #t t");

            var id = items.Single(i => i.DisplayText == "Id");
            Assert.Contains("int not null", id.Description);
            Assert.Contains("nvarchar(20) null", items.Single(i => i.DisplayText == "Name").Description);
        }

        [Fact]
        public void DerivedTableColumns()
        {
            var items = BuildWithScript("SELECT | FROM (SELECT Id AS UserId, Name FROM dbo.Users) d");

            Assert.Contains(items, i => i.Kind == SuggestionKind.Column && i.InsertText == "UserId");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Alias && i.InsertText == "d");
        }

        [Fact]
        public void ScriptTablesAreOfferedAfterFrom()
        {
            var items = BuildWithScript("DECLARE @t TABLE (a int)\nCREATE TABLE #tmp (b int);\nWITH c AS (SELECT 1 AS x) SELECT * FROM |");

            Assert.Contains(items, i => i.Kind == SuggestionKind.ScriptTable && i.InsertText == "c");
            Assert.Contains(items, i => i.Kind == SuggestionKind.ScriptTable && i.InsertText == "#tmp");
            Assert.Contains(items, i => i.Kind == SuggestionKind.ScriptTable && i.InsertText == "@t");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Table && i.InsertText == "dbo.Users");
        }

        [Fact]
        public void ScriptTablesAreOfferedWithoutMetadata()
        {
            var sql = "CREATE TABLE #tmp (b int)\nSELECT * FROM ";
            var items = SuggestionBuilder.Build(ContextAnalyzer.Analyze(sql, sql.Length), null, ScriptModel.Build(sql, sql.Length));

            Assert.Equal("#tmp", Assert.Single(items).InsertText);
        }

        [Fact]
        public void ScriptTablesAreNotOfferedAfterSchema()
        {
            var items = BuildWithScript("CREATE TABLE #tmp (b int)\nSELECT * FROM dbo.|");

            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.ScriptTable);
        }

        [Fact]
        public void VariablesAreOfferedAfterAt()
        {
            var items = BuildWithScript("DECLARE @count int, @name varchar(10)\nSELECT * FROM dbo.Users WHERE Name = @|");

            Assert.Equal(new[] { "@count", "@name" }, items.Where(i => !i.InsertText.StartsWith("@@")).Select(i => i.InsertText).OrderBy(t => t).ToArray());
            Assert.All(items, i => Assert.Equal(SuggestionKind.Variable, i.Kind));
            Assert.Equal("varchar(10)", items.Single(i => i.DisplayText == "@name").Suffix);
        }

        [Fact]
        public void ParametersAreVariables()
        {
            var items = BuildWithScript("CREATE PROCEDURE dbo.p @id int AS\nSELECT * FROM dbo.Users WHERE Id = @|");

            var id = Assert.Single(items, i => !i.InsertText.StartsWith("@@"));
            Assert.Equal("@id", id.InsertText);
            Assert.StartsWith("parameter", id.Description);
        }

        [Fact]
        public void VariablesAlsoAppearInExpressions()
        {
            var items = BuildWithScript("DECLARE @x int\nSELECT * FROM dbo.Users WHERE |");

            Assert.Contains(items, i => i.Kind == SuggestionKind.Variable && i.InsertText == "@x");
        }

        [Fact]
        public void OnlyGlobalVariablesWithoutDeclarations()
        {
            Assert.All(BuildWithScript("SELECT @|"), i => Assert.StartsWith("@@", i.InsertText));
        }

        [Fact]
        public void ReadinessRequestsColumnsBehindStarInCte()
        {
            var sql = "WITH c AS (SELECT * FROM dbo.Users) SELECT  FROM c";
            var caret = sql.IndexOf("  FROM", System.StringComparison.Ordinal) + 1;
            var context = ContextAnalyzer.Analyze(sql, caret);
            var snapshot = SampleDatabase(withColumns: false);

            var readiness = Readiness.Evaluate(context, snapshot, ScriptModel.Build(sql, caret));

            Assert.Equal(ReadinessState.NeedsColumns, readiness.State);
            Assert.Equal("Users", Assert.Single(readiness.MissingColumns).Name);
        }

        [Fact]
        public void ReadinessIsReadyForScriptSources()
        {
            var sql = "CREATE TABLE #t (a int)\nSELECT  FROM #t";
            var caret = sql.IndexOf("  FROM", System.StringComparison.Ordinal) + 1;

            var readiness = Readiness.Evaluate(ContextAnalyzer.Analyze(sql, caret), SampleDatabase(), ScriptModel.Build(sql, caret));

            Assert.Equal(ReadinessState.Ready, readiness.State);
        }
    }
}
