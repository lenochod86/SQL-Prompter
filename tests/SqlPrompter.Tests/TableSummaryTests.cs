using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The Summary tab of a table lists its keys and indexes below the columns (#351).</summary>
    public class TableSummaryTests
    {
        [Fact]
        public void KeysAndIndexesFollowTheColumns()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Countries", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Cities", DbObjectKind.Table, false),
                new DbObject(3, "dbo", "Visits", DbObjectKind.Table, false),
            };
            var keys = new[]
            {
                new DbForeignKey("FK_Cities_Countries", 2, 1, new[] { new DbForeignKeyColumn("IdCountry", "IdCountry") }),
                new DbForeignKey("FK_Visits_Cities", 3, 2, new[] { new DbForeignKeyColumn("IdCity", "IdCity") }),
            };
            var db = new DatabaseSnapshot("shop", "dbo", new[] { "dbo" }, objects, "v1", null, null, null, keys,
                new Dictionary<int, IReadOnlyList<string>> { [2] = new[] { "IdCity" } }, indexes: new[] { new DbIndex("dbo", "Cities", "IX_Cities_Name", "nonclustered") })
                .WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>> { [2] = new[] { new DbColumn("IdCity", "int", false), new DbColumn("IdCountry", "int", false) } });

            var rows = ObjectSummary.For(db, "dbo", "Cities").Rows;

            Assert.Equal("IdCity", rows[0][0]);
            Assert.Contains(rows, r => r[0] == "Primary key" && r[1] == "IdCity");
            Assert.Contains(rows, r => r[0] == "Foreign key" && r[1] == "IdCountry → dbo.Countries (IdCountry)" && r[2] == "FK_Cities_Countries");
            Assert.Contains(rows, r => r[0] == "Referenced by" && r[1] == "dbo.Visits (IdCity)");
            Assert.Contains(rows, r => r[0] == "Index" && r[1] == "IX_Cities_Name" && r[2] == "nonclustered");
        }
    }
}
