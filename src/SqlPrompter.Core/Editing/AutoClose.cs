using System;
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

        /// <summary>
        /// The <c>*</c> of a block comment that was closed automatically is typed: the caret steps over the (optional) space and the star of the closing <c> */</c>, so that
        /// typing <c>*/</c> ends the comment (the <c>/</c> is then stepped over by <see cref="Overtype"/>).
        /// </summary>
        OvertypeCommentStar,
    }

    /// <summary>The pairs that are closed automatically (the switches of the settings, #440).</summary>
    [Flags]
    public enum AutoClosePairs
    {
        None = 0,

        /// <summary><c>'</c> closes with <c>'</c>.</summary>
        Quote = 1,

        /// <summary><c>(</c> closes with <c>)</c>.</summary>
        Parenthesis = 2,

        /// <summary><c>[</c> closes with <c>]</c>.</summary>
        Bracket = 4,

        /// <summary><c>/*</c> closes with <c> */</c>.</summary>
        BlockComment = 8,

        All = Quote | Parenthesis | Bracket | BlockComment,
    }

    /// <summary>
    /// Auto-closing of <c>'</c>, <c>[</c>, <c>(</c> and <c>/*</c>. It decides from the text before the character is typed; the same lexer as the completion tells
    /// strings, comments and bracketed names apart, so nothing is closed inside them. Only the pairs in <c>enabled</c> do anything (no closing, no step over, no
    /// removal of an empty pair).
    /// </summary>
    public static class AutoClose
    {
        /// <summary>What is inserted after the caret when <c>/*</c> has been typed.</summary>
        public const string BlockCommentClosing = " */";

        public static AutoCloseAction Decide(string text, int caret, char typed) => Decide(text, caret, typed, AutoClosePairs.All);

        public static AutoCloseAction Decide(string text, int caret, char typed, AutoClosePairs enabled)
        {
            if (text == null || caret < 0 || caret > text.Length || (enabled & PairOf(typed)) == 0)
            {
                return AutoCloseAction.None;
            }

            var next = caret < text.Length ? text[caret] : '\0';
            var previous = caret > 0 ? text[caret - 1] : '\0';
            var inside = TokenAround(text, caret);

            if (typed == '*')
            {
                return DecideStar(text, caret, previous, inside);
            }

            if (typed == '/')
            {
                // The slash of the closing */ that was inserted with the opening: step over it.
                return previous == '*' && next == '/' && IsInsertedCommentEnd(inside, caret + 1) ? AutoCloseAction.Overtype : AutoCloseAction.None;
            }

            if (typed == ')')
            {
                // Step over a closing parenthesis only when the parentheses of the text are balanced (otherwise it is a deliberate one).
                return next == ')' && inside == null && IsBalanced(text) ? AutoCloseAction.Overtype : AutoCloseAction.None;
            }

            if (typed == '(')
            {
                // In front of a word the parenthesis is just typed; after a word (a function call) the pair is inserted.
                return inside != null || IsWordCharacter(next) ? AutoCloseAction.None : AutoCloseAction.InsertPair;
            }

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

        /// <summary>The star: it starts a block comment after a slash, or steps over the star of an inserted closing.</summary>
        private static AutoCloseAction DecideStar(string text, int caret, char previous, Token inside)
        {
            // " */" or "*/" right after the caret, the end of the comment the caret is in: the closing that was inserted with the opening.
            if (inside != null && IsInsertedCommentEnd(inside, caret + (StartsWith(text, caret, BlockCommentClosing) ? 3 : 2)) &&
                (StartsWith(text, caret, BlockCommentClosing) || StartsWith(text, caret, "*/")))
            {
                return AutoCloseAction.OvertypeCommentStar;
            }

            if (inside != null || previous != '/')
            {
                return AutoCloseAction.None;
            }

            // The slash must be a plain one, not the end of a comment (*/ and then a star) and not a part of a name or a number.
            if (caret >= 2 && text[caret - 2] == '*')
            {
                return AutoCloseAction.None;
            }

            // Another closing of this comment is already there (a comment that is being edited): nothing to add.
            return StartsWith(text, caret, BlockCommentClosing) || StartsWith(text, caret, "*/") ? AutoCloseAction.None : AutoCloseAction.InsertPair;
        }

        /// <summary>
        /// Backspace right after an opening character removes the empty pair <c>()</c>, <c>[]</c> or <c>''</c> around the caret, or the <c>/*</c> in front of the caret together
        /// with the untouched <c> */</c> behind it. <paramref name="start"/> and <paramref name="length"/> are the text to delete.
        /// </summary>
        public static bool TryGetPairDeletion(string text, int caret, AutoClosePairs enabled, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (text == null || caret < 1 || caret >= text.Length)
            {
                return false;
            }

            if (enabled.HasFlag(AutoClosePairs.BlockComment) && caret >= 2 && text[caret - 2] == '/' && text[caret - 1] == '*' && StartsWith(text, caret, BlockCommentClosing))
            {
                var comment = TokenAround(text, caret);
                if (comment != null && comment.Kind == TokenKind.Comment && !comment.IsLineComment && comment.Start == caret - 2 && comment.Text == "/*" + BlockCommentClosing)
                {
                    start = caret - 2;
                    length = 2 + BlockCommentClosing.Length;
                    return true;
                }

                return false;
            }

            var open = text[caret - 1];
            var close = text[caret];
            var pair = open == '(' && close == ')' ? AutoClosePairs.Parenthesis
                : open == '[' && close == ']' ? AutoClosePairs.Bracket
                : open == '\'' && close == '\'' ? AutoClosePairs.Quote
                : AutoClosePairs.None;
            if (pair == AutoClosePairs.None || (enabled & pair) == 0)
            {
                return false;
            }

            var inside = TokenAround(text, caret);
            bool deletes;
            switch (open)
            {
                case '(':
                    deletes = inside == null;
                    break;
                case '[':
                    deletes = inside != null && inside.Kind == TokenKind.QuotedIdentifier && inside.Text == "[]";
                    break;
                default:
                    deletes = inside != null && inside.Kind == TokenKind.String && inside.Text == "''";
                    break;
            }

            if (deletes)
            {
                start = caret - 1;
                length = 2;
            }

            return deletes;
        }

        /// <summary>True when Backspace right after an opening character removes the empty pair <c>()</c>, <c>[]</c> or <c>''</c> around the caret.</summary>
        public static bool DeletesPair(string text, int caret) =>
            TryGetPairDeletion(text, caret, AutoClosePairs.Quote | AutoClosePairs.Parenthesis | AutoClosePairs.Bracket, out _, out _);

        private static AutoClosePairs PairOf(char typed)
        {
            switch (typed)
            {
                case '\'': return AutoClosePairs.Quote;
                case '(':
                case ')': return AutoClosePairs.Parenthesis;
                case '[':
                case ']': return AutoClosePairs.Bracket;
                case '*':
                case '/': return AutoClosePairs.BlockComment;
                default: return AutoClosePairs.None;
            }
        }

        private static bool StartsWith(string text, int index, string value) => string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

        /// <summary>A terminated block comment that ends at <paramref name="end"/>.</summary>
        private static bool IsInsertedCommentEnd(Token comment, int end) =>
            comment != null && comment.Kind == TokenKind.Comment && !comment.IsLineComment && !comment.IsUnterminated && comment.End == end;

        private static bool IsBalanced(string text)
        {
            var depth = 0;
            foreach (var token in Lexer.Tokenize(text))
            {
                if (token.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (token.Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
            }

            return depth == 0;
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
