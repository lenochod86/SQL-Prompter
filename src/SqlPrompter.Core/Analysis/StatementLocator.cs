using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>A span of the text: where a statement starts and how long it is.</summary>
    public sealed class StatementRange
    {
        public StatementRange(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }

        public int Length { get; }

        public int End => Start + Length;
    }

    /// <summary>Finds the statement the caret is in, with the statement splitting the completion uses.</summary>
    public static class StatementLocator
    {
        /// <summary>
        /// The statement at the caret from its first to its last token (a trailing semicolon included, comments around it not). A caret in the
        /// white space or in a comment after a statement belongs to that statement; before the first statement, to the first one.
        /// Null for an empty script and when the caret is on a GO line.
        /// </summary>
        public static StatementRange Find(string text, int caret)
        {
            if (string.IsNullOrEmpty(text) || caret < 0 || caret > text.Length)
            {
                return null;
            }

            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            if (tokens.Count == 0)
            {
                return null;
            }

            var bounds = StatementSplitter.Split(tokens);
            var statement = 0;
            for (var i = 0; i < bounds.Count; i++)
            {
                if (tokens[bounds[i]].Start <= caret)
                {
                    statement = i;
                }
            }

            var first = bounds[statement];
            var last = (statement + 1 < bounds.Count ? bounds[statement + 1] : tokens.Count) - 1;
            if (tokens[first].IsWord("GO"))
            {
                return null;
            }

            return new StatementRange(tokens[first].Start, tokens[last].End - tokens[first].Start);
        }
    }
}
