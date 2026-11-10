using System.Linq;
using SqlPrompter.Core.Editing;
using SqlPrompter.Core.Lexing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class UnformatterTests
    {
        [Fact]
        public void MultiLineSelectWithJoinsAndWhereBecomesOneLine()
        {
            var text = "SELECT a,\r\n       b\r\nFROM dbo.T t\r\n\r\n    INNER JOIN dbo.U u\r\n        ON u.Id = t.Id\r\nWHERE t.x = 1";
            Assert.Equal("SELECT a, b FROM dbo.T t INNER JOIN dbo.U u ON u.Id = t.Id WHERE t.x = 1", Unformatter.ToOneLine(text));
        }

        [Fact]
        public void LeadingAndTrailingWhiteSpaceIsTrimmed() =>
            Assert.Equal("SELECT 1", Unformatter.ToOneLine("\r\n  SELECT   1  \r\n"));

        [Fact]
        public void NoSpaceIsAddedWhereThereWasNone() =>
            Assert.Equal("SELECT f(a,b),c FROM T", Unformatter.ToOneLine("SELECT f(a,b),c\nFROM T"));

        [Fact]
        public void StringsAndQuotedIdentifiersAreKept()
        {
            var text = "SELECT 'a\r\n  b  c', [x  y\r\nz], \"q  r\"\r\nFROM T";
            Assert.Equal("SELECT 'a\r\n  b  c', [x  y\r\nz], \"q  r\" FROM T", Unformatter.ToOneLine(text));
        }

        [Fact]
        public void LineCommentBecomesBlockComment() =>
            Assert.Equal("SELECT a /* the column */ FROM T", Unformatter.ToOneLine("SELECT a -- the column\r\nFROM T"));

        [Fact]
        public void LineCommentWithCommentMarkersIsMadeSafe() =>
            Assert.Equal("SELECT 1 /* a * / b / * c */ FROM T", Unformatter.ToOneLine("SELECT 1 -- a */ b /* c\r\nFROM T"));

        [Fact]
        public void BlockCommentIsKept() =>
            Assert.Equal("SELECT /* one\r\n two */ 1", Unformatter.ToOneLine("SELECT\r\n/* one\r\n two */\r\n1"));

        [Fact]
        public void GoStaysAloneAndEveryBatchIsOneLine() =>
            Assert.Equal("SELECT 1 FROM A\r\nGO\r\nSELECT 2 FROM B", Unformatter.ToOneLine("SELECT 1\r\nFROM A\r\n\r\nGO\r\nSELECT 2\r\nFROM B"));

        [Fact]
        public void GoInsideAStringIsNotABatchSeparator()
        {
            var text = "SELECT 'a\r\nGO\r\nb'";
            Assert.Equal(text, Unformatter.ToOneLine(text));
        }

        [Fact]
        public void EmptyAndOneLineSelectionsAreUnchanged()
        {
            Assert.Equal(string.Empty, Unformatter.ToOneLine(string.Empty));
            Assert.Equal("SELECT 1 FROM T", Unformatter.ToOneLine("SELECT 1 FROM T"));
        }

        [Fact]
        public void TheTokensStayTheSame()
        {
            var text = "SELECT a, -- first\r\n  b\r\nFROM T\r\nWHERE c = 'x  y' /* k\r\n */";
            var before = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).Select(t => t.Text).ToList();
            var after = Lexer.Tokenize(Unformatter.ToOneLine(text)).Where(t => t.Kind != TokenKind.Comment).Select(t => t.Text).ToList();
            Assert.Equal(before, after);
        }
    }
}
