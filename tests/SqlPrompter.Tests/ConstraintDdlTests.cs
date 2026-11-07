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
    /// <summary>Constraint names by convention, existing constraint names and key columns in REFERENCES (#213).</summary>
    public class ConstraintDdlTests
    {
        private static DatabaseSnapshot Database()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(3, "dbo", "OrderItems", DbObjectKind.Table, false),
            };
            var constraints = new[]
            {
                new DbConstraint("dbo", "Orders", "PK_Orders", "primary key"),
                new DbConstraint("dbo", "Orders", "FK_Orders_Users", "foreign key"),
                new DbConstraint("dbo", "Users", "PK_Users", "primary key"),
            };
            var keys = new Dictionary<int, IReadOnlyList<string>>
            {
                [1] = new[] { "Id" },
                [2] = new[] { "Id" },
                [3] = new[] { "IdOrder", "Line" },
            };
            return new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, objects, "v1", primaryKeys: keys, constraints: constraints)
                .WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
                {
                    [1] = Columns("Id", "Name", "Email"),
                    [2] = Columns("Id", "UserId", "Total"),
                    [3] = Columns("IdOrder", "Line", "Qty"),
                });
        }

        private static string[] Names(string sql) =>
            SuggestionBuilder.Build(Analyze(sql), Database()).Select(i => i.DisplayText).ToArray();

        [Fact]
        public void ATableLevelConstraintGetsNamesFromTheTable() =>
            Assert.Equal(new[] { "PK_Orders", "FK_Orders", "UQ_Orders", "CK_Orders", "DF_Orders" },
                Names("CREATE TABLE dbo.Orders (Id int, CONSTRAINT |"));

        [Fact]
        public void AColumnConstraintAlsoUsesTheColumn() =>
            Assert.Equal(new[] { "DF_Orders_Total", "UQ_Orders_Total", "FK_Orders_Total", "CK_Orders_Total", "PK_Orders" },
                Names("CREATE TABLE dbo.Orders (Id int, Total decimal(18,2) CONSTRAINT |"));

        [Fact]
        public void AlterTableAddConstraintWorksToo()
        {
            Assert.Equal("PK_Users", Names("ALTER TABLE dbo.Users ADD CONSTRAINT |")[0]);
            Assert.Equal("DF_Users_Name", Names("ALTER TABLE dbo.Users ADD Name nvarchar(50) CONSTRAINT |")[0]);
            Assert.Equal("DF_Users_Name", Names("ALTER TABLE dbo.Users ADD COLUMN Name nvarchar(50) CONSTRAINT |")[0]);
        }

        [Theory]
        [InlineData("ALTER TABLE dbo.Orders DROP CONSTRAINT |")]
        [InlineData("ALTER TABLE dbo.Orders NOCHECK CONSTRAINT |")]
        [InlineData("ALTER TABLE dbo.Orders CHECK CONSTRAINT |")]
        public void ExistingConstraintsOfTheTableAreOffered(string sql) =>
            Assert.Equal(new[] { "FK_Orders_Users", "PK_Orders" }, Names(sql).OrderBy(n => n).ToArray());

        [Fact]
        public void TheTypeOfTheExistingConstraintIsShown()
        {
            var item = SuggestionBuilder.Build(Analyze("ALTER TABLE dbo.Orders DROP CONSTRAINT |"), Database()).First(i => i.DisplayText == "FK_Orders_Users");

            Assert.Equal("foreign key", item.Suffix);
            Assert.Contains("dbo.Orders", item.Description);
        }

        [Fact]
        public void ConstraintsOfOtherTablesAreNotOffered() =>
            Assert.DoesNotContain("PK_Users", Names("ALTER TABLE dbo.Orders DROP CONSTRAINT |"));

        [Fact]
        public void ReferencesParenthesisOffersTheKeyColumnsOfTheTableFirst()
        {
            var items = SuggestionBuilder.Build(Analyze("CREATE TABLE dbo.Orders (UserId int REFERENCES dbo.Users (|"), Database());

            Assert.Equal(ContextKind.InsertColumns, Analyze("CREATE TABLE dbo.Orders (UserId int REFERENCES dbo.Users (|").Kind);
            Assert.Equal(new[] { "Id", "Name", "Email" }, items.OrderBy(i => i.Group).Select(i => i.DisplayText).ToArray());
        }

        [Fact]
        public void ACompositeKeyGetsAnItemWithAllItsColumns()
        {
            var items = SuggestionBuilder.Build(Analyze("ALTER TABLE dbo.Orders ADD FOREIGN KEY (IdOrder, Line) REFERENCES dbo.OrderItems (|"), Database());
            var first = items.OrderBy(i => i.Group).First();

            Assert.Equal("(primary key)", first.DisplayText);
            Assert.Equal("IdOrder, Line", first.InsertText);
        }

        [Fact]
        public void NothingChangesWhereNoConstraintNameIsExpected()
        {
            Assert.DoesNotContain("PK_Orders", Names("CREATE TABLE dbo.Orders (Id int, Name |"));
            Assert.DoesNotContain("PK_Orders", Names("SELECT * FROM |"));
        }
    }
}
