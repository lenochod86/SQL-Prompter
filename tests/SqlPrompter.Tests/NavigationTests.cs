using SqlPrompter.Core.Navigation;
using Xunit;

namespace SqlPrompter.Tests
{
    public class NavigationTests
    {
        private static ObjectReference Find(string sqlWithCaret)
        {
            var caret = sqlWithCaret.IndexOf('|');
            return ObjectReferenceFinder.Find(sqlWithCaret.Remove(caret, 1), caret);
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Ord|ers", "dbo.Orders")]
        [InlineData("SELECT * FROM dbo.Orders|", "dbo.Orders")]
        [InlineData("SELECT * FROM |Orders", "Orders")]
        [InlineData("SELECT * FROM [dbo].[My Orders|]", "dbo.My Orders")]
        [InlineData("SELECT * FROM Sales.dbo.Ord|ers", "Sales.dbo.Orders")]
        [InlineData("EXEC dbo.usp_Do|It", "dbo.usp_DoIt")]
        public void NameUnderCaretIsFound(string sql, string expected)
        {
            Assert.Equal(expected, Find(sql).ToString());
        }

        [Fact]
        public void AliasResolvesToItsTable()
        {
            Assert.Equal("dbo.Orders", Find("SELECT o| .Id FROM dbo.Orders o").ToString());
            Assert.Equal("dbo.Orders", Find("SELECT o.I|d FROM dbo.Orders o").ToString());
        }

        [Fact]
        public void TableNameWithoutAliasQualifiesItsColumns()
        {
            Assert.Equal("dbo.Orders", Find("SELECT Orders.I|d FROM dbo.Orders").ToString());
        }

        [Fact]
        public void CaretOnTheSchemaPartDoesNotReachTheTable()
        {
            Assert.Equal("dbo", Find("SELECT * FROM d|bo.Orders").Name);
        }

        [Fact]
        public void TemporaryTablesAndDerivedTablesAreNotFollowed()
        {
            Assert.Null(Find("SELECT * FROM #te|mp"));
            Assert.Null(Find("SELECT d.I|d FROM (SELECT 1 AS Id) d"));
        }

        [Fact]
        public void LinkedServerNamesAreNotFollowed()
        {
            Assert.Null(Find("SELECT * FROM srv.db.dbo.Ord|ers"));
        }

        [Fact]
        public void NothingUnderTheCaret()
        {
            Assert.Null(Find("SELECT 1 +| 2"));
        }

        [Fact]
        public void CreateOrAlterIsAddedOnce()
        {
            Assert.Equal("CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1", DefinitionScript.ToCreateOrAlter("CREATE PROCEDURE dbo.P AS SELECT 1"));
            Assert.Equal("CREATE OR ALTER VIEW v AS SELECT 1", DefinitionScript.ToCreateOrAlter("CREATE OR ALTER VIEW v AS SELECT 1"));
            Assert.Equal("-- header CREATE\r\ncreate OR ALTER FUNCTION f()", DefinitionScript.ToCreateOrAlter("-- header CREATE\r\ncreate FUNCTION f()"));
            Assert.Equal("ALTER VIEW v AS SELECT 1", DefinitionScript.ToCreateOrAlter("ALTER VIEW v AS SELECT 1"));
            Assert.Equal(string.Empty, DefinitionScript.ToCreateOrAlter(null));
        }

        [Fact]
        public void TableScriptListsColumnsAndPrimaryKey()
        {
            var script = DefinitionScript.ForTable(
                "dbo",
                "Orders",
                new[]
                {
                    new ScriptTableColumn("IdOrders", "int", false, "IDENTITY(1,1)"),
                    new ScriptTableColumn("Note", "nvarchar(50)", true, null),
                },
                "PK_Orders",
                new[] { "IdOrders" });

            Assert.Equal(
                "CREATE TABLE [dbo].[Orders]\r\n(\r\n    [IdOrders] int IDENTITY(1,1) NOT NULL,\r\n    [Note] nvarchar(50) NULL,\r\n    CONSTRAINT [PK_Orders] PRIMARY KEY ([IdOrders])\r\n);\r\n",
                script);
        }
    }
}
