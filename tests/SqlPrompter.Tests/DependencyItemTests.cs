using SqlPrompter.Metadata;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The names and kinds shown by the dependency list (#222).</summary>
    public class DependencyItemTests
    {
        [Theory]
        [InlineData("U", "Table")]
        [InlineData("V", "View")]
        [InlineData("P ", "Procedure")]
        [InlineData("IF", "Table function")]
        [InlineData("FN", "Scalar function")]
        [InlineData("TR", "Trigger")]
        [InlineData("SN", "Synonym")]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("XX", "")]
        public void TheTypeCodeBecomesAWord(string code, string kind) =>
            Assert.Equal(kind, new DependencyItem(null, "dbo", "t", code).KindText);

        [Fact]
        public void TheFullNameHasTheDatabaseOnlyWhenItIsAnotherOne()
        {
            Assert.Equal("dbo.Users", new DependencyItem(null, "dbo", "Users", "U").FullName);
            Assert.Equal("Other.dbo.Users", new DependencyItem("Other", "dbo", "Users", "").FullName);
            Assert.Equal("Users", new DependencyItem(null, null, "Users", "").FullName);
        }
    }
}
