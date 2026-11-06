using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>
    /// Finds a script that stops in the middle of a construct, at the end of every batch (the end of the text or a line with <c>GO</c>): an
    /// unclosed parenthesis, an unterminated string, name or comment, a CTE that no statement follows, <c>BEGIN</c> or <c>CASE</c> without
    /// <c>END</c>, and a last token that needs something after it (<c>FROM</c>, <c>WHERE</c>, <c>AND</c>, a comma, an operator ...).
    /// </summary>
    internal static class UnexpectedEnd
    {
        private static readonly HashSet<string> NeedsMore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FROM", "JOIN", "ON", "WHERE", "AND", "OR", "BY", "SET", "VALUES", "SELECT", "INTO", "EXEC", "EXECUTE", "HAVING", "UNION", "ALL", "WHEN", "THEN", "ELSE", "NOT", "IN", "LIKE", "BETWEEN", "AS", "TOP", "DISTINCT",
        };

        private static readonly HashSet<string> StartsCteStatement = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
        };

        private static readonly HashSet<string> NotABlock = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TRAN", "TRANSACTION", "DISTRIBUTED", "DIALOG", "CONVERSATION",
        };

        public static void Find(string script, List<Token> allTokens, List<CodeIssue> issues)
        {
            foreach (var token in allTokens.Where(t => t.IsUnterminated))
            {
                var what = token.Kind == TokenKind.Comment ? "block comment" : token.Kind == TokenKind.String ? "string" : "name";
                issues.Add(new CodeIssue(CodeIssueKind.UnexpectedEnd, CodeIssueSeverity.Warning, token.Start, token.End - token.Start, "Unexpected end: the " + what + " is not closed."));
            }

            var tokens = allTokens.Where(t => t.Kind != TokenKind.Comment).ToList();
            var batch = new List<Token>();
            foreach (var token in tokens)
            {
                if (IsGo(script, token))
                {
                    FindInBatch(batch, issues);
                    batch.Clear();
                }
                else
                {
                    batch.Add(token);
                }
            }

            FindInBatch(batch, issues);
        }

        /// <summary>GO on a line of its own (optionally with a count).</summary>
        private static bool IsGo(string script, Token token)
        {
            if (!token.IsWord("GO"))
            {
                return false;
            }

            var lineStart = token.Start == 0 ? 0 : script.LastIndexOf('\n', token.Start - 1) + 1;

            if (script.Substring(lineStart, token.Start - lineStart).Trim().Length > 0)
            {
                return false;
            }

            var lineEnd = script.IndexOf('\n', token.End);
            var rest = script.Substring(token.End, (lineEnd < 0 ? script.Length : lineEnd) - token.End).Trim();
            return rest.Length == 0 || rest.All(char.IsDigit);
        }

        private static void FindInBatch(List<Token> batch, List<CodeIssue> issues)
        {
            if (batch.Count == 0)
            {
                return;
            }

            var unclosedParen = false;
            var parens = new Stack<Token>();
            foreach (var token in batch)
            {
                if (token.Kind == TokenKind.OpenParen)
                {
                    parens.Push(token);
                }
                else if (token.Kind == TokenKind.CloseParen && parens.Count > 0)
                {
                    parens.Pop();
                }
            }

            foreach (var open in parens)
            {
                unclosedParen = true;
                issues.Add(new CodeIssue(CodeIssueKind.UnexpectedEnd, CodeIssueSeverity.Warning, open.Start, 1, "Unexpected end: the parenthesis is not closed."));
            }

            FindCteWithoutStatement(batch, unclosedParen, issues);
            FindOpenBlocks(batch, issues);
            if (!unclosedParen)
            {
                FindDanglingEnd(batch, issues);
            }
        }

        /// <summary><c>WITH cte AS (...)</c> and nothing after it.</summary>
        private static void FindCteWithoutStatement(List<Token> batch, bool unclosedParen, List<CodeIssue> issues)
        {
            if (unclosedParen)
            {
                return;
            }

            for (var i = 0; i < batch.Count; i++)
            {
                if (!batch[i].IsWord("WITH") || (i > 0 && batch[i - 1].Kind != TokenKind.Semicolon))
                {
                    continue;
                }

                var j = i + 1;
                Token last = null;
                while (j < batch.Count)
                {
                    if (batch[j].Kind != TokenKind.Word && batch[j].Kind != TokenKind.QuotedIdentifier)
                    {
                        break;
                    }

                    last = batch[j];
                    j++;
                    if (j < batch.Count && batch[j].Kind == TokenKind.OpenParen)
                    {
                        j = SkipParens(batch, j);
                    }

                    if (j >= batch.Count || !batch[j].IsWord("AS"))
                    {
                        break;
                    }

                    j++;
                    if (j >= batch.Count || batch[j].Kind != TokenKind.OpenParen)
                    {
                        break;
                    }

                    j = SkipParens(batch, j);
                    if (j < batch.Count && batch[j].Kind == TokenKind.Comma)
                    {
                        j++;
                        continue;
                    }

                    if (j >= batch.Count || batch[j].Kind == TokenKind.Semicolon && j == batch.Count - 1)
                    {
                        issues.Add(new CodeIssue(
                            CodeIssueKind.UnexpectedEnd,
                            CodeIssueSeverity.Warning,
                            batch[i].Start,
                            batch[i].End - batch[i].Start,
                            "Unexpected end: the CTE " + last.Text + " is not followed by a statement."));
                    }

                    break;
                }
            }
        }

        private static int SkipParens(List<Token> batch, int open)
        {
            var depth = 0;
            for (var j = open; j < batch.Count; j++)
            {
                if (batch[j].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (batch[j].Kind == TokenKind.CloseParen && --depth == 0)
                {
                    return j + 1;
                }
            }

            return batch.Count;
        }

        /// <summary><c>BEGIN</c> blocks (also <c>BEGIN TRY</c> / <c>BEGIN CATCH</c>) and <c>CASE</c> need their <c>END</c>.</summary>
        private static void FindOpenBlocks(List<Token> batch, List<CodeIssue> issues)
        {
            var open = new Stack<Token>();
            for (var i = 0; i < batch.Count; i++)
            {
                var token = batch[i];
                var next = i + 1 < batch.Count ? batch[i + 1] : null;
                if (token.IsWord("CASE"))
                {
                    open.Push(token);
                }
                else if (token.IsWord("BEGIN") && !(next != null && next.Kind == TokenKind.Word && NotABlock.Contains(next.Text)))
                {
                    open.Push(token);
                }
                else if (token.IsWord("END") && !(next != null && next.IsWord("CONVERSATION")))
                {
                    if (open.Count > 0)
                    {
                        open.Pop();
                    }

                    if (next != null && (next.IsWord("TRY") || next.IsWord("CATCH")))
                    {
                        i++;
                    }
                }
            }

            foreach (var token in open)
            {
                issues.Add(new CodeIssue(
                    CodeIssueKind.UnexpectedEnd,
                    CodeIssueSeverity.Warning,
                    token.Start,
                    token.End - token.Start,
                    "Unexpected end: " + token.Upper + " has no END."));
            }
        }

        /// <summary>The last statement of the batch has a JOIN and does not start with SET, so a final ON is a join condition that is missing.</summary>
        private static bool IsJoinCondition(List<Token> batch)
        {
            var start = 0;
            for (var i = 0; i < batch.Count; i++)
            {
                if (batch[i].Kind == TokenKind.Semicolon)
                {
                    start = i + 1;
                }
            }

            return start < batch.Count && !batch[start].IsWord("SET") && batch.Skip(start).Any(t => t.IsWord("JOIN"));
        }

        /// <summary>The last token of the batch needs something after it.</summary>
        private static void FindDanglingEnd(List<Token> batch, List<CodeIssue> issues)
        {
            var last = batch.Count - 1;
            while (last >= 0 && batch[last].Kind == TokenKind.Semicolon)
            {
                last--;
            }

            if (last < 0)
            {
                return;
            }

            var token = batch[last];
            if (token.IsWord("ON") && !IsJoinCondition(batch))
            {
                return; // SET NOCOUNT ON and the like
            }

            if (token.Kind == TokenKind.Word && NeedsMore.Contains(token.Text))
            {
                issues.Add(new CodeIssue(CodeIssueKind.UnexpectedEnd, CodeIssueSeverity.Warning, token.Start, token.End - token.Start, "Unexpected end: " + token.Upper + " has nothing after it."));
            }
            else if (token.Kind == TokenKind.Comma)
            {
                issues.Add(new CodeIssue(CodeIssueKind.UnexpectedEnd, CodeIssueSeverity.Warning, token.Start, 1, "Unexpected end: the list ends with a comma."));
            }
            else if (token.Kind == TokenKind.Operator && token.Text != "*" && token.Text != ";")
            {
                issues.Add(new CodeIssue(CodeIssueKind.UnexpectedEnd, CodeIssueSeverity.Warning, token.Start, token.End - token.Start, "Unexpected end: the expression after " + token.Text + " is missing."));
            }
        }
    }
}
