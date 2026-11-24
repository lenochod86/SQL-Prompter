using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>The other end of a BEGIN ... END block (#224).</summary>
    public sealed class BlockMatch
    {
        public BlockMatch(int target, bool targetIsEnd, string name, int line)
        {
            Target = target;
            TargetIsEnd = targetIsEnd;
            Name = name;
            Line = line;
        }

        /// <summary>Character position of the BEGIN or END to jump to.</summary>
        public int Target { get; }

        /// <summary>True when the caret is on a BEGIN and the target is its END.</summary>
        public bool TargetIsEnd { get; }

        /// <summary>For example "END TRY" or "BEGIN".</summary>
        public string Name { get; }

        /// <summary>1-based line of the target.</summary>
        public int Line { get; }
    }

    /// <summary>
    /// Pairs BEGIN with END (BEGIN TRY with END TRY, BEGIN CATCH with END CATCH). CASE ... END is skipped as a pair but counted, so its END does not
    /// close a block; BEGIN TRANSACTION, BEGIN DIALOG and the like are not blocks. Unfinished code that has no partner gives null.
    /// </summary>
    public static class BlockMatcher
    {
        public static BlockMatch Find(string text, int caret)
        {
            if (string.IsNullOrEmpty(text) || caret < 0 || caret > text.Length)
            {
                return null;
            }

            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            var stack = new Stack<int>();
            var pairs = new Dictionary<int, int>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind != TokenKind.Word)
                {
                    continue;
                }

                if (token.IsWord("CASE"))
                {
                    stack.Push(i);
                }
                else if (token.IsWord("BEGIN"))
                {
                    var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
                    if (next != null && (next.IsWord("TRAN") || next.IsWord("TRANSACTION") || next.IsWord("DISTRIBUTED") || next.IsWord("DIALOG") || next.IsWord("CONVERSATION")))
                    {
                        continue;
                    }

                    stack.Push(i);
                }
                else if (token.IsWord("END") && stack.Count > 0)
                {
                    var open = stack.Pop();
                    if (tokens[open].IsWord("BEGIN"))
                    {
                        pairs[open] = i;
                        pairs[i] = open;
                    }
                }
            }

            // The word under the caret (or just before it); TRY and CATCH count as part of BEGIN TRY and END CATCH.
            var at = tokens.FindIndex(t => t.Kind == TokenKind.Word && t.Start <= caret && caret <= t.End);
            if (at > 0 && (tokens[at].IsWord("TRY") || tokens[at].IsWord("CATCH")) && (tokens[at - 1].IsWord("BEGIN") || tokens[at - 1].IsWord("END")))
            {
                at--;
            }

            if (at < 0 || !pairs.TryGetValue(at, out var other))
            {
                return null;
            }

            var isBegin = tokens[at].IsWord("BEGIN");
            var target = tokens[other];
            var name = target.Upper;
            if (other + 1 < tokens.Count && (tokens[other + 1].IsWord("TRY") || tokens[other + 1].IsWord("CATCH")))
            {
                name += " " + tokens[other + 1].Upper;
            }

            var line = 1;
            for (var i = 0; i < target.Start; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return new BlockMatch(target.Start, isBegin, name, line);
        }
    }
}
