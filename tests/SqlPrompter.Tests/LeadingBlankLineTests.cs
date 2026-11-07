using SqlPrompter.Core.Navigation;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The stored text of a module often starts with blank lines; they are not carried into the script.</summary>
    public class LeadingBlankLineTests
    {
        [Theory]
        [InlineData("\r\nCREATE VIEW v AS SELECT 1", "ALTER VIEW v AS SELECT 1")]
        [InlineData("\r\n\r\n  \r\nCREATE VIEW v AS SELECT 1", "ALTER VIEW v AS SELECT 1")]
        [InlineData("\n\nCREATE VIEW v AS SELECT 1", "ALTER VIEW v AS SELECT 1")]
        [InlineData("  \r\n  CREATE VIEW v AS SELECT 1", "  ALTER VIEW v AS SELECT 1")]
        [InlineData("-- c\r\nCREATE VIEW v AS SELECT 1", "-- c\r\nALTER VIEW v AS SELECT 1")]
        public void ToAlterDropsLeadingBlankLines(string stored, string expected) =>
            Assert.Equal(expected, DefinitionScript.ToAlter(stored));

        [Fact]
        public void ToCreateOrAlterAndCreateOrAlterModeDoToo()
        {
            Assert.Equal("CREATE OR ALTER VIEW v AS SELECT 1", DefinitionScript.ToCreateOrAlter("\r\nCREATE VIEW v AS SELECT 1"));
            Assert.Equal("CREATE OR ALTER VIEW v AS SELECT 1", DefinitionScript.ToAlter("\r\n\r\nCREATE VIEW v AS SELECT 1", true));
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("   ", "   ")]
        [InlineData("SELECT 1", "SELECT 1")]
        [InlineData("\r\n\r\n", "")]
        public void TheHelperHandlesEmptyAndPlainText(string text, string expected) =>
            Assert.Equal(expected, DefinitionScript.WithoutLeadingBlankLines(text));
    }
}
