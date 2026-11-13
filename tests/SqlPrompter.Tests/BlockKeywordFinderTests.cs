using System;
using System.Linq;
using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class BlockKeywordFinderTests
    {
        // The caret is marked with |; the result is the keywords found, separated by commas, in the order of the text.
        private static string Find(string marked)
        {
            var caret = marked.IndexOf('|');
            var text = marked.Remove(caret, 1);
            return string.Join(",", BlockKeywordFinder.Find(text, caret).Select(s => text.Substring(s.Start, s.Length)));
        }

        [Theory]
        [InlineData("|BEGIN SELECT 1 END")]
        [InlineData("BEGIN| SELECT 1 END")]
        [InlineData("BEGIN SELECT 1 |END")]
        [InlineData("BEGIN SELECT 1 EN|D")]
        [InlineData("BEGIN SELECT 1 END|")]
        public void BeginAndEndFindEachOther(string marked) => Assert.Equal("BEGIN,END", Find(marked));

        [Fact]
        public void NestedBlocksAreMatchedByNesting()
        {
            var text = "BEGIN\n  BEGIN\n    SELECT 1\n  END\nEND";
            var caret = text.IndexOf("BEGIN\n  BEGIN", System.StringComparison.Ordinal);
            var outer = BlockKeywordFinder.Find(text, caret);
            Assert.Equal(new[] { 0, text.LastIndexOf("END", System.StringComparison.Ordinal) }, outer.Select(s => s.Start).ToArray());
            var inner = BlockKeywordFinder.Find(text, text.IndexOf("  BEGIN", System.StringComparison.Ordinal) + 3);
            Assert.Equal(new[] { text.IndexOf("  BEGIN", System.StringComparison.Ordinal) + 2, text.IndexOf("END", System.StringComparison.Ordinal) }, inner.Select(s => s.Start).ToArray());
        }

        [Fact]
        public void TryAndCatchFormOneGroup()
        {
            const string Script = "BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 2 END CATCH";
            Assert.Equal("BEGIN,TRY,END,TRY,BEGIN,CATCH,END,CATCH", Find(Script.Insert(2, "|")));
            Assert.Equal("BEGIN,TRY,END,TRY,BEGIN,CATCH,END,CATCH", Find(Script.Insert(Script.IndexOf("CATCH", System.StringComparison.Ordinal) + 2, "|")));
            Assert.Equal("BEGIN,TRY,END,TRY,BEGIN,CATCH,END,CATCH", Find(Script.Insert(Script.LastIndexOf("END", System.StringComparison.Ordinal), "|")));
        }

        [Fact]
        public void TryAndCatchInsideABeginBlockDoNotConfuseTheBlock() =>
            Assert.Equal("BEGIN,END", Find("|BEGIN BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 2 END CATCH END"));

        [Fact]
        public void CaseEndIsToldApartFromBlockEnd() =>
            Assert.Equal("CASE,WHEN,THEN,ELSE,END", Find("BEGIN SELECT |CASE WHEN a = 1 THEN 2 ELSE 3 END END"));

        [Fact]
        public void BlockEndIsNotTheCaseEnd() =>
            Assert.Equal("BEGIN,END", Find("|BEGIN SELECT CASE WHEN a = 1 THEN 2 ELSE 3 END END"));

        [Fact]
        public void ElseOfACaseBelongsToTheCase() =>
            Assert.Equal("CASE,WHEN,THEN,ELSE,END", Find("SELECT CASE WHEN a = 1 THEN 2 EL|SE 3 END"));

        [Fact]
        public void TransactionFindsCommit() =>
            Assert.Equal("BEGIN,TRAN,COMMIT,TRANSACTION", Find("|BEGIN TRAN INSERT INTO t VALUES (1) COMMIT TRANSACTION"));

        [Fact]
        public void TransactionFindsRollback() =>
            Assert.Equal("BEGIN,TRANSACTION,ROLLBACK", Find("BEGIN TRANSACTION x; SELECT 1; ROLL|BACK"));

        [Fact]
        public void NestedTransactionsAreMatchedByNesting()
        {
            const string Script = "BEGIN TRAN a BEGIN TRAN b COMMIT TRAN COMMIT TRAN";
            Assert.Equal("BEGIN,TRAN,COMMIT,TRAN", Find(Script.Insert(Script.IndexOf("BEGIN TRAN b", System.StringComparison.Ordinal), "|")));
            Assert.Equal(new[] { 0, 6, 38, 45 }, BlockKeywordFinder.Find(Script, 0).Select(s => s.Start).ToArray());
        }

        [Fact]
        public void IfFindsItsElse() =>
            Assert.Equal("IF,ELSE", Find("|IF a = 1 SELECT 1 ELSE SELECT 2"));

        [Fact]
        public void ElseFindsTheNearestIf() =>
            Assert.Equal("IF,ELSE", Find("IF a = 1 BEGIN IF b = 1 SELECT 1 END ELS|E SELECT 2"));

        [Fact]
        public void IfWithoutElseHasNoGroup() =>
            Assert.Equal(string.Empty, Find("|IF a = 1 SELECT 1"));

        [Fact]
        public void WhileFindsTheBlockOfItsBody() =>
            Assert.Equal("WHILE,BEGIN,END", Find("|WHILE EXISTS (SELECT 1) BEGIN SELECT 1 END"));

        [Fact]
        public void EndFindsTheWhile() =>
            Assert.Equal("WHILE,BEGIN,END", Find("WHILE a < 3 BEGIN SELECT 1 EN|D"));

        [Fact]
        public void WhileWithASingleStatementHasNoGroup() =>
            Assert.Equal(string.Empty, Find("|WHILE a < 3 SET @a = @a + 1"));

        [Fact]
        public void KeywordsInStringsCommentsAndIdentifiersAreIgnored() =>
            Assert.Equal("BEGIN,END", Find("|BEGIN SELECT 'END', [END] /* END */ -- END\n END"));

        [Fact]
        public void CaretInAStringFindsNothing() =>
            Assert.Equal(string.Empty, Find("BEGIN SELECT 'EN|D' END"));

        [Fact]
        public void GoEndsTheSearch()
        {
            Assert.Equal(string.Empty, Find("|BEGIN\nSELECT 1\nGO\nEND"));
            Assert.Equal(string.Empty, Find("BEGIN\nSELECT 1\nGO\n|END"));
            Assert.Equal("BEGIN,END", Find("BEGIN\nGO\n|BEGIN SELECT 1 END"));
        }

        [Fact]
        public void UnbalancedCodeFindsNothingAndDoesNotThrow()
        {
            Assert.Equal(string.Empty, Find("|BEGIN SELECT 1"));
            Assert.Equal(string.Empty, Find("SELECT 1 |END"));
            Assert.Equal(string.Empty, Find("|BEGIN TRY SELECT 1"));
            Assert.Equal(string.Empty, Find("|BEGIN TRAN SELECT 1"));
            Assert.Equal(string.Empty, Find("|CASE WHEN a = 1 THEN 2"));
            Assert.Equal(string.Empty, Find("|COMMIT"));
        }

        [Fact]
        public void CaretOnOtherWordsFindsNothing() =>
            Assert.Equal(string.Empty, Find("BEGIN SEL|ECT 1 END"));

        [Fact]
        public void EmptyTextFindsNothing() => Assert.Empty(BlockKeywordFinder.Find(string.Empty, 0));
    }
}
