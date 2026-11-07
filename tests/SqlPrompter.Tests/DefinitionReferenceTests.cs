using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class DefinitionReferenceTests
    {
        private static DatabaseSnapshot Database()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "ActiveUsers", DbObjectKind.View, false),
                new DbObject(3, "dbo", "Alias", DbObjectKind.Synonym, false, new[] { "dbo", "Users" }),
                new DbObject(4, "dbo", "fnTax", DbObjectKind.ScalarFunction, false, null, 1),
                new DbObject(5, "dbo", "fnOrders", DbObjectKind.TableFunction, false, null, 0),
            };

            return new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, objects, "v1", procedures: new[] { new DbProcedure(9, "dbo", "AddUser", new DbParameter[0]) });
        }

        [Fact]
        public void TablesViewsAndFunctionsCarryTheObjectTheirDefinitionBelongsTo()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), Database());

            Assert.Equal("Users", items.Single(i => i.DisplayText == "Users").DefinitionName);
            Assert.Equal("dbo", items.Single(i => i.DisplayText == "ActiveUsers").DefinitionSchema);
            Assert.Equal("fnOrders", items.Single(i => i.DisplayText == "fnOrders").DefinitionName);
        }

        [Fact]
        public void SynonymsSchemasAndKeywordsHaveNoDefinition()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), Database());

            Assert.Null(items.Single(i => i.DisplayText == "Alias").DefinitionName);
            Assert.All(items.Where(i => i.Kind == SuggestionKind.Schema || i.Kind == SuggestionKind.Keyword), i => Assert.Null(i.DefinitionName));
        }

        [Fact]
        public void ScalarFunctionsInExpressionsAndProceduresAfterExecCarryTheirObject()
        {
            var functions = SuggestionBuilder.Build(Analyze("SELECT fn|"), Database());
            Assert.Equal("fnTax", functions.Single(i => i.Kind == SuggestionKind.ScalarFunction).DefinitionName);

            var procedures = SuggestionBuilder.Build(Analyze("EXEC |"), Database());
            Assert.Equal("AddUser", procedures.Single(i => i.Kind == SuggestionKind.Procedure && i.Suffix != "system").DefinitionName);
        }

        [Fact]
        public void ForcedKeywordCaseKeepsTheDefinition()
        {
            var options = new SuggestionOptions(KeywordCase.Upper, new AliasSettings());
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), Database(), null, options);

            Assert.Equal("Users", items.Single(i => i.DisplayText == "Users").DefinitionName);
        }
    }
}
