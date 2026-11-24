using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>A statement with a comment inside is laid out and every comment stays next to the token it followed (#528).</summary>
    public class FormatterCommentTests
    {
        private static FormatResult Run(string sql, FormatStyle style = null) => SqlFormatter.Format(sql, style ?? new FormatStyle(), "\n");

        private static string Format(string sql, FormatStyle style = null) => Run(sql, style).Text;

        [Fact]
        public void ATrailingLineCommentAfterASelectItemStaysAtTheEndOfItsLine()
        {
            var result = Run("select  1   as  one,\n   2 as two   -- keep me\nfrom dual");

            Assert.Equal("SELECT\t1 AS one\n\t  , 2 AS two -- keep me\nFROM\tdual", result.Text);
            Assert.Equal(0, result.Kept);
        }

        [Fact]
        public void ALineCommentAfterFromKeepsTheNextClauseOnItsOwnLine()
        {
            var result = Format("select a,b from t -- c\nwhere x=1 and y=2");

            Assert.Equal("SELECT\ta\n\t  , b\nFROM\tt -- c\nWHERE\tx = 1\n\tAND y = 2", result);
        }

        [Fact]
        public void ACommentAfterACondition()
        {
            var result = Format("select a from t where x = 1 -- first\n and y = 2 -- second\n and z = 3");

            Assert.Contains("x = 1 -- first", result);
            Assert.Contains("y = 2 -- second", result);
            Assert.Contains("AND z = 3", result);
        }

        [Fact]
        public void ABlockCommentInsideAListStaysBetweenTheSameTokens()
        {
            var result = Format("select a, /* b */ c from t");

            Assert.Contains(", /* b */", result);
            Assert.Contains("c", result);
        }

        [Fact]
        public void ABlockCommentBeforeAKeyword()
        {
            var result = Format("select a from t /* filter */ where x = 1");

            Assert.Contains("t /* filter */", result);
            Assert.Contains("WHERE\tx = 1", result);
        }

        [Theory]
        [InlineData("select  1   as  one,\n   2 as two   -- keep me\nfrom dual")]
        [InlineData("select a,b from t -- c\nwhere x=1 and y=2")]
        [InlineData("select a from t where x = 1 -- first\n and y = 2 -- second\n and z = 3")]
        [InlineData("select a, /* b */ c from t /* filter */ where x = 1")]
        [InlineData("select a -- one\n -- two\n from t")]
        [InlineData("update t set a = 1 -- note\n, b = 2 where id = 3")]
        [InlineData("insert into t (a, b) -- cols\n values (1, 2)")]
        public void FormattingTwiceGivesTheSameTextAndKeepsEveryComment(string sql)
        {
            var once = Run(sql);
            var twice = Format(once.Text);

            Assert.Equal(once.Text, twice);
            Assert.Equal(0, once.Kept);
            foreach (var token in SqlPrompter.Core.Lexing.Lexer.Tokenize(sql))
            {
                if (token.Kind == SqlPrompter.Core.Lexing.TokenKind.Comment)
                {
                    Assert.Contains(token.Text, once.Text);
                }
            }
        }

        [Fact]
        public void AStatementWithACommentIsNotCollapsedOntoOneLine()
        {
            var style = new FormatStyle { CollapseStatementsShorterThan = 120 };

            Assert.Equal("SELECT\ta -- x\nFROM\tt", Format("select a -- x\nfrom t", style));
        }
    }
}
