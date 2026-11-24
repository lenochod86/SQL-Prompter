using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The parameters of a procedure are offered at an argument of EXEC (#523).</summary>
    public class ExecParameterTests
    {
        private static DatabaseSnapshot Database()
        {
            var procedures = new[]
            {
                new DbProcedure(20, "sales", "GetOrdersOfCustomer", new[]
                {
                    new DbParameter("@IdCustomer", "int", false, false),
                    new DbParameter("@From", "date", false, true, "NULL"),
                    new DbParameter("@Total", "int", true, false),
                }),
            };

            return new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales" }, new DbObject[0], "v1", procedures: procedures);
        }

        private static IReadOnlyList<Suggestion> Names(string sql)
        {
            var caret = sql.IndexOf('|');
            return SuggestionBuilder.Build(Analyze(sql), Database(), ScriptModel.Build(sql.Remove(caret, 1), caret));
        }

        [Theory]
        [InlineData("EXEC sales.GetOrdersOfCustomer @|")]
        [InlineData("EXEC sales.GetOrdersOfCustomer @Id|")]
        [InlineData("EXEC sales.GetOrdersOfCustomer |")]
        [InlineData("EXEC sales.GetOrdersOfCustomer @From = NULL, |")]
        [InlineData("EXECUTE sales.GetOrdersOfCustomer @From = NULL, @|")]
        public void TheArgumentPositionOfAProcedureCallOffersItsParameters(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.Variable, context.Kind);
            Assert.Equal(new[] { "sales", "GetOrdersOfCustomer" }, context.ExecProcedure);
            Assert.Contains(Names(sql), s => s.DisplayText == "@IdCustomer");
        }

        [Fact]
        public void ParametersAlreadyNamedInTheCallAreLeftOut()
        {
            var names = Names("EXEC sales.GetOrdersOfCustomer @IdCustomer = 1, @|").Select(s => s.DisplayText).ToArray();

            Assert.Equal(new[] { "@From", "@Total" }, names);
        }

        [Fact]
        public void ChoosingAParameterInsertsItsNameWithTheEqualsSign()
        {
            var item = Names("EXEC sales.GetOrdersOfCustomer @|").Single(s => s.DisplayText == "@From");

            Assert.Equal("@From = ", item.InsertText);
            Assert.Equal("date", item.Suffix);
            Assert.Contains("= NULL", item.Description);
        }

        [Fact]
        public void OutputParametersAreMarked()
        {
            var item = Names("EXEC sales.GetOrdersOfCustomer @|").Single(s => s.DisplayText == "@Total");

            Assert.Contains("OUTPUT", item.Description);
        }

        [Fact]
        public void TheVariablesOfTheScriptFollowTheParameters()
        {
            var items = Names("DECLARE @c int\nEXEC sales.GetOrdersOfCustomer @|");

            Assert.True(items.Single(s => s.DisplayText == "@c").Group > items.Single(s => s.DisplayText == "@From").Group);
            Assert.Equal(4, items.Count);
        }

        [Theory]
        [InlineData("EXEC sales.GetOrdersOfCustomer @IdCustomer = @|")]
        [InlineData("EXEC sales.GetOrdersOfCustomer @IdCustomer =|")]
        [InlineData("SELECT @|")]
        public void AValueOrAnotherStatementIsNotAnArgumentPosition(string sql)
        {
            Assert.Null(Analyze(sql).ExecProcedure);
        }

        [Fact]
        public void AnUnknownProcedureOffersOnlyTheVariables()
        {
            Assert.Empty(Names("EXEC dbo.Nothing @|"));
        }
    }
}
