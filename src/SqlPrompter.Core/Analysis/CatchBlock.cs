using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>Whether the caret is inside a <c>BEGIN CATCH ... END CATCH</c> block, where the ERROR_ functions belong (#218).</summary>
    public static class CatchBlock
    {
        public static bool IsInside(string text, int caret)
        {
            if (string.IsNullOrEmpty(text) || caret <= 0)
            {
                return false;
            }

            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment && t.Start < caret).ToList();
            var depth = 0;
            for (var i = 0; i + 1 < tokens.Count; i++)
            {
                if (tokens[i].IsWord("BEGIN") && tokens[i + 1].IsWord("CATCH"))
                {
                    depth++;
                }
                else if (tokens[i].IsWord("END") && tokens[i + 1].IsWord("CATCH") && depth > 0)
                {
                    depth--;
                }
            }

            return depth > 0;
        }
    }
}
