using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>System procedures after EXEC with their parameters (#218).</summary>
    public class SystemProcedureTests
    {
        private static Suggestion Item(string sql, string name) =>
            SuggestionBuilder.Build(Analyze(sql), SampleDatabase()).FirstOrDefault(i => i.DisplayText == name);

        [Theory]
        [InlineData("sp_help")]
        [InlineData("sp_helptext")]
        [InlineData("sp_helpindex")]
        [InlineData("sp_rename")]
        [InlineData("sp_who2")]
        [InlineData("sp_spaceused")]
        [InlineData("sp_columns")]
        [InlineData("sp_executesql")]
        public void TheCommonSystemProceduresAreOfferedAfterExec(string name)
        {
            var item = Item("EXEC |", name);

            Assert.NotNull(item);
            Assert.Equal(SuggestionKind.Procedure, item.Kind);
            Assert.Equal("system", item.Suffix);
        }

        [Fact]
        public void ExecuteAndTheSchemaFreeFormWorkToo() =>
            Assert.NotNull(Item("EXECUTE |", "sp_help"));

        [Fact]
        public void SpExecutesqlInsertsStmtAndParams()
        {
            var item = Item("EXEC |", "sp_executesql");

            Assert.StartsWith("sp_executesql @stmt = N''", item.InsertText);
            Assert.Contains("@params = NULL", item.InsertText);
        }

        [Fact]
        public void ParametersWithDefaultsKeepThem()
        {
            var item = Item("EXEC |", "sp_rename");

            Assert.Contains("@objname = N''", item.InsertText);
            Assert.Contains("@newname = ", item.InsertText);
            Assert.Contains("@objtype = NULL", item.InsertText);
        }

        [Fact]
        public void TheDescriptionShowsTheSignature() =>
            Assert.Contains("@objname nvarchar(776)", Item("EXEC |", "sp_helptext").Description);

        [Fact]
        public void SystemProceduresAreNotOfferedWhereNoProcedureGoes()
        {
            Assert.Null(Item("SELECT * FROM |", "sp_help"));
            Assert.Null(Item("SELECT |", "sp_help"));
        }

        [Fact]
        public void TheyComeAfterTheProceduresOfTheDatabase()
        {
            var items = SuggestionBuilder.Build(Analyze("EXEC |"), SampleDatabase());
            var system = items.First(i => i.DisplayText == "sp_help");

            Assert.True(system.Group >= 2);
        }

        [Fact]
        public void TheCatalogFindsByNameIgnoringCase()
        {
            Assert.NotNull(SystemProcedures.Find("SP_HELP"));
            Assert.Null(SystemProcedures.Find("nope"));
            Assert.All(SystemProcedures.All, p => Assert.StartsWith("sp_", p.Name));
        }

        [Fact]
        public void ParameterInfoKnowsThem()
        {
            var sql = "EXEC sp_rename 'a', ";
            var info = ParameterInfoFinder.Find(sql, sql.Length, SampleDatabase());

            Assert.NotNull(info);
            Assert.Equal(1, info.Current);
            Assert.StartsWith("@newname sysname", info.Parameters[1]);
        }
    }
}
