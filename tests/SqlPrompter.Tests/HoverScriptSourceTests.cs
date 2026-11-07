using SqlPrompter.Core.Navigation;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The tooltip over CTEs, subqueries and temporary tables, and the object it can take a description for (#220).</summary>
    public class HoverScriptSourceTests
    {
        private static HoverText Hover(string marked)
        {
            var caret = marked.IndexOf('|');
            return HoverInfo.At(marked.Remove(caret, 1), caret, SampleDatabase());
        }

        [Fact]
        public void ACteUsedAsASourceListsItsColumns()
        {
            var text = Hover("WITH c1 AS (SELECT Id, Name FROM dbo.Users) SELECT * FROM c|1").Text;

            Assert.StartsWith("Common table expression c1", text);
            Assert.Contains("columns: Id, Name", text);
        }

        [Fact]
        public void AnAliasOfACteNamesTheCte()
        {
            var text = Hover("WITH c1 AS (SELECT Id FROM dbo.Users) SELECT * FROM c1 AS |x").Text;

            Assert.Contains("Alias x for common table expression c1", text);
        }

        [Fact]
        public void AnAliasOfASubqueryIsDescribed()
        {
            var text = Hover("SELECT * FROM (SELECT Id, Name FROM dbo.Users) AS |d").Text;

            Assert.Contains("Alias d for a subquery", text);
            Assert.Contains("columns: Id, Name", text);
        }

        [Fact]
        public void AColumnOfASubqueryIsFoundThroughItsAlias()
        {
            var text = Hover("SELECT d.Na|me FROM (SELECT Id, Name FROM dbo.Users) AS d").Text;

            Assert.StartsWith("Column d.Name", text);
            Assert.Contains("of a subquery", text);
        }

        [Fact]
        public void ATemporaryTableOfTheScriptIsRecognised()
        {
            var text = Hover("CREATE TABLE #t (a int, b nvarchar(10)); SELECT * FROM #t AS |t2").Text;

            Assert.Contains("Alias t2 for temporary table #t", text);
            Assert.Contains("columns: a, b", text);
        }

        [Fact]
        public void TheDescriptionIsAskedForTheObjectOfATable()
        {
            var table = Hover("SELECT * FROM dbo.Us|ers");

            Assert.Equal("dbo", table.DescriptionSchema);
            Assert.Equal("Users", table.DescriptionObject);
            Assert.Null(table.DescriptionColumn);
        }

        [Fact]
        public void TheDescriptionIsAskedForTheColumnAndItsTable()
        {
            var column = Hover("SELECT u.Na|me FROM dbo.Users u");

            Assert.Equal("Users", column.DescriptionObject);
            Assert.Equal("Name", column.DescriptionColumn);
        }

        [Fact]
        public void AnAliasAsksForTheDescriptionOfItsTable() =>
            Assert.Equal("Users", Hover("SELECT * FROM dbo.Users |u").DescriptionObject);

        [Fact]
        public void ScriptSourcesAndVariablesHaveNoDescriptionObject()
        {
            Assert.Null(Hover("WITH c1 AS (SELECT Id FROM dbo.Users) SELECT * FROM c|1").DescriptionObject);
            Assert.Null(Hover("DECLARE @n int; SELECT @|n").DescriptionObject);
        }
    }
}
