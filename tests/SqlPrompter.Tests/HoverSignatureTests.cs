using SqlPrompter.Core.Model;
using SqlPrompter.Core.Navigation;
using SqlPrompter.Metadata;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>SSMS shows the signature of a procedure or function itself, so the tooltip must not repeat it (#422).</summary>
    public class HoverSignatureTests
    {
        private static HoverText Hover(string sql, int caret)
        {
            var snapshot = new DatabaseSnapshot("test", "dbo", new[] { "dbo" },
                new[] { new DbObject(1, "dbo", "Users", DbObjectKind.Table, false), new DbObject(2, "dbo", "fnTax", DbObjectKind.ScalarFunction, false) },
                "v1", procedures: new[] { new DbProcedure(9, "dbo", "AddUser", new DbParameter[0]) });
            return HoverInfo.At(sql, caret, snapshot);
        }

        [Fact]
        public void AProcedureSignatureIsLeftToSsms() => Assert.Equal(2, Hover("EXEC dbo.AddUser", 14).LinesInSsms);

        [Fact]
        public void AFunctionSignatureIsLeftToSsms() => Assert.Equal(2, Hover("SELECT dbo.fnTax(1)", 14).LinesInSsms);

        [Fact]
        public void ATableNameIsLeftToSsms() => Assert.Equal(1, Hover("SELECT * FROM dbo.Users", 20).LinesInSsms);
    }
}
