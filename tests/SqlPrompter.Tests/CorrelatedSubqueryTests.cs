using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The first table of a correlated subquery: related tables first, then the correlation as a WHERE condition (#216).</summary>
    public class CorrelatedSubqueryTests
    {
        private static DatabaseSnapshot Database()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "OrderItems", DbObjectKind.Table, false),
                new DbObject(3, "dbo", "Payments", DbObjectKind.Table, false),
                new DbObject(4, "dbo", "Products", DbObjectKind.Table, false),
            };
            var keys = new[]
            {
                new DbForeignKey("FK_OrderItems_Orders", 2, 1, new[] { new DbForeignKeyColumn("IdOrder", "IdOrder") }),
                new DbForeignKey("FK_Payments_Orders", 3, 1, new[] { new DbForeignKeyColumn("IdOrder", "IdOrder") }),
            };
            return new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, objects, "v1", foreignKeys: keys).WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
            {
                [1] = Columns("IdOrder", "Total"),
                [2] = Columns("IdOrderItem", "IdOrder", "Qty"),
                [3] = Columns("IdPayment", "IdOrder", "Amount"),
                [4] = Columns("IdProduct", "Name"),
            });
        }

        private static IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), Database());

        [Theory]
        [InlineData("SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM |")]
        [InlineData("SELECT * FROM dbo.Orders o WHERE NOT EXISTS (SELECT 1 FROM |")]
        [InlineData("SELECT * FROM dbo.Orders o WHERE o.IdOrder IN (SELECT IdOrder FROM |")]
        [InlineData("SELECT * FROM dbo.Orders o CROSS APPLY (SELECT 1 AS x FROM |")]
        public void RelatedTablesComeFirstWithTheRelationOnTheRight(string sql)
        {
            var items = Items(sql);
            var first = items.Where(i => i.Group == JoinSuggestions.Group).Select(i => i.DisplayText).OrderBy(x => x).ToArray();

            Assert.Equal(new[] { "OrderItems", "Payments" }, first);
            Assert.Equal("FK_OrderItems_Orders", items.Single(i => i.DisplayText == "OrderItems" && i.Group == JoinSuggestions.Group).Suffix);
        }

        [Fact]
        public void RelatedTablesComeFirstWhileTheNameIsBeingTyped()
        {
            var first = Items("SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM p|").Where(i => i.Group == JoinSuggestions.Group).Select(i => i.DisplayText).OrderBy(x => x);

            Assert.Equal(new[] { "OrderItems", "Payments" }, first);
        }

        [Fact]
        public void TheRelatedTableIsInsertedWithAnAliasAndWithoutOn()
        {
            var item = Items("SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM |").Single(i => i.DisplayText == "OrderItems" && i.Group == JoinSuggestions.Group);

            Assert.DoesNotContain("ON", item.InsertText.ToUpperInvariant().Replace("ORDER", string.Empty));
            Assert.StartsWith("dbo.OrderItems", item.InsertText);
        }

        [Fact]
        public void TheOtherTablesFollow()
        {
            Assert.Contains(Items("SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM |"), i => i.DisplayText == "Products");
        }

        [Fact]
        public void AQueryWithoutAnOuterQueryIsUnchanged()
        {
            Assert.DoesNotContain(Items("SELECT * FROM |"), i => i.Group == JoinSuggestions.Group);
        }

        [Fact]
        public void TheSecondTableOfASubqueryIsOfferedAsAJoin()
        {
            var items = Items("SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM dbo.OrderItems oi JOIN |");

            Assert.DoesNotContain(items, i => i.Group == JoinSuggestions.Group && i.DisplayText == "Payments" && !i.InsertText.ToUpperInvariant().Contains(" ON "));
        }

        [Fact]
        public void AfterTheTableTheCorrelationIsOfferedAsWhere()
        {
            var item = Items("SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM dbo.OrderItems oi |").Single(i => i.Kind == SuggestionKind.JoinCondition && i.InsertText.StartsWith("WHERE"));

            Assert.Equal("WHERE o.IdOrder = oi.IdOrder", item.InsertText);
        }

        [Fact]
        public void NoCorrelationWithoutAnOuterQuery()
        {
            Assert.DoesNotContain(Items("SELECT * FROM dbo.OrderItems oi |"), i => i.Kind == SuggestionKind.JoinCondition && i.InsertText.StartsWith("WHERE"));
        }
    }
}
