using System;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Lexing
{
    public enum TokenKind
    {
        /// <summary>Identifikátor nebo klíčové slovo, včetně dočasných tabulek (#t).</summary>
        Word,

        /// <summary>[name] nebo "name".</summary>
        QuotedIdentifier,

        /// <summary>@name nebo @@name.</summary>
        Variable,

        /// <summary>'text' nebo N'text'.</summary>
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

        /// <summary>Pozice za posledním znakem.</summary>
        public int End { get; }

        public string Text { get; }

        /// <summary>Chybí uzavírací znak (řetězec, komentář, [identifikátor]) a token končí koncem textu.</summary>
        public bool IsUnterminated { get; }

        /// <summary>Text slova velkými písmeny, u jiných tokenů null.</summary>
        public string Upper => Kind == TokenKind.Word ? upper ?? (upper = Text.ToUpperInvariant()) : null;

        public bool IsWord(string keyword) => Kind == TokenKind.Word && string.Equals(Text, keyword, StringComparison.OrdinalIgnoreCase);

        public bool IsLineComment => Kind == TokenKind.Comment && Text.StartsWith("--", StringComparison.Ordinal);

        /// <summary>Část víceúrovňového názvu: slovo, [identifikátor] nebo "identifikátor".</summary>
        public bool IsNamePart => Kind == TokenKind.Word || Kind == TokenKind.QuotedIdentifier;

        /// <summary>Identifikátor, který není rezervované klíčové slovo.</summary>
        public bool IsIdentifier => Kind == TokenKind.QuotedIdentifier || (Kind == TokenKind.Word && !Keywords.IsReserved(Text));

        /// <summary>Hodnota identifikátoru bez oddělovačů a escapování.</summary>
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
