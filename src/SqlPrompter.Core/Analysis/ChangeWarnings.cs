using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>A statement that changes data or schema, found for the confirmation in a marked environment (#209).</summary>
    public sealed class ChangeStatement
    {
        public ChangeStatement(string kind, int line, string statement)
        {
            Kind = kind;
            Line = line;
            Statement = statement;
        }

        /// <summary>UPDATE, DELETE, TRUNCATE, DROP, ALTER, CREATE OR ALTER, MERGE or sp_rename.</summary>
        public string Kind { get; }

        /// <summary>1-based line of the statement in the script.</summary>
        public int Line { get; }

        /// <summary>The first line of the statement, shortened.</summary>
        public string Statement { get; }

        public string Message => Kind + " (line " + Line + "): " + Statement;
    }

    /// <summary>
    /// Lists what a script changes (<c>UPDATE</c>, <c>DELETE</c> with or without <c>WHERE</c>, <c>TRUNCATE</c>, <c>DROP</c>, <c>ALTER</c>,
    /// <c>CREATE OR ALTER</c>, <c>MERGE</c>, <c>EXEC sp_rename</c>), and finds a transaction that the script leaves open. Comments and strings are
    /// ignored. Changes of temporary tables and table variables are routine and left out.
    /// </summary>
    public static class ChangeWarnings
    {
        /// <summary>The statements of the script that change data or schema, in script order.</summary>
        public static IReadOnlyList<ChangeStatement> FindChanges(string script)
        {
            var result = new List<ChangeStatement>();
            if (string.IsNullOrWhiteSpace(script))
            {
                return result;
            }

            var tokens = Lexer.Tokenize(script).Where(t => t.Kind != TokenKind.Comment).ToList();
            var starts = StatementSplitter.Split(tokens);
            for (var s = 0; s < starts.Count; s++)
            {
                var first = starts[s];
                var end = s + 1 < starts.Count ? starts[s + 1] : tokens.Count;
                var kind = KindOf(tokens, first, end);
                if (kind == null)
                {
                    continue;
                }

                var line = 1 + script.Take(tokens[first].Start).Count(c => c == '\n');
                var lineEnd = script.IndexOfAny(new[] { '\r', '\n' }, tokens[first].Start);
                var text = script.Substring(tokens[first].Start, (lineEnd < 0 ? script.Length : lineEnd) - tokens[first].Start).Trim();
                if (text.Length > 80)
                {
                    text = text.Substring(0, 80) + "…";
                }

                result.Add(new ChangeStatement(kind, line, text));
            }

            return result;
        }

        private static string KindOf(List<Token> tokens, int first, int end)
        {
            // A CTE in front of the statement.
            var i = first;
            if (tokens[i].IsWord("WITH"))
            {
                var depth = 0;
                for (i = first + 1; i < end; i++)
                {
                    if (tokens[i].Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (tokens[i].Kind == TokenKind.CloseParen)
                    {
                        depth = Math.Max(0, depth - 1);
                    }
                    else if (depth == 0 && (tokens[i].IsWord("UPDATE") || tokens[i].IsWord("DELETE") || tokens[i].IsWord("MERGE") || tokens[i].IsWord("INSERT") || tokens[i].IsWord("SELECT")))
                    {
                        break;
                    }
                }

                if (i >= end)
                {
                    return null;
                }
            }

            var word = tokens[i];
            string kind;
            if (word.IsWord("UPDATE") || word.IsWord("MERGE"))
            {
                kind = word.Upper;
                return i + 1 < end && IsScratch(tokens[i + 1]) ? null : kind;
            }

            if (word.IsWord("DELETE"))
            {
                var target = i + 1 < end && tokens[i + 1].IsWord("FROM") ? i + 2 : i + 1;
                return target < end && IsScratch(tokens[target]) ? null : "DELETE";
            }

            if (word.IsWord("TRUNCATE") || word.IsWord("DROP") || word.IsWord("ALTER"))
            {
                var target = i + 1;
                while (target < end && !IsScratchSkippable(tokens[target]))
                {
                    target++;
                }

                return target < end && IsScratch(tokens[target]) ? null : word.Upper;
            }

            if (word.IsWord("CREATE") && i + 2 < end && tokens[i + 1].IsWord("OR") && tokens[i + 2].IsWord("ALTER"))
            {
                return "CREATE OR ALTER";
            }

            if ((word.IsWord("EXEC") || word.IsWord("EXECUTE")) && i + 1 < end && tokens[i + 1].Kind == TokenKind.Word && tokens[i + 1].Text.EndsWith("sp_rename", StringComparison.OrdinalIgnoreCase))
            {
                return "sp_rename";
            }

            return null;
        }

        /// <summary>The first token of the object name after TRUNCATE TABLE / DROP TABLE [IF EXISTS] / ALTER TABLE: not a keyword that only introduces it.</summary>
        private static bool IsScratchSkippable(Token token) =>
            !(token.IsWord("TABLE") || token.IsWord("IF") || token.IsWord("EXISTS") || token.IsWord("VIEW") || token.IsWord("INDEX") || token.IsWord("PROCEDURE") || token.IsWord("PROC")
              || token.IsWord("FUNCTION") || token.IsWord("TRIGGER") || token.IsWord("DATABASE") || token.IsWord("SCHEMA") || token.IsWord("SEQUENCE") || token.IsWord("SYNONYM") || token.IsWord("TYPE"));

        private static bool IsScratch(Token token) =>
            token.Kind == TokenKind.Variable || (token.Kind == TokenKind.Word && token.Text.StartsWith("#", StringComparison.Ordinal));

        /// <summary>
        /// The line of a <c>BEGIN TRAN</c> that has no <c>COMMIT</c> or <c>ROLLBACK</c> in the same batch (batches are separated by GO), or 0 when
        /// every transaction is closed. A batch that stores a procedure, function or trigger is not looked at.
        /// </summary>
        public static int OpenTransactionLine(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return 0;
            }

            var tokens = Lexer.Tokenize(script).Where(t => t.Kind != TokenKind.Comment).ToList();
            var open = new Stack<int>();
            var storing = false;
            for (var i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.IsWord("GO"))
                {
                    if (open.Count > 0)
                    {
                        return open.Peek();
                    }

                    storing = false;
                    continue;
                }

                if (storing)
                {
                    continue;
                }

                if (t.IsWord("CREATE") || t.IsWord("ALTER"))
                {
                    for (var k = i + 1; k < Math.Min(tokens.Count, i + 5); k++)
                    {
                        if (tokens[k].IsWord("PROC") || tokens[k].IsWord("PROCEDURE") || tokens[k].IsWord("FUNCTION") || tokens[k].IsWord("TRIGGER"))
                        {
                            storing = true;
                            break;
                        }
                    }
                }
                else if (t.IsWord("BEGIN") && i + 1 < tokens.Count && (tokens[i + 1].IsWord("TRAN") || tokens[i + 1].IsWord("TRANSACTION")))
                {
                    open.Push(1 + script.Take(t.Start).Count(c => c == '\n'));
                }
                else if ((t.IsWord("COMMIT") || t.IsWord("ROLLBACK")) && open.Count > 0)
                {
                    open.Pop();
                }
            }

            return open.Count > 0 ? open.Peek() : 0;
        }
    }
}
