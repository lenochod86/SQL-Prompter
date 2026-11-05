using System.Linq;
using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class EditorHelperTests
    {
        // The caret is marked with |; the decision is made for the text without the mark.
        private static AutoCloseAction Decide(string marked, char typed)
        {
            var caret = marked.IndexOf('|');
            return AutoClose.Decide(marked.Remove(caret, 1), caret, typed);
        }

        private static string Matches(string marked)
        {
            var caret = marked.IndexOf('|');
            var text = marked.Remove(caret, 1);
            return string.Join(",", MatchFinder.Find(text, caret).Select(s => text.Substring(s.Start, s.Length)));
        }

        [Fact]
        public void CommasGoToEveryLineButTheLast() =>
            Assert.Equal("a,\r\nb,\r\nc", LineTools.AddCommas("a\r\nb\r\nc"));

        [Fact]
        public void CommasKeepIndentationBlankLinesAndExistingCommas() =>
            Assert.Equal("  a,\n\n  b,\n  c\n", LineTools.AddCommas("  a\n\n  b,\n  c\n"));

        [Fact]
        public void CommasOnASingleLineChangeNothing() => Assert.Equal("a", LineTools.AddCommas("a"));

        [Fact]
        public void QuotesAndCommasMakeAStringListAndDoubleQuotes() =>
            Assert.Equal("'a',\r\n'it''s',\r\n'c'", LineTools.AddQuotesAndCommas("a\r\nit's\r\nc"));

        [Fact]
        public void QuotesDoNotSwallowAnExistingCommaAndKeepIndentation() =>
            Assert.Equal("\t'a',\n\t'b'", LineTools.AddQuotesAndCommas("\ta,\n\tb"));

        [Fact]
        public void EmptyTextStaysEmpty()
        {
            Assert.Equal(string.Empty, LineTools.AddCommas(string.Empty));
            Assert.Equal(string.Empty, LineTools.AddQuotesAndCommas(string.Empty));
        }

        [Theory]
        [InlineData("SELECT |", '\'', AutoCloseAction.InsertPair)]
        [InlineData("SELECT * FROM t WHERE a = |", '\'', AutoCloseAction.InsertPair)]
        [InlineData("SELECT N|", '\'', AutoCloseAction.InsertPair)]
        [InlineData("SELECT x|", '\'', AutoCloseAction.None)]
        [InlineData("SELECT |abc", '\'', AutoCloseAction.None)]
        [InlineData("SELECT 'abc|", '\'', AutoCloseAction.None)]
        [InlineData("SELECT 'a|bc'", '\'', AutoCloseAction.None)]
        [InlineData("SELECT 'abc|'", '\'', AutoCloseAction.Overtype)]
        [InlineData("SELECT ''|", '\'', AutoCloseAction.None)]
        [InlineData("SELECT '|'", '\'', AutoCloseAction.Overtype)]
        [InlineData("-- it|", '\'', AutoCloseAction.None)]
        [InlineData("/* it| */", '\'', AutoCloseAction.None)]
        [InlineData("SELECT [a|]", '\'', AutoCloseAction.None)]
        public void QuotesAreClosedOnlyWhereAStringCanStart(string text, char typed, AutoCloseAction expected) =>
            Assert.Equal(expected, Decide(text, typed));

        [Theory]
        [InlineData("SELECT * FROM |", '[', AutoCloseAction.InsertPair)]
        [InlineData("SELECT * FROM dbo.|", '[', AutoCloseAction.InsertPair)]
        [InlineData("SELECT * FROM |abc", '[', AutoCloseAction.None)]
        [InlineData("SELECT 'a|'", '[', AutoCloseAction.None)]
        [InlineData("-- a|", '[', AutoCloseAction.None)]
        [InlineData("SELECT [a|", '[', AutoCloseAction.None)]
        [InlineData("SELECT [Orders|]", ']', AutoCloseAction.Overtype)]
        [InlineData("SELECT [Orders|", ']', AutoCloseAction.None)]
        [InlineData("SELECT a|]", ']', AutoCloseAction.None)]
        [InlineData("SELECT 'a|]'", ']', AutoCloseAction.None)]
        public void BracketsAreClosedOnlyWhereAnameCanStart(string text, char typed, AutoCloseAction expected) =>
            Assert.Equal(expected, Decide(text, typed));

        [Fact]
        public void OtherCharactersAreNeverClosed() => Assert.Equal(AutoCloseAction.None, Decide("SELECT |", '('));

        [Fact]
        public void AllAppearancesOfANameAreFoundIgnoringCaseAndBrackets() =>
            Assert.Equal("Orders,[orders],Orders", Matches("SELECT * FROM dbo.Or|ders o JOIN [orders] x ON 1 = 1 WHERE Orders.a = 1"));

        [Fact]
        public void AnAliasIsMatchedEverywhere() =>
            Assert.Equal("o,o,o", Matches("SELECT o.Id FROM Orders o WHERE |o.Id = 1"));

        [Fact]
        public void VariablesAreMatched() => Assert.Equal("@id,@id", Matches("DECLARE @id int; SELECT |@id"));

        [Fact]
        public void KeywordsStringsAndCommentsAreNotMatched()
        {
            Assert.Equal(string.Empty, Matches("SEL|ECT 1"));
            Assert.Equal(string.Empty, Matches("SELECT 'Or|ders'"));
            Assert.Equal(string.Empty, Matches("-- Or|ders"));
            Assert.Equal("Orders", Matches("SELECT 'Orders' -- Orders\nFROM Or|ders"));
        }

        [Fact]
        public void TheCaretRightAfterTheNameCounts() => Assert.Equal("a,a", Matches("SELECT a| FROM a"));
    }
}
