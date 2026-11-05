using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    public enum ExecutionWarningKind
    {
        DeleteWithoutWhere,
        UpdateWithoutWhere,
    }

    public sealed class ExecutionWarning
    {
        public ExecutionWarning(ExecutionWarningKind kind, int line, string statement)
        {
            Kind = kind;
            Line = line;
            Statement = statement;
        }

        public ExecutionWarningKind Kind { get; }

        /// <summary>1-based line of the statement in the script.</summary>
        public int Line { get; }

        /// <summary>The first line of the statement, shortened.</summary>
        public string Statement { get; }

        public string Message => (Kind == ExecutionWarningKind.DeleteWithoutWhere ? "DELETE" : "UPDATE") + " without WHERE (line " + Line + "): " + Statement;
    }

    /// <summary>
    /// Finds statements that are dangerous to run: <c>DELETE</c> or <c>UPDATE</c> without <c>WHERE</c>. Comments and strings are ignored. Not
    /// reported: statements limited with <c>TOP</c>, a temporary table or a table variable as the target, and statements inside a
    /// <c>CREATE</c> or <c>ALTER</c> of a procedure, function, trigger or view (they are not run, only stored).
    /// </summary>
    public static class ExecutionWarnings
    {
        private static readonly HashSet<string> ObjectsWithBody = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PROC", "PROCEDURE", "FUNCTION", "TRIGGER", "VIEW",
        };

        private static readonly HashSet<string> Dml = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
        };

        public static IReadOnlyList<ExecutionWarning> Find(string script, bool deleteWithoutWhere = true, bool updateWithoutWhere = true)
        {
            var warnings = new List<ExecutionWarning>();
            if (string.IsNullOrWhiteSpace(script) || (!deleteWithoutWhere && !updateWithoutWhere))
            {
                return warnings;
            }

            var tokens = Lexer.Tokenize(script).Where(t => t.Kind != TokenKind.Comment).ToList();
            var starts = StatementSplitter.Split(tokens);
            var storing = false;
            for (var s = 0; s < starts.Count; s++)
            {
                var first = starts[s];
                var end = s + 1 < starts.Count ? starts[s + 1] : tokens.Count;
                var head = tokens[first];
                if (head.IsWord("GO"))
                {
                    storing = false;
                    continue;
                }

                if (IsCreateOfObjectWithBody(tokens, first, end))
                {
                    storing = true;
                }

                if (storing)
                {
                    continue;
                }

                var warning = Inspect(tokens, first, end, script, deleteWithoutWhere, updateWithoutWhere);
                if (warning != null)
                {
                    warnings.Add(warning);
                }
            }

            return warnings;
        }

        private static bool IsCreateOfObjectWithBody(List<Token> tokens, int first, int end)
        {
            if (!tokens[first].IsWord("CREATE") && !tokens[first].IsWord("ALTER"))
            {
                return false;
            }

            for (var i = first + 1; i < Math.Min(end, first + 5); i++)
            {
                if (tokens[i].Kind == TokenKind.Word && ObjectsWithBody.Contains(tokens[i].Text))
                {
                    return true;
                }
            }

            return false;
        }

        private static ExecutionWarning Inspect(List<Token> tokens, int first, int end, string script, bool deleteWithoutWhere, bool updateWithoutWhere)
        {
            // The command is the first DML word at the top level (after WITH ... for a common table expression).
            var depth = 0;
            var command = -1;
            for (var i = first; i < end; i++)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (token.Kind == TokenKind.CloseParen)
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (depth == 0 && token.Kind == TokenKind.Word && Dml.Contains(token.Text))
                {
                    command = i;
                    break;
                }
            }

            if (command < 0)
            {
                return null;
            }

            var isDelete = tokens[command].IsWord("DELETE");
            var isUpdate = tokens[command].IsWord("UPDATE");
            if ((!isDelete || !deleteWithoutWhere) && (!isUpdate || !updateWithoutWhere))
            {
                return null;
            }

            // With a CTE only the part after WITH counts as the statement, and the statement must be what starts the batch or follows the CTE.
            var i2 = command + 1;
            if (i2 < end && tokens[i2].IsWord("TOP"))
            {
                return null;
            }

            if (isDelete && i2 < end && tokens[i2].IsWord("FROM"))
            {
                i2++;
            }

            if (i2 < end && IsScratchTarget(tokens[i2]))
            {
                return null;
            }

            depth = 0;
            for (var i = command + 1; i < end; i++)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (token.Kind == TokenKind.CloseParen)
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (depth == 0 && token.Kind == TokenKind.Word && token.IsWord("WHERE"))
                {
                    return null;
                }
            }

            var line = 1 + script.Take(tokens[command].Start).Count(c => c == '\n');
            var lineStart = script.LastIndexOf('\n', Math.Max(0, tokens[command].Start - 1)) + 1;
            var lineEnd = script.IndexOfAny(new[] { '\r', '\n' }, tokens[command].Start);
            var text = script.Substring(lineStart, (lineEnd < 0 ? script.Length : lineEnd) - lineStart).Trim();
            if (text.Length > 80)
            {
                text = text.Substring(0, 80) + "…";
            }

            return new ExecutionWarning(isDelete ? ExecutionWarningKind.DeleteWithoutWhere : ExecutionWarningKind.UpdateWithoutWhere, line, text);
        }

        /// <summary>A temporary table (#t, ##t) or a table variable (@t): clearing it is routine.</summary>
        private static bool IsScratchTarget(Token token) =>
            token.Kind == TokenKind.Variable || (token.Kind == TokenKind.Word && token.Text.StartsWith("#", StringComparison.Ordinal));
    }
}
