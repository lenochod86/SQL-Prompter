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
    public class JoinSuggestionTests
    {
        private static readonly SuggestionOptions Aliased = new SuggestionOptions(KeywordCase.MatchTyped, new AliasSettings(), null, new JoinSettings { NewTableOnLeft = true });

        /// <summary>Users ← Orders ← OrderLines ← Shipments (the last key has two columns), Products without keys.</summary>
        private static DatabaseSnapshot Shop()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(3, "dbo", "OrderLines", DbObjectKind.Table, false),
                new DbObject(4, "dbo", "Shipments", DbObjectKind.Table, false),
                new DbObject(5, "dbo", "Products", DbObjectKind.Table, false),
            };
            var keys = new[]
            {
                new DbForeignKey("FK_Orders_Users", 2, 1, new[] { new DbForeignKeyColumn("IdUser", "IdUser") }),
                new DbForeignKey("FK_OrderLines_Orders", 3, 2, new[] { new DbForeignKeyColumn("IdOrder", "IdOrder") }),
                new DbForeignKey("FK_Shipments_OrderLines", 4, 3, new[] { new DbForeignKeyColumn("IdOrder", "IdOrder"), new DbForeignKeyColumn("IdLine", "IdLine") }),
            };
            return new DatabaseSnapshot("shop", "dbo", new[] { "dbo" }, objects, "v1", null, null, null, keys,
                new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "IdUser" }, [2] = new[] { "IdOrder" }, [3] = new[] { "IdOrder", "IdLine" } });
        }

        private static IReadOnlyList<Suggestion> After(string sql, SuggestionOptions options = null) =>
            SuggestionBuilder.Build(Analyze(sql), Shop(), null, options ?? Aliased);

        [Fact]
        public void TableThatPointsToATableOfTheQueryComesWithItsClause()
        {
            var items = After("SELECT * FROM dbo.Users u JOIN |");

            var orders = items.Single(i => i.InsertText.StartsWith("dbo.Orders") && i.Detail != null);
            Assert.Equal("dbo.Orders AS o ON o.IdUser = u.IdUser", orders.InsertText);
            Assert.Equal("Orders", orders.DisplayText);
            Assert.Equal("FK_Orders_Users", orders.Suffix);
            Assert.Equal("ON o.IdUser = u.IdUser", orders.Detail);
        }

        [Fact]
        public void TableThatATableOfTheQueryPointsToComesWithItsClause()
        {
            var items = After("SELECT * FROM dbo.Orders o JOIN |");

            Assert.Contains(items, i => i.InsertText == "dbo.Users AS u ON u.IdUser = o.IdUser");
            Assert.Contains(items, i => i.InsertText == "dbo.OrderLines AS ol ON ol.IdOrder = o.IdOrder");
        }

        [Fact]
        public void KeyOverSeveralColumnsGivesAllConditions()
        {
            var items = After("SELECT * FROM dbo.OrderLines ol JOIN |");

            Assert.Contains(items, i => i.InsertText == "dbo.Shipments AS s ON s.IdOrder = ol.IdOrder AND s.IdLine = ol.IdLine");
        }

        [Fact]
        public void RelatedTablesComeBeforeEverythingElse()
        {
            var items = After("SELECT * FROM dbo.Users u JOIN |").OrderBy(i => i.SortText).ToList();

            Assert.StartsWith("dbo.Orders AS o ON", items[0].InsertText);
            Assert.Equal(SuggestionKind.Table, items[0].Kind);
            Assert.Contains(items, i => i.InsertText == "dbo.Products AS p");
            Assert.True(items.FindIndex(i => i.InsertText == "dbo.Products AS p") > 0);
        }

        [Fact]
        public void EveryTableOfTheQueryContributesItsRelations()
        {
            var items = After("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON o.IdUser = u.IdUser JOIN |");

            Assert.Contains(items, i => i.InsertText == "dbo.OrderLines AS ol ON ol.IdOrder = o.IdOrder");
            // Orders is related to Users too, joined again with a new alias.
            Assert.Contains(items, i => i.InsertText == "dbo.Orders AS o1 ON o1.IdUser = u.IdUser");
        }

        [Fact]
        public void UnrelatedTablesGetNoClause()
        {
            Assert.DoesNotContain(After("SELECT * FROM dbo.Products p JOIN |"), i => i.Detail != null);
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Users u, |")]
        [InlineData("SELECT * FROM |")]
        [InlineData("INSERT INTO |")]
        public void OnlyAfterJoin(string sql)
        {
            Assert.DoesNotContain(After(sql), i => i.Detail != null && i.Detail.StartsWith("ON"));
        }

        [Fact]
        public void WithoutAliasesTheTableNameIsUsed()
        {
            var options = new SuggestionOptions(KeywordCase.MatchTyped, new AliasSettings { AddAliases = false }, null, new JoinSettings { NewTableOnLeft = true });

            var items = After("SELECT * FROM dbo.Users u JOIN |", options);

            Assert.Contains(items, i => i.InsertText == "dbo.Orders ON Orders.IdUser = u.IdUser");
        }

        [Fact]
        public void KeywordCaseAppliesToOn()
        {
            var options = new SuggestionOptions(KeywordCase.Lower, new AliasSettings(), null, new JoinSettings { NewTableOnLeft = true });

            Assert.Contains(After("SELECT * FROM dbo.Users u JOIN |", options), i => i.InsertText == "dbo.Orders as o on o.IdUser = u.IdUser");
        }

        [Fact]
        public void TheWordBeingTypedDoesNotTakeAnAlias()
        {
            // "JOIN o" parses "o" as a table named o; the alias o of Orders must still be free.
            var items = After("SELECT * FROM dbo.Users u JOIN o|");

            Assert.Contains(items, i => i.InsertText == "dbo.Orders AS o ON o.IdUser = u.IdUser");
            Assert.Contains(items, i => i.InsertText == "dbo.Orders AS o");
        }

        [Fact]
        public void TwoKeysBetweenTheSameTablesGiveTwoClauses()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Messages", DbObjectKind.Table, false),
            };
            var keys = new[]
            {
                new DbForeignKey("FK_Messages_From", 2, 1, new[] { new DbForeignKeyColumn("IdFrom", "IdUser") }),
                new DbForeignKey("FK_Messages_To", 2, 1, new[] { new DbForeignKeyColumn("IdTo", "IdUser") }),
            };
            var db = new DatabaseSnapshot("db", "dbo", new[] { "dbo" }, objects, "v1", null, null, null, keys);

            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Users u JOIN |"), db, null, Aliased);

            Assert.Contains(items, i => i.InsertText == "dbo.Messages AS m ON m.IdFrom = u.IdUser" && i.Suffix == "FK_Messages_From");
            Assert.Contains(items, i => i.InsertText == "dbo.Messages AS m ON m.IdTo = u.IdUser" && i.Suffix == "FK_Messages_To");
        }

        [Fact]
        public void ColumnsCarryTheirKeyKind()
        {
            var db = Shop().WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
            {
                [2] = Columns("IdOrder", "IdUser", "Note"),
            });

            var items = SuggestionBuilder.Build(Analyze("SELECT o.| FROM dbo.Orders o"), db, null, Aliased);

            Assert.Equal(KeyKind.Primary, items.Single(i => i.DisplayText == "IdOrder").Key);
            Assert.Equal(KeyKind.Foreign, items.Single(i => i.DisplayText == "IdUser").Key);
            Assert.Equal(KeyKind.None, items.Single(i => i.DisplayText == "Note").Key);
        }

        [Fact]
        public void PrimaryKeyIsKnown()
        {
            var db = Shop();

            Assert.Equal(new[] { "IdOrder", "IdLine" }, db.GetPrimaryKey(db.FindObject("dbo", "OrderLines")));
            Assert.Empty(db.GetPrimaryKey(db.FindObject("dbo", "Products")));
        }
    }
}
