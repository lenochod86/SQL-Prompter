using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class FunctionTests
    {
        private static DatabaseSnapshot WithFunctions()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(10, "dbo", "fnNow", DbObjectKind.ScalarFunction, false, null, 0),
                new DbObject(11, "dbo", "fnTax", DbObjectKind.ScalarFunction, false, null, 2),
                new DbObject(12, "sales", "fnDiscount", DbObjectKind.ScalarFunction, false, null, 1),
                new DbObject(13, "dbo", "fnOrdersOf", DbObjectKind.TableFunction, false, null, 1),
                new DbObject(14, "dbo", "fnAllUsers", DbObjectKind.TableFunction, false, null, 0),
                new DbObject(200, "sys", "fn_helpcollations", DbObjectKind.ScalarFunction, true, null, 0),
            };

            var snapshot = new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales", "sys" }, objects, "v1");
            return snapshot.WithColumns(new System.Collections.Generic.Dictionary<int, System.Collections.Generic.IReadOnlyList<DbColumn>>
            {
                [1] = Columns("Id", "Name"),
                [13] = Columns("Id"),
                [14] = Columns("Id"),
            });
        }

        [Fact]
        public void ScalarFunctionsAreOfferedInExpressionsWithTheirParentheses()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT fn|"), WithFunctions());

            var tax = items.Single(i => i.Kind == SuggestionKind.ScalarFunction && i.DisplayText == "fnTax");
            Assert.Equal("dbo.fnTax()", tax.InsertText);
            Assert.Equal(1, tax.CaretBack);
            Assert.Equal("dbo", tax.Suffix);

            var now = items.Single(i => i.DisplayText == "fnNow");
            Assert.Equal("dbo.fnNow()", now.InsertText);
            Assert.Equal(0, now.CaretBack);
            Assert.DoesNotContain(items, i => i.DisplayText == "fn_helpcollations");
        }

        [Fact]
        public void ScalarFunctionsOfASchemaAfterTheSchemaName()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT sales.|"), WithFunctions());

            var discount = Assert.Single(items);
            Assert.Equal("fnDiscount()", discount.InsertText);
            Assert.Equal(1, discount.CaretBack);
        }

        [Fact]
        public void ScalarFunctionsAreNotTableSources()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), WithFunctions());

            Assert.DoesNotContain(items, i => i.DisplayText == "fnTax" || i.DisplayText == "fnNow");
            Assert.DoesNotContain(SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.|"), WithFunctions()), i => i.DisplayText == "fnTax");
        }

        [Fact]
        public void TableFunctionsGetParenthesesAndTheCaretGoesInsideWhenTheyTakeParameters()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM fn|"), WithFunctions());

            var withParameter = items.Single(i => i.DisplayText == "fnOrdersOf");
            Assert.Equal("dbo.fnOrdersOf()", withParameter.InsertText);
            Assert.Equal(1, withParameter.CaretBack);
            var without = items.Single(i => i.DisplayText == "fnAllUsers");
            Assert.Equal("dbo.fnAllUsers()", without.InsertText);
            Assert.Equal(0, without.CaretBack);
        }


        [Fact]
        public void BuiltInFunctionsComeWithParentheses()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT |"), WithFunctions());

            var count = items.Single(i => i.Kind == SuggestionKind.Function && i.DisplayText == "COUNT");
            Assert.Equal("COUNT()", count.InsertText);
            Assert.Equal(1, count.CaretBack);
            var now = items.Single(i => i.Kind == SuggestionKind.Function && i.DisplayText == "GETDATE");
            Assert.Equal("GETDATE()", now.InsertText);
            Assert.Equal(0, now.CaretBack);
        }

        [Fact]
        public void BuiltInFunctionsFollowTheTypedCase()
        {
            var items = SuggestionBuilder.Build(Analyze("select get|"), WithFunctions());

            Assert.Equal("getdate()", items.Single(i => i.Kind == SuggestionKind.Function && i.DisplayText == "getdate").InsertText);
        }
        [Fact]
        public void ForcedKeywordCaseKeepsTheCaretPosition()
        {
            var options = new SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.Upper, new SqlPrompter.Core.Settings.AliasSettings(), null, new SqlPrompter.Core.Settings.JoinSettings());
            var items = SuggestionBuilder.Build(Analyze("SELECT fn|"), WithFunctions(), null, options);

            Assert.Equal(1, items.Single(i => i.DisplayText == "fnTax").CaretBack);
        }
    }
}
