using System.Linq;
using SqlPrompter.Core.Navigation;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The text search over the code of a database (#212).</summary>
    public class CodeSearchTests
    {
        private const string Script = "CREATE PROCEDURE dbo.p\r\nAS\r\n    SELECT * FROM dbo.Users u\r\n    JOIN dbo.UsersArchive a ON a.Id = u.Id;\r\n    -- users again\r\n";

        private static int[] Lines(string text, CodeSearchOptions options = null) =>
            CodeSearch.Find(Script, CodeSearch.Compile(text, options)).Select(m => m.Line).ToArray();

        [Fact]
        public void LinesAreNumberedFromOneAndEachIsReportedOnce() =>
            Assert.Equal(new[] { 3, 4, 5 }, Lines("users"));

        [Fact]
        public void CaseSensitivityIsAnOption() =>
            Assert.Equal(new[] { 5 }, Lines("users", new CodeSearchOptions { CaseSensitive = true }));

        [Fact]
        public void WholeWordSkipsLongerNames() =>
            Assert.Equal(new[] { 3, 5 }, Lines("Users", new CodeSearchOptions { WholeWord = true }));

        [Fact]
        public void ARegularExpressionIsSupported() =>
            Assert.Equal(new[] { 3, 4 }, Lines(@"dbo\.Users\w*\s", new CodeSearchOptions { UseRegex = true }));

        [Fact]
        public void TheMatchTextIsTrimmed() =>
            Assert.Equal("SELECT * FROM dbo.Users u", CodeSearch.Find(Script, CodeSearch.Compile("select", null)).Single().Text);

        [Fact]
        public void AnInvalidExpressionOrEmptyTextFindsNothing()
        {
            Assert.Null(CodeSearch.Compile("(", new CodeSearchOptions { UseRegex = true }));
            Assert.Null(CodeSearch.Compile(string.Empty, null));
            Assert.Empty(CodeSearch.Find(Script, null));
            Assert.Empty(CodeSearch.Find(null, CodeSearch.Compile("a", null)));
        }

        [Fact]
        public void ATextWithSpecialCharactersIsTakenLiterally() =>
            Assert.Equal(new[] { 4 }, Lines("a.Id = u.Id"));
    }
}
