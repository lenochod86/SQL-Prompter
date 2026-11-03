using System;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Lexing
{
    public enum TokenKind
    {
        /// <summary>Identifier or keyword, including temporary tables (#t).</summary>
        Word,

        /// <summary>[name] or "name".</summary>
        QuotedIdentifier,

        /// <summary>@name or @@name.</summary>
        Variable,

        /// <summary>'text' or N'text'.</summary>
        String,

        Number,
        Comment,
        Dot,
        Comma,
        OpenParen,
        CloseParen,
        Semicolon,
        Operator,
    }

    public sealed class Token
    {
        private string upper;

        public Token(TokenKind kind, int start, int end, string text, bool isUnterminated)
        {
            Kind = kind;
            Start = start;
            End = end;
            Text = text;
            IsUnterminated = isUnterminated;
        }

        public TokenKind Kind { get; }

        public int Start { get; }

        /// <summary>Position after the last character.</summary>
        public int End { get; }

        public string Text { get; }

        /// <summary>The closing character is missing (string, comment, [identifier]) and the token ends at the end of the text.</summary>
        public bool IsUnterminated { get; }

        /// <summary>Upper-case text of a word; null for other tokens.</summary>
        public string Upper => Kind == TokenKind.Word ? upper ?? (upper = Text.ToUpperInvariant()) : null;

        public bool IsWord(string keyword) => Kind == TokenKind.Word && string.Equals(Text, keyword, StringComparison.OrdinalIgnoreCase);

        public bool IsLineComment => Kind == TokenKind.Comment && Text.StartsWith("--", StringComparison.Ordinal);

        /// <summary>Part of a multi-part name: word, [identifier] or "identifier".</summary>
        public bool IsNamePart => Kind == TokenKind.Word || Kind == TokenKind.QuotedIdentifier;

        /// <summary>Identifier that is not a reserved keyword.</summary>
        public bool IsIdentifier => Kind == TokenKind.QuotedIdentifier || (Kind == TokenKind.Word && !Keywords.IsReserved(Text));

        /// <summary>Identifier value without delimiters and escaping.</summary>
        public string IdentifierValue
        {
            get
            {
                if (Kind != TokenKind.QuotedIdentifier || Text.Length == 0)
                {
                    return Text;
                }

                var close = Text[0] == '[' ? ']' : '"';
                var inner = Text.Substring(1, Text.Length - (IsUnterminated ? 1 : 2));
                return inner.Replace(new string(close, 2), close.ToString());
            }
        }

        public override string ToString() => Kind + " '" + Text + "' @" + Start;
    }
}
