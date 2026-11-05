using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;

namespace SqlPrompter.Tests
{
    public class ParameterInfoTests
    {
        private static DatabaseSnapshot Database()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(10, "dbo", "fnTax", DbObjectKind.ScalarFunction, false, null, 2),
                new DbObject(11, "sales", "fnOrdersOf", DbObjectKind.TableFunction, false, null, 1),
                new DbObject(12, "dbo", "fnNow", DbObjectKind.ScalarFunction, false, null, 0),
            };

            var procedures = new[]
            {
                new DbProcedure(20, "dbo", "AddOrder", new[]
                {
                    new DbParameter("@IdCustomer", "int", false, false),
                    new DbParameter("@Note", "nvarchar(50)", false, true, "NULL"),
                    new DbParameter("@IdOrder", "int", true, false),
                }),
            };

            var functions = new Dictionary<int, IReadOnlyList<DbParameter>>
            {
                [10] = new[] { new DbParameter("@Amount", "money", false, false), new DbParameter("@Rate", "decimal(5,2)", false, false) },
                [11] = new[] { new DbParameter("@IdCustomer", "int", false, false) },
            };

            return new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales" }, objects, "v1", procedures: procedures, functionParameters: functions);
        }

        private static ParameterInfo Find(string sqlWithCaret)
        {
            var caret = sqlWithCaret.IndexOf('|');
            return ParameterInfoFinder.Find(sqlWithCaret.Remove(caret, 1), caret, Database());
        }

        [Fact]
        public void FunctionCallShowsItsParametersAndTheCurrentOne()
        {
            var info = Find("SELECT dbo.fnTax(|");

            Assert.Equal("dbo.fnTax", info.Name);
            Assert.Equal(new[] { "@Amount money", "@Rate decimal(5,2)" }, info.Parameters.ToArray());
            Assert.Equal(0, info.Current);
        }

        [Theory]
        [InlineData("SELECT dbo.fnTax(1|", 0)]
        [InlineData("SELECT dbo.fnTax(1,|", 1)]
        [InlineData("SELECT dbo.fnTax(1, |", 1)]
        [InlineData("SELECT dbo.fnTax(1, 2|)", 1)]
        [InlineData("SELECT dbo.fnTax(ISNULL(a, b), |", 1)]
        [InlineData("SELECT dbo.fnTax(1, 2, |", -1)]
        public void TheCurrentParameterFollowsTheCommas(string sql, int expected) => Assert.Equal(expected, Find(sql).Current);

        [Fact]
        public void NestedBuiltInFunctionShowsTheOuterCall()
        {
            var info = Find("SELECT dbo.fnTax(ISNULL(|");

            Assert.Equal("dbo.fnTax", info.Name);
            Assert.Equal(0, info.Current);
        }

        [Fact]
        public void TableFunctionInFrom()
        {
            var info = Find("SELECT * FROM sales.fnOrdersOf(|");

            Assert.Equal("sales.fnOrdersOf", info.Name);
            Assert.Equal(new[] { "@IdCustomer int" }, info.Parameters.ToArray());
        }

        [Theory]
        [InlineData("SELECT |")]
        [InlineData("SELECT dbo.fnTax(1, 2)|")]
        [InlineData("SELECT dbo.fnTax(1, 2); SELECT (|")]
        [InlineData("SELECT * FROM dbo.Orders WHERE Id IN (|")]
        [InlineData("SELECT dbo.fnNow(|")]
        [InlineData("SELECT UPPER(|")]
        [InlineData("SELECT dbo.fnTax('(|')")]
        public void NothingIsShownOutsideACallOfAKnownFunction(string sql) => Assert.Null(Find(sql));

        [Fact]
        public void ProcedureAfterExecShowsItsParameters()
        {
            var info = Find("EXEC dbo.AddOrder |");

            Assert.Equal("dbo.AddOrder", info.Name);
            Assert.Equal(new[] { "@IdCustomer int", "@Note nvarchar(50) = NULL", "@IdOrder int OUTPUT" }, info.Parameters.ToArray());
            Assert.Equal(0, info.Current);
        }

        [Theory]
        [InlineData("EXEC dbo.AddOrder 5,| ", 1)]
        [InlineData("EXEC AddOrder 5, 'x',|", 2)]
        [InlineData("EXECUTE dbo.AddOrder @IdCustomer = 5,|", 1)]
        [InlineData("EXEC @r = dbo.AddOrder @IdCustomer = 5, @IdOrder = @o,|", 2)]
        [InlineData("EXEC dbo.AddOrder @IdOrder = |", 2)]
        [InlineData("EXEC dbo.AddOrder @Note = N'a, b', |", 1)]
        [InlineData("EXEC dbo.AddOrder 1, 2, 3, 4,|", -1)]
        public void ProcedureParameterByPositionOrByName(string sql, int expected) => Assert.Equal(expected, Find(sql).Current);

        [Theory]
        [InlineData("EXEC dbo.AddOrder|")]
        [InlineData("EXEC |")]
        [InlineData("EXEC dbo.Unknown |")]
        [InlineData("SELECT 1; SELECT |")]
        public void NoInfoWhereNoProcedureIsBeingCalled(string sql) => Assert.Null(Find(sql));

        [Fact]
        public void TheProcedureOfTheStatementBeforeIsNotUsed()
        {
            Assert.Null(Find("EXEC dbo.AddOrder 1\nSELECT |"));
        }
    }
}
