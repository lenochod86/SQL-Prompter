using System.Linq;
using SqlPrompter.Core.Lexing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class LexerTests
    {
        [Fact]
        public void SplitsSimpleSelect()
        {
            var kinds = Lexer.Tokenize("SELECT u.Name, 1 FROM [dbo].[Users] u;").Select(t => t.Kind).ToArray();

            Assert.Equal(new[]
            {
                TokenKind.Word, TokenKind.Word, TokenKind.Dot, TokenKind.Word, TokenKind.Comma, TokenKind.Number,
                TokenKind.Word, TokenKind.QuotedIdentifier, TokenKind.Dot, TokenKind.QuotedIdentifier, TokenKind.Word,
                TokenKind.Semicolon,
            }, kinds);
        }

        [Fact]
        public void ReadsStringsWithEscapedQuotes()
        {
            var tokens = Lexer.Tokenize("N'it''s' 'x'");

            Assert.Equal(2, tokens.Count);
            Assert.Equal("N'it''s'", tokens[0].Text);
            Assert.False(tokens[0].IsUnterminated);
        }

        [Fact]
        public void MarksUnterminatedTokens()
        {
            Assert.True(Lexer.Tokenize("SELECT 'abc").Last().IsUnterminated);
            Assert.True(Lexer.Tokenize("SELECT [abc").Last().IsUnterminated);
            Assert.True(Lexer.Tokenize("/* a /* b */ c").Single().IsUnterminated);
        }

        [Fact]
        public void HandlesNestedBlockComments()
        {
            var tokens = Lexer.Tokenize("/* a /* b */ c */ SELECT");

            Assert.Equal(TokenKind.Comment, tokens[0].Kind);
            Assert.True(tokens[1].IsWord("SELECT"));
        }

        [Fact]
        public void ReadsVariablesTempTablesAndNumbers()
        {
            var tokens = Lexer.Tokenize("@id @@ROWCOUNT #tmp ##g 1.5e3 0x1F .5");

            Assert.Equal(new[]
            {
                TokenKind.Variable, TokenKind.Variable, TokenKind.Word, TokenKind.Word, TokenKind.Number, TokenKind.Number,
                TokenKind.Number,
            }, tokens.Select(t => t.Kind).ToArray());
        }

        [Fact]
        public void UnquotesIdentifiers()
        {
            var tokens = Lexer.Tokenize("[a]]b] \"c\"\"d\" [open");

            Assert.Equal("a]b", tokens[0].IdentifierValue);
            Assert.Equal("c\"d", tokens[1].IdentifierValue);
            Assert.Equal("open", tokens[2].IdentifierValue);
        }
    }
}
