using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The types of same-named columns first in DECLARE @x | and CAST(x AS | (#217).</summary>
    public class ColumnTypeTests
    {
        private static DatabaseSnapshot Database(bool withColumns = true)
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Customers", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(3, "dbo", "Archive", DbObjectKind.Table, false),
            };
            var snapshot = new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, objects, "v1");
            return !withColumns ? snapshot : snapshot.WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
            {
                [1] = new[] { new DbColumn("IdCustomer", "int", false), new DbColumn("Name", "nvarchar(100)", false), new DbColumn("Total", "decimal(18,2)", true) },
                [2] = new[] { new DbColumn("IdOrder", "bigint", false), new DbColumn("IdCustomer", "int", false), new DbColumn("Total", "decimal(18,2)", false) },
                [3] = new[] { new DbColumn("IdCustomer", "smallint", false), new DbColumn("Name", "nvarchar(100)", true) },
            });
        }

        private static IReadOnlyList<Suggestion> Items(string sql, DatabaseSnapshot database = null) => SuggestionBuilder.Build(Analyze(sql), database ?? Database());

        private static string[] First(string sql) => Items(sql).Where(i => i.Group < 0).OrderBy(i => i.Group).Select(i => i.InsertText).ToArray();

        [Fact]
        public void DeclareOffersTheTypeOfTheColumnWithTheSameName()
        {
            Assert.Equal(new[] { "decimal(18,2)" }, First("DECLARE @Total |"));
        }

        [Fact]
        public void TheMostCommonTypeComesFirstAndTheRelationIsShown()
        {
            var items = Items("DECLARE @IdCustomer |").Where(i => i.Group < 0).OrderBy(i => i.Group).ToList();

            Assert.Equal(new[] { "int", "smallint" }, items.Select(i => i.InsertText).ToArray());
            Assert.Contains("dbo.Customers.IdCustomer", items[0].Description);
            Assert.Contains("dbo.Orders.IdCustomer", items[0].Description);
        }

        [Fact]
        public void TheNameIsMatchedWithoutRegardToCase()
        {
            Assert.Equal(new[] { "nvarchar(100)" }, First("DECLARE @name |"));
        }

        [Fact]
        public void TheExactLengthComesFromTheColumn()
        {
            Assert.Equal("nvarchar(100)", First("DECLARE @Name |")[0]);
        }

        [Fact]
        public void TheOtherTypesFollow()
        {
            var items = Items("DECLARE @Total |");

            Assert.Contains(items, i => i.Kind == SuggestionKind.DataType && i.InsertText.ToUpperInvariant() == "INT");
            Assert.True(items.Count > 10);
        }

        [Fact]
        public void NoColumnWithTheNameLeavesTheListAsItWas()
        {
            Assert.Empty(First("DECLARE @Nothing |"));
        }

        [Fact]
        public void CastOffersTheTypeOfItsOperand()
        {
            Assert.Equal(new[] { "decimal(18,2)" }, First("SELECT CAST(Total AS |"));
            Assert.Equal(new[] { "nvarchar(100)" }, First("SELECT TRY_CAST(Name AS |"));
        }

        [Fact]
        public void ColumnsThatAreNotLoadedGiveNothing()
        {
            Assert.Empty(Items("DECLARE @Total |", Database(withColumns: false)).Where(i => i.Group < 0));
        }
    }
}
