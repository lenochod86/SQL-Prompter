using System.Collections.Generic;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Which typed characters insert the selected suggestion, mirroring the SQL Prompt option
    /// "Insert selected suggestions into your code when any of the following keys are pressed".
    /// Every other character in <see cref="PotentialCharacters"/> only closes the list and is typed,
    /// so a dot after a partially typed alias keeps the alias and opens its columns.
    /// Enter and Tab always insert.
    /// </summary>
    public sealed class CommitKeys
    {
        private static readonly char[] WordEnders =
        {
            ' ', '.', ',', '(', ')', '[', ']', ';', '=', '<', '>', '+', '-', '*', '/', '%', '!', '&', '|', '^', '~', '\'', '"',
        };

        public CommitKeys(bool space, bool dot, bool comma, bool parentheses, bool semicolon, bool closingBracket)
        {
            Space = space;
            Dot = dot;
            Comma = comma;
            Parentheses = parentheses;
            Semicolon = semicolon;
            ClosingBracket = closingBracket;
        }

        /// <summary>The user's SQL Prompt settings: only Space (besides Enter and Tab) inserts.</summary>
        public static CommitKeys Default { get; } = new CommitKeys(space: true, dot: false, comma: false, parentheses: false, semicolon: false, closingBracket: false);

        public bool Space { get; }

        public bool Dot { get; }

        public bool Comma { get; }

        public bool Parentheses { get; }

        public bool Semicolon { get; }

        public bool ClosingBracket { get; }

        /// <summary>Characters that end the word being completed: each either inserts the suggestion or closes the list.</summary>
        public IReadOnlyList<char> PotentialCharacters => WordEnders;

        /// <summary>True if typing the character inserts the selected suggestion before the character itself.</summary>
        public bool Commits(char typedChar)
        {
            switch (typedChar)
            {
                case ' ':
                    return Space;
                case '.':
                    return Dot;
                case ',':
                    return Comma;
                case '(':
                case ')':
                    return Parentheses;
                case ';':
                    return Semicolon;
                case ']':
                    return ClosingBracket;
                default:
                    return false;
            }
        }
    }
}
