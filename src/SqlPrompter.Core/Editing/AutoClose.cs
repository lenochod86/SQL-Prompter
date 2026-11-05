using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Editing
{
    public enum AutoCloseAction
    {
        /// <summary>The character is typed as it is.</summary>
        None,

        /// <summary>The character is typed and its closing partner is inserted after the caret.</summary>
        InsertPair,

        /// <summary>The closing character is already after the caret: step over it instead of typing a second one.</summary>
        Overtype,
    }

    /// <summary>
    /// Auto-closing of <c>'</c> and <c>[</c>. It decides from the text before the character is typed; the same lexer as the completion tells
    /// strings, comments and bracketed names apart, so nothing is closed inside them.
    /// </summary>
    public static class AutoClose
    {
        public static AutoCloseAction Decide(string text, int caret, char typed)
        {
            if (text == null || caret < 0 || caret > text.Length || (typed != '\'' && typed != '[' && typed != ']'))
            {
                return AutoCloseAction.None;
            }

            var next = caret < text.Length ? text[caret] : '\0';
            var previous = caret > 0 ? text[caret - 1] : '\0';
            var inside = TokenAround(text, caret);

            if (typed == ']')
            {
                // Typing the closing bracket of a name whose closing bracket is already there (the one this feature inserted).
                return next == ']' && inside != null && inside.Kind == TokenKind.QuotedIdentifier && !inside.IsUnterminated &&
                       inside.Text[0] == '[' && inside.End == caret + 1
                    ? AutoCloseAction.Overtype
                    : AutoCloseAction.None;
            }

            if (typed == '\'')
            {
                if (inside != null && inside.Kind == TokenKind.String)
                {
                    // The closing quote is right there: step over it. Anywhere else in a string the quote is just typed (it doubles).
                    return next == '\'' && !inside.IsUnterminated && inside.End == caret + 1 && inside.Start < caret
                        ? AutoCloseAction.Overtype
                        : AutoCloseAction.None;
                }

                if (inside != null || next == '\'' || previous == '\'' || IsWordCharacter(next))
                {
                    return AutoCloseAction.None;
                }

                // A quote right after a word is not a start (it is a closing quote or a typo); N'text' is the exception.
                if (IsWordCharacter(previous) && !IsNationalPrefix(text, caret))
                {
                    return AutoCloseAction.None;
                }

                return AutoCloseAction.InsertPair;
            }

            // '['
            if (inside != null || IsWordCharacter(next) || IsWordCharacter(previous))
            {
                return AutoCloseAction.None;
            }

            return AutoCloseAction.InsertPair;
        }

        /// <summary>The string, comment or bracketed name the caret is inside of (a caret at the very end counts for an unfinished one).</summary>
        private static Token TokenAround(string text, int caret)
        {
            foreach (var token in Lexer.Tokenize(text))
            {
                if (token.Start >= caret)
                {
                    break;
                }

                if (token.Kind != TokenKind.String && token.Kind != TokenKind.Comment && token.Kind != TokenKind.QuotedIdentifier)
                {
                    continue;
                }

                var open = token.IsUnterminated || token.IsLineComment;
                if (caret < token.End || (open && caret == token.End))
                {
                    return token;
                }
            }

            return null;
        }

        private static bool IsNationalPrefix(string text, int caret) =>
            (text[caret - 1] == 'N' || text[caret - 1] == 'n') && (caret < 2 || !IsWordCharacter(text[caret - 2]));

        private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';
    }
}
