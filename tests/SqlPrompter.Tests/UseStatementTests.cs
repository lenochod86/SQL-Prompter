using SqlPrompter.Core.Analysis;
using Xunit;

namespace SqlPrompter.Tests
{
    public class UseStatementTests
    {
        [Theory]
        [InlineData("USE Sales", "Sales")]
        [InlineData("  use sales;", "sales")]
        [InlineData("USE [Sales Archive]", "Sales Archive")]
        [InlineData("USE [Odd]]Name]  -- comment", "Odd]Name")]
        [InlineData("USE \"Sales\";", "Sales")]
        [InlineData("USE Sales /* x */", "Sales")]
        public void RecognizesUse(string line, string expected)
        {
            Assert.True(UseStatement.TryParse(line, out var database));
            Assert.Equal(expected, database);
        }

        [Theory]
        [InlineData("SELECT * FROM Users")]
        [InlineData("-- USE Sales")]
        [InlineData("USE")]
        [InlineData("USERS u")]
        [InlineData("USE Sales; SELECT 1")]
        [InlineData("")]
        public void IgnoresEverythingElse(string line)
        {
            Assert.False(UseStatement.TryParse(line, out _));
        }
    }
}
