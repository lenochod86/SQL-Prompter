using SqlPrompter.Core.Navigation;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The path of an object in Object Explorer (#222) and the objects F12 (#41) resolves, functions included.</summary>
    public class ObjectExplorerPathTests
    {
        private static LocatedObject Object(string type, string schema = "dbo", string name = "Cities") =>
            new LocatedObject { Server = "SRV", Database = "Shop", Schema = schema, Name = name, TypeCode = type };

        [Theory]
        [InlineData("U", "Server[@Name='SRV']/Database[@Name='Shop']/Table[@Name='Cities' and @Schema='dbo']")]
        [InlineData("V", "Server[@Name='SRV']/Database[@Name='Shop']/View[@Name='Cities' and @Schema='dbo']")]
        [InlineData("P ", "Server[@Name='SRV']/Database[@Name='Shop']/StoredProcedure[@Name='Cities' and @Schema='dbo']")]
        [InlineData("FN", "Server[@Name='SRV']/Database[@Name='Shop']/UserDefinedFunction[@Name='Cities' and @Schema='dbo']")]
        [InlineData("IF", "Server[@Name='SRV']/Database[@Name='Shop']/UserDefinedFunction[@Name='Cities' and @Schema='dbo']")]
        [InlineData("TF", "Server[@Name='SRV']/Database[@Name='Shop']/UserDefinedFunction[@Name='Cities' and @Schema='dbo']")]
        [InlineData("SN", "Server[@Name='SRV']/Database[@Name='Shop']/Synonym[@Name='Cities' and @Schema='dbo']")]
        [InlineData("SO", "Server[@Name='SRV']/Database[@Name='Shop']/Sequence[@Name='Cities' and @Schema='dbo']")]
        public void EveryKindOfObjectHasItsNode(string type, string expected) => Assert.Equal(expected, ObjectExplorerPath.Urn(Object(type)));

        [Fact]
        public void ATriggerIsBelowItsTableOrView()
        {
            var onTable = Object("TR", "dbo", "TR_Cities");
            onTable.ParentSchema = "dbo";
            onTable.ParentName = "Cities";
            onTable.ParentTypeCode = "U";
            Assert.Equal("Server[@Name='SRV']/Database[@Name='Shop']/Table[@Name='Cities' and @Schema='dbo']/Trigger[@Name='TR_Cities']", ObjectExplorerPath.Urn(onTable));

            onTable.ParentTypeCode = "V";
            Assert.Contains("/View[@Name='Cities' and @Schema='dbo']/Trigger[", ObjectExplorerPath.Urn(onTable));

            onTable.ParentName = null;
            Assert.Null(ObjectExplorerPath.Urn(onTable));
        }

        [Fact]
        public void QuotesInNamesAreDoubledAndUnknownKindsHaveNoNode()
        {
            Assert.Contains("@Name='O''Brien'", ObjectExplorerPath.Urn(Object("U", "dbo", "O'Brien")));
            Assert.Null(ObjectExplorerPath.Urn(Object("TT")));
            Assert.Null(ObjectExplorerPath.Urn(new LocatedObject { Database = "Shop", Name = "X", TypeCode = "U" }));
            Assert.Null(ObjectExplorerPath.Urn(null));
        }

        [Fact]
        public void TheQualifiedNameIsBracketed() =>
            Assert.Equal("[Shop].[dbo].[Ci]]ties]", ObjectExplorerPath.Qualified("Shop", "dbo", "Ci]ties"));

        [Theory]
        [InlineData("SELECT dbo.ufn_Double|(1)", null, "dbo", "ufn_Double")]
        [InlineData("SELECT * FROM dbo.tvf_Cities|(1) t", null, "dbo", "tvf_Cities")]
        [InlineData("SELECT * FROM hr.tvf_Names|() n", null, "hr", "tvf_Names")]
        [InlineData("SELECT * FROM Shop.dbo.tvf_Cities|(1)", "Shop", "dbo", "tvf_Cities")]
        [InlineData("SELECT ufn_Double|(1)", null, null, "ufn_Double")]
        [InlineData("SELECT dbo.ufn_Dou|ble(1)", null, "dbo", "ufn_Double")]
        public void TheNameOfAFunctionIsResolvedLikeThatOfATable(string sql, string database, string schema, string name)
        {
            var caret = sql.IndexOf('|');
            var reference = ObjectReferenceFinder.Find(sql.Remove(caret, 1), caret);

            Assert.NotNull(reference);
            Assert.Equal(database, reference.Database);
            Assert.Equal(schema, reference.Schema);
            Assert.Equal(name, reference.Name);
        }
    }
}
