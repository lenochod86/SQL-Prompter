using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>
    /// JOIN suggestions on a schema built like real ones: the key column is called <c>IdTable</c> everywhere, a table can reference another
    /// one twice under role names, a table references itself, a junction table has a key over two columns. The same schema is in
    /// tests/TestDatabase/joins-idtable.sql for trying it in SSMS.
    /// </summary>
    public class JoinRealisticSchemaTests
    {
        private static IReadOnlyList<DbColumn> Cols(params string[] columns) =>
            columns.Select(c => new DbColumn(c.Split(':')[0], c.Contains(":") ? c.Split(':')[1] : "int", false)).ToList();

        private static DatabaseSnapshot Schema()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Customers", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Addresses", DbObjectKind.Table, false),
                new DbObject(3, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(4, "dbo", "OrderItems", DbObjectKind.Table, false),
                new DbObject(5, "dbo", "Products", DbObjectKind.Table, false),
                new DbObject(6, "dbo", "Categories", DbObjectKind.Table, false),
                new DbObject(7, "dbo", "Employees", DbObjectKind.Table, false),
                new DbObject(8, "dbo", "Students", DbObjectKind.Table, false),
                new DbObject(9, "dbo", "Courses", DbObjectKind.Table, false),
                new DbObject(10, "dbo", "Enrollments", DbObjectKind.Table, false),
                new DbObject(11, "dbo", "AuditLog", DbObjectKind.Table, false),
            };
            var keys = new[]
            {
                new DbForeignKey("FK_Orders_Customers", 3, 1, new[] { new DbForeignKeyColumn("IdCustomer", "IdCustomer") }),
                new DbForeignKey("FK_Orders_BillingAddress", 3, 2, new[] { new DbForeignKeyColumn("IdBillingAddress", "IdAddress") }),
                new DbForeignKey("FK_Orders_ShippingAddress", 3, 2, new[] { new DbForeignKeyColumn("IdShippingAddress", "IdAddress") }),
                new DbForeignKey("FK_OrderItems_Orders", 4, 3, new[] { new DbForeignKeyColumn("IdOrder", "IdOrder") }),
                new DbForeignKey("FK_OrderItems_Products", 4, 5, new[] { new DbForeignKeyColumn("IdProduct", "IdProduct") }),
                new DbForeignKey("FK_Products_Categories", 5, 6, new[] { new DbForeignKeyColumn("IdCategory", "IdCategory") }),
                new DbForeignKey("FK_Categories_Parent", 6, 6, new[] { new DbForeignKeyColumn("IdParentCategory", "IdCategory") }),
                new DbForeignKey("FK_Employees_Manager", 7, 7, new[] { new DbForeignKeyColumn("IdManager", "IdEmployee") }),
                new DbForeignKey("FK_Enrollments_Students", 10, 8, new[] { new DbForeignKeyColumn("IdStudent", "IdStudent") }),
                new DbForeignKey("FK_Enrollments_Courses", 10, 9, new[] { new DbForeignKeyColumn("IdCourse", "IdCourse") }),
            };
            var primaryKeys = new Dictionary<int, IReadOnlyList<string>>
            {
                [1] = new[] { "IdCustomer" },
                [2] = new[] { "IdAddress" },
                [3] = new[] { "IdOrder" },
                [4] = new[] { "IdOrderItem" },
                [5] = new[] { "IdProduct" },
                [6] = new[] { "IdCategory" },
                [7] = new[] { "IdEmployee" },
                [8] = new[] { "IdStudent" },
                [9] = new[] { "IdCourse" },
                [10] = new[] { "IdStudent", "IdCourse" },
            };
            return new DatabaseSnapshot("school", "dbo", new[] { "dbo" }, objects, "v1", null, null, null, keys, primaryKeys)
                .WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
                {
                    [1] = Cols("IdCustomer", "Name:nvarchar(100)"),
                    [2] = Cols("IdAddress", "Street:nvarchar(100)"),
                    [3] = Cols("IdOrder", "IdCustomer", "IdBillingAddress", "IdShippingAddress", "Created:datetime"),
                    [4] = Cols("IdOrderItem", "IdOrder", "IdProduct", "Quantity"),
                    [5] = Cols("IdProduct", "IdCategory", "Name:nvarchar(100)"),
                    [6] = Cols("IdCategory", "IdParentCategory", "Name:nvarchar(100)"),
                    [7] = Cols("IdEmployee", "IdManager", "Name:nvarchar(100)"),
                    [8] = Cols("IdStudent", "Name:nvarchar(100)"),
                    [9] = Cols("IdCourse", "Name:nvarchar(100)"),
                    [10] = Cols("IdStudent", "IdCourse", "Grade"),
                    [11] = Cols("IdAudit", "IdCustomer", "IdOrder", "Message:nvarchar(max)"),
                });
        }

        private static SuggestionOptions Options() => new SuggestionOptions(KeywordCase.MatchTyped, null, null, new JoinSettings { NewTableOnLeft = true });

        private static IReadOnlyList<string> Conditions(string sql) =>
            SuggestionBuilder.Build(Analyze(sql), Schema(), null, Options())
                .Where(i => i.Kind == SuggestionKind.JoinCondition)
                .OrderBy(i => i.SortText)
                .Select(i => i.InsertText)
                .ToList();

        private static IReadOnlyList<string> Tables(string sql) =>
            SuggestionBuilder.Build(Analyze(sql), Schema(), null, Options())
                .Where(i => i.Kind == SuggestionKind.Table)
                .OrderBy(i => i.SortText)
                .Select(i => i.DisplayText)
                .ToList();

        [Fact]
        public void TwoKeysToTheSameTableGiveTwoConditions()
        {
            var conditions = Conditions("SELECT * FROM dbo.Orders o JOIN dbo.Addresses a ON |");

            Assert.Contains("a.IdAddress = o.IdBillingAddress", conditions);
            Assert.Contains("a.IdAddress = o.IdShippingAddress", conditions);
            Assert.Equal(new[] { "a.IdAddress = o.IdBillingAddress", "a.IdAddress = o.IdShippingAddress" }, conditions.Take(2).OrderBy(c => c));
        }

        [Fact]
        public void TheKeyOfTheNewTableToTheEarlierTableIsFoundInBothDirections()
        {
            Assert.Equal("o.IdCustomer = c.IdCustomer", Conditions("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON |").First());
            Assert.Equal("c.IdCustomer = o.IdCustomer", Conditions("SELECT * FROM dbo.Orders o JOIN dbo.Customers c ON |").First());
        }

        [Fact]
        public void ARoleNameColumnIsPairedWithTheKeyItReferences()
        {
            var conditions = Conditions("SELECT * FROM dbo.Categories c JOIN dbo.Categories p ON |");

            Assert.Contains("p.IdCategory = c.IdParentCategory", conditions);
            Assert.Contains("p.IdParentCategory = c.IdCategory", conditions);
        }

        [Fact]
        public void ASelfReferenceGivesTheConditionBetweenTheTwoAliases()
        {
            var conditions = Conditions("SELECT * FROM dbo.Employees e JOIN dbo.Employees m ON |");

            Assert.Contains("m.IdEmployee = e.IdManager", conditions);
            Assert.Contains("m.IdManager = e.IdEmployee", conditions);
            Assert.Equal(2, conditions.Take(2).Count(c => c.Contains("IdManager")));
        }

        [Fact]
        public void AJunctionTableIsLinkedToBothSidesInAChain()
        {
            var first = Conditions("SELECT * FROM dbo.Students s JOIN dbo.Enrollments en ON |");
            var second = Conditions("SELECT * FROM dbo.Students s JOIN dbo.Enrollments en ON en.IdStudent = s.IdStudent JOIN dbo.Courses c ON |");

            Assert.Equal("en.IdStudent = s.IdStudent", first.First());
            Assert.Equal("c.IdCourse = en.IdCourse", second.First());
        }

        [Fact]
        public void TablesLinkedByAKeyComeFirstAfterJoin()
        {
            var tables = Tables("SELECT * FROM dbo.Orders o JOIN |");

            // Addresses appears once per key (billing and shipping).
            var linked = new[] { "Addresses", "Customers", "OrderItems" };
            Assert.Equal(linked, tables.Take(4).Distinct().OrderBy(t => t));
            Assert.True(tables.ToList().IndexOf("Products") > 3);
        }

        [Fact]
        public void ATableWithoutKeysIsStillLinkedByTheIdTableConvention()
        {
            var conditions = Conditions("SELECT * FROM dbo.Orders o JOIN dbo.AuditLog a ON |");

            Assert.Contains("a.IdOrder = o.IdOrder", conditions);
            Assert.Contains("a.IdCustomer = o.IdCustomer", conditions);
        }

        [Fact]
        public void AColumnOfAnotherTableWithTheSameNameIsNotMistakenForAKey()
        {
            var conditions = Conditions("SELECT * FROM dbo.Customers c JOIN dbo.Products p ON |");

            Assert.DoesNotContain(conditions, c => c.Contains("IdCategory"));
        }
    }
}
