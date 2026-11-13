using SqlPrompter.Core.Navigation;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Jump between BEGIN and END (#224); the caret is marked with |.</summary>
    public class BlockMatcherTests
    {
        private static BlockMatch Match(string marked)
        {
            var caret = marked.IndexOf('|');
            return BlockMatcher.Find(marked.Remove(caret, 1), caret);
        }

        [Fact]
        public void BeginTryJumpsToEndTry()
        {
            var sql = "BEGIN TRY\nSELECT 1;\nEND TRY\nBEGIN CATCH\nSELECT 2;\nEND CATCH";
            var match = Match(sql.Insert(2, "|"));

            Assert.True(match.TargetIsEnd);
            Assert.Equal("END TRY", match.Name);
            Assert.Equal(sql.IndexOf("END TRY"), match.Target);
            Assert.Equal(3, match.Line);
        }

        [Fact]
        public void EndCatchJumpsBackToBegin()
        {
            var match = Match("BEGIN TRY\nSELECT 1;\nEND TRY\nBEGIN CATCH\nSELECT 2;\nEN|D CATCH");

            Assert.False(match.TargetIsEnd);
            Assert.Equal("BEGIN CATCH", match.Name);
        }

        [Fact]
        public void TheCaretOnTryOrAtTheEndOfTheWordCounts()
        {
            Assert.Equal("END TRY", Match("BEGIN TR|Y\nEND TRY").Name);
            Assert.Equal("END", Match("BEGIN|\nEND").Name);
        }

        [Fact]
        public void NestedBlocksAndCaseAreMatchedCorrectly()
        {
            var sql = "BEGIN\nSELECT CASE WHEN 1 = 1 THEN 1 END;\nBEGIN\nSELECT 1\nEND\nEND";

            Assert.Equal(sql.LastIndexOf("END"), Match(sql.Insert(1, "|")).Target);
            Assert.Equal(sql.IndexOf("BEGIN", 6), Match(sql.Insert(sql.IndexOf("END\nEND"), "|")).Target);
        }

        [Fact]
        public void CaseEndsBeginTransactionAndUnfinishedCodeHaveNoPartner()
        {
            Assert.Null(Match("SELECT CASE WHEN 1 = 1 THEN 1 EN|D"));
            Assert.Null(Match("BEGIN| TRAN\nCOMMIT"));
            Assert.Null(Match("BEGIN| SELECT 1"));
            Assert.Null(Match("SELECT |1"));
        }
    }
}
