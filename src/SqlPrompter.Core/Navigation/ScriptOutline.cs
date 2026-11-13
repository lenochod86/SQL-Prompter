using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>One entry of the outline of a script (#224).</summary>
    public sealed class OutlineItem
    {
        public OutlineItem(string kind, string name, int line, int offset, int depth)
        {
            Kind = kind;
            Name = name;
            Line = line;
            Offset = offset;
            Depth = depth;
        }

        /// <summary>Batch, Statement, CTE, Temp table, Variable or Module.</summary>
        public string Kind { get; }

        public string Name { get; }

        /// <summary>1-based line.</summary>
        public int Line { get; }

        /// <summary>Character position in the script.</summary>
        public int Offset { get; }

        /// <summary>0 for batches, 1 for statements, 2 for what a statement holds.</summary>
        public int Depth { get; }

        public string Text => Kind + ": " + Name;
    }

    /// <summary>
    /// The structure of a script: batches (separated by GO), the statements of each, the CTEs, temporary tables (where they are created and dropped),
    /// variables and the header of a procedure, function, view or trigger. Built from the lexer and the statement splitter, so unfinished code gives
    /// a (possibly shorter) outline and never fails.
    /// </summary>
    public static class ScriptOutline
    {
        public static IReadOnlyList<OutlineItem> Build(string script)
        {
            var items = new List<OutlineItem>();
            if (string.IsNullOrWhiteSpace(script))
            {
                return items;
            }

            var tokens = Lexer.Tokenize(script).Where(t => t.Kind != TokenKind.Comment).ToList();
            if (tokens.Count == 0)
            {
                return items;
            }

            var lineStarts = new List<int> { 0 };
            for (var i = 0; i < script.Length; i++)
            {
                if (script[i] == '\n')
                {
                    lineStarts.Add(i + 1);
                }
            }

            int LineOf(int offset)
            {
                var index = lineStarts.BinarySearch(offset);
                return (index >= 0 ? index : ~index - 1) + 1;
            }

            string Shortened(int from, int to)
            {
                var lineEnd = script.IndexOfAny(new[] { '\r', '\n' }, from);
                var end = lineEnd >= 0 ? Math.Min(to, lineEnd) : to;
                var text = script.Substring(from, Math.Max(0, end - from)).Trim();
                return text.Length > 60 ? text.Substring(0, 60) + "…" : text;
            }

            var batch = 0;
            var starts = StatementSplitter.Split(tokens);
            var batchOpen = false;
            for (var s = 0; s < starts.Count; s++)
            {
                var first = starts[s];
                var end = s + 1 < starts.Count ? starts[s + 1] : tokens.Count;
                var head = tokens[first];
                if (head.IsWord("GO"))
                {
                    batchOpen = false;
                    continue;
                }

                if (!batchOpen)
                {
                    batchOpen = true;
                    batch++;
                    items.Add(new OutlineItem("Batch", batch.ToString(), LineOf(head.Start), head.Start, 0));
                }

                var kind = IsBlockHead(tokens, first, end) ? "Block" : "Statement";
                items.Add(new OutlineItem(kind, Shortened(head.Start, tokens[end - 1].End), LineOf(head.Start), head.Start, 1));
                AddDetails(items, tokens, first, end, LineOf);
            }

            return items;
        }

        /// <summary>IF, ELSE, WHILE, BEGIN TRY and BEGIN CATCH start a block (a plain BEGIN, END and the like stay statements).</summary>
        private static bool IsBlockHead(List<Token> tokens, int first, int end)
        {
            var head = tokens[first];
            if (head.IsWord("IF") || head.IsWord("ELSE") || head.IsWord("WHILE"))
            {
                return true;
            }

            return head.IsWord("BEGIN") && first + 1 < end && (tokens[first + 1].IsWord("TRY") || tokens[first + 1].IsWord("CATCH"));
        }

        private static void AddDetails(List<OutlineItem> items, List<Token> tokens, int first, int end, Func<int, int> lineOf)
        {
            var head = tokens[first];
            if (head.IsWord("DECLARE"))
            {
                // DECLARE @a int, @b int = 1: a variable name directly after DECLARE or after a top-level comma.
                var depth = 0;
                for (var i = first + 1; i < end; i++)
                {
                    if (tokens[i].Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (tokens[i].Kind == TokenKind.CloseParen)
                    {
                        depth = Math.Max(0, depth - 1);
                    }
                    else if (depth == 0 && tokens[i].Kind == TokenKind.Variable && (i == first + 1 || tokens[i - 1].Kind == TokenKind.Comma))
                    {
                        items.Add(new OutlineItem("Variable", tokens[i].Text, lineOf(tokens[i].Start), tokens[i].Start, 2));
                    }
                }

                return;
            }

            if (head.IsWord("CREATE") || head.IsWord("ALTER"))
            {
                var k = first + 1;
                if (k + 1 < end && tokens[k].IsWord("OR") && tokens[k + 1].IsWord("ALTER"))
                {
                    k += 2;
                }

                if (k < end && (tokens[k].IsWord("PROC") || tokens[k].IsWord("PROCEDURE") || tokens[k].IsWord("FUNCTION") || tokens[k].IsWord("VIEW") || tokens[k].IsWord("TRIGGER")))
                {
                    var name = NameAt(tokens, k + 1, end);
                    if (name.Length > 0)
                    {
                        items.Add(new OutlineItem("Module", tokens[k].Upper + " " + name, lineOf(head.Start), head.Start, 2));
                        var open = 0;
                        for (var i = k + 1; i < end; i++)
                        {
                            if (tokens[i].Kind == TokenKind.OpenParen)
                            {
                                open++;
                            }
                            else if (tokens[i].Kind == TokenKind.CloseParen)
                            {
                                open = Math.Max(0, open - 1);
                            }
                            else if (open <= 1 && tokens[i].Kind == TokenKind.Variable && i > k + 1 &&
                                     (tokens[i - 1].Kind == TokenKind.Comma || tokens[i - 1].Kind == TokenKind.OpenParen || tokens[i - 1].IsNamePart || tokens[i - 1].Kind == TokenKind.Dot))
                            {
                                items.Add(new OutlineItem("Parameter", tokens[i].Text, lineOf(tokens[i].Start), tokens[i].Start, 2));
                            }
                        }
                    }
                }
                else if (k + 1 < end && tokens[k].IsWord("TABLE") && tokens[k + 1].Text.StartsWith("#", StringComparison.Ordinal))
                {
                    items.Add(new OutlineItem("Temp table", tokens[k + 1].Text + " (created)", lineOf(head.Start), head.Start, 2));
                }

                return;
            }

            if (head.IsWord("DROP"))
            {
                for (var i = first + 1; i < Math.Min(end, first + 5); i++)
                {
                    if (tokens[i].Kind == TokenKind.Word && tokens[i].Text.StartsWith("#", StringComparison.Ordinal))
                    {
                        items.Add(new OutlineItem("Temp table", tokens[i].Text + " (dropped)", lineOf(head.Start), head.Start, 2));
                        break;
                    }
                }

                return;
            }

            // SELECT ... INTO #t
            var level = 0;
            for (var i = first; i < end; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    level++;
                }
                else if (tokens[i].Kind == TokenKind.CloseParen)
                {
                    level = Math.Max(0, level - 1);
                }
                else if (level == 0 && tokens[i].IsWord("INTO") && i + 1 < end && tokens[i + 1].Text.StartsWith("#", StringComparison.Ordinal) && !head.IsWord("INSERT"))
                {
                    items.Add(new OutlineItem("Temp table", tokens[i + 1].Text + " (created)", lineOf(tokens[i].Start), tokens[i].Start, 2));
                }
            }

            // WITH a AS (...), b AS (...)
            if (head.IsWord("WITH"))
            {
                var depth = 0;
                for (var i = first + 1; i < end; i++)
                {
                    if (tokens[i].Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (tokens[i].Kind == TokenKind.CloseParen)
                    {
                        depth = Math.Max(0, depth - 1);
                    }
                    else if (depth == 0 && tokens[i].IsNamePart && !tokens[i].IsWord("AS") && (i == first + 1 || tokens[i - 1].Kind == TokenKind.Comma) && NextIsAsOrColumns(tokens, i, end))
                    {
                        items.Add(new OutlineItem("CTE", tokens[i].IdentifierValue, lineOf(tokens[i].Start), tokens[i].Start, 2));
                    }
                }
            }
        }

        private static bool NextIsAsOrColumns(List<Token> tokens, int i, int end) =>
            i + 1 < end && (tokens[i + 1].IsWord("AS") || tokens[i + 1].Kind == TokenKind.OpenParen);

        private static string NameAt(List<Token> tokens, int index, int end)
        {
            var parts = new List<string>();
            while (index < end && tokens[index].IsNamePart)
            {
                parts.Add(tokens[index].IdentifierValue);
                if (index + 1 < end && tokens[index + 1].Kind == TokenKind.Dot)
                {
                    index += 2;
                }
                else
                {
                    break;
                }
            }

            return string.Join(".", parts);
        }

        /// <summary>
        /// The offset to jump to from <paramref name="offset"/> (#224): the start of the next (or previous) statement or block, or of the next
        /// (or previous) batch; -1 when there is none.
        /// </summary>
        public static int JumpTarget(IReadOnlyList<OutlineItem> items, int offset, bool batches, bool forward)
        {
            var starts = items.Where(i => batches ? i.Kind == "Batch" : i.Kind == "Statement" || i.Kind == "Block")
                .Select(i => i.Offset).Distinct().OrderBy(o => o).ToList();
            return forward
                ? starts.Where(o => o > offset).DefaultIfEmpty(-1).First()
                : starts.Where(o => o < offset).DefaultIfEmpty(-1).Last();
        }

        /// <summary>The index of the item that holds the position: the last one that starts at or before it, or -1.</summary>
        public static int IndexAt(IReadOnlyList<OutlineItem> items, int offset)
        {
            var found = -1;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Offset <= offset)
                {
                    found = i;
                }
            }

            return found;
        }
    }
}
