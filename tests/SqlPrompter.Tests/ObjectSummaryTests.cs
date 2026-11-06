using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;

namespace SqlPrompter.Tests
{
    public class ObjectSummaryTests
    {
        private static DatabaseSnapshot Database()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Empty", DbObjectKind.Table, false),
                new DbObject(3, "dbo", "f_Name", DbObjectKind.ScalarFunction, false, null, 2),
                new DbObject(4, "dbo", "f_None", DbObjectKind.ScalarFunction, false),
                new DbObject(5, "dbo", "NotLoaded", DbObjectKind.Table, false),
            };
            var columns = new Dictionary<int, IReadOnlyList<DbColumn>>
            {
                [1] = new[] { new DbColumn("Id", "int", false), new DbColumn("Name", "nvarchar(200)", true) },
                [2] = new DbColumn[0],
            };
            var functionParameters = new Dictionary<int, IReadOnlyList<DbParameter>>
            {
                [3] = new[] { new DbParameter("@Schema", "nvarchar(250)", false, false), new DbParameter("@Flag", "bit", true, true, "0") },
            };
            var procedures = new[] { new DbProcedure(10, "dbo", "p_Save", new[] { new DbParameter("@Id", "int", false, false) }), new DbProcedure(11, "dbo", "p_NoArgs", new DbParameter[0]) };
            return new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, objects, "v1", columns, null, null, null, null, procedures, functionParameters);
        }

        [Fact]
        public void ATableShowsNameTypeAndNullability()
        {
            var summary = ObjectSummary.For(Database(), "dbo", "Users");

            Assert.Equal(new[] { "Column Name", "Data Type", "Nullability" }, summary.Headers.ToArray());
            Assert.Equal(new[] { "Id", "int", "not null" }, summary.Rows[0]);
            Assert.Equal(new[] { "Name", "nvarchar(200)", "null" }, summary.Rows[1]);
        }

        [Fact]
        public void AFunctionShowsItsParametersWithOutputAndDefault()
        {
            var summary = ObjectSummary.For(Database(), "dbo", "f_Name");

            Assert.Equal(new[] { "Parameter Name", "Data Type" }, summary.Headers.ToArray());
            Assert.Equal(new[] { "@Schema", "nvarchar(250)" }, summary.Rows[0]);
            Assert.Equal(new[] { "@Flag", "bit OUTPUT = 0" }, summary.Rows[1]);
        }

        [Fact]
        public void AProcedureShowsItsParameters() =>
            Assert.Equal(new[] { "@Id", "int" }, ObjectSummary.For(Database(), "dbo", "p_Save").Rows.Single());

        [Theory]
        [InlineData("dbo", "Empty")]
        [InlineData("dbo", "f_None")]
        [InlineData("dbo", "p_NoArgs")]
        [InlineData("dbo", "NotLoaded")]
        [InlineData("dbo", "Missing")]
        [InlineData("dbo", "")]
        public void WithoutColumnsOrParametersThereIsNoSummary(string schema, string name) =>
            Assert.Null(ObjectSummary.For(Database(), schema, name));

        [Fact]
        public void TheSummaryCopiesAsTabSeparatedText() =>
            Assert.Equal("Column Name\tData Type\tNullability\r\nId\tint\tnot null\r\nName\tnvarchar(200)\tnull", ObjectSummary.For(Database(), "dbo", "Users").ToText());
    }
}
