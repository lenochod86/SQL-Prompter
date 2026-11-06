using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Editing
{
    /// <summary>The result of turning a statement into a preview: the script, or the reason why that is not possible.</summary>
    public sealed class PreviewResult
    {
        private PreviewResult(string text, string reason)
        {
            Text = text;
            Reason = reason;
        }

        public static PreviewResult Success(string text) => new PreviewResult(text, null);

        public static PreviewResult Failure(string reason) => new PreviewResult(null, reason);

        public bool Succeeded => Text != null;

        /// <summary>The script to show; null when the statement cannot be converted.</summary>
        public string Text { get; }

        /// <summary>Why the statement cannot be converted; null on success.</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// UPDATE and DELETE before they run (#210): a <c>SELECT</c> over the rows the statement would change (with the new values of an UPDATE next to the
    /// row), or the statement wrapped in a transaction that is rolled back and reports the number of rows. Nothing is executed here; the script goes to
    /// a new window. A statement that cannot be converted safely gives a reason instead.
    /// </summary>
    public static class StatementPreview
    {
        /// <summary>The SELECT that returns the rows of one UPDATE or DELETE statement.</summary>
        public static PreviewResult ToSelect(string statement, string newLine = "\r\n")
        {
            var parsed = Parse(statement, out var failure);
            if (parsed == null)
            {
                return PreviewResult.Failure(failure);
            }

            var list = new StringBuilder();
            list.Append(parsed.TopText).Append(parsed.SourceHasFrom ? parsed.TargetReference + ".*" : "*");
            foreach (var assignment in parsed.Assignments)
            {
                list.Append(", ").Append(assignment.Value).Append(" AS [New ").Append(assignment.Key.Replace("]", "]]")).Append(']');
            }

            var text = new StringBuilder();
            text.Append("SELECT ").Append(list);
            if (parsed.SourceHasFrom)
            {
                text.Append(newLine).Append(parsed.FromAndAfter);
            }
            else
            {
                text.Append(newLine).Append("FROM ").Append(parsed.Target);
                if (parsed.AfterTarget.Length > 0)
                {
                    text.Append(newLine).Append(parsed.AfterTarget);
                }
            }

            return PreviewResult.Success(text.ToString());
        }

        /// <summary>The statement inside <c>BEGIN TRANSACTION</c> and <c>ROLLBACK</c> with the number of rows it changed.</summary>
        public static PreviewResult ToDryRun(string statement, string newLine = "\r\n")
        {
            var tokens = Lexer.Tokenize(statement ?? string.Empty).Where(t => t.Kind != TokenKind.Comment).ToList();
            if (tokens.Count == 0 || !(tokens[0].IsWord("UPDATE") || tokens[0].IsWord("DELETE") || tokens[0].IsWord("INSERT") || tokens[0].IsWord("MERGE") || tokens[0].IsWord("WITH")))
            {
                return PreviewResult.Failure("The statement is not an UPDATE, DELETE, INSERT or MERGE.");
            }

            if (tokens.Count(t => t.Kind == TokenKind.Semicolon) > 1 || (tokens.Any(t => t.Kind == TokenKind.Semicolon) && tokens[tokens.Count - 1].Kind != TokenKind.Semicolon))
            {
                return PreviewResult.Failure("The selection holds more than one statement; select one.");
            }

            var body = statement.Trim().TrimEnd(';').TrimEnd();
            return PreviewResult.Success("BEGIN TRANSACTION;" + newLine + body + ";" + newLine + "SELECT @@ROWCOUNT AS [Rows affected];" + newLine + "ROLLBACK TRANSACTION;" + newLine);
        }

        private sealed class Parsed
        {
            public string TopText = string.Empty;
            public string Target;
            public string TargetReference;
            public bool SourceHasFrom;
            public string FromAndAfter = string.Empty;
            public string AfterTarget = string.Empty;
            public List<KeyValuePair<string, string>> Assignments = new List<KeyValuePair<string, string>>();
        }

        private static Parsed Parse(string statement, out string failure)
        {
            failure = null;
            var text = statement ?? string.Empty;
            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            while (tokens.Count > 0 && tokens[tokens.Count - 1].Kind == TokenKind.Semicolon)
            {
                tokens.RemoveAt(tokens.Count - 1);
            }

            if (tokens.Count == 0)
            {
                failure = "There is no statement.";
                return null;
            }

            if (tokens.Any(t => t.Kind == TokenKind.Semicolon))
            {
                failure = "The selection holds more than one statement; select one.";
                return null;
            }

            var update = tokens[0].IsWord("UPDATE");
            if (tokens[0].IsWord("WITH"))
            {
                failure = "A statement that starts with a CTE is not converted; preview the query inside the CTE instead.";
                return null;
            }

            if (!update && !tokens[0].IsWord("DELETE"))
            {
                failure = "The statement is not an UPDATE or DELETE.";
                return null;
            }

            // Keywords of the first level.
            int Find(string word, int from)
            {
                var depth = 0;
                for (var i = from; i < tokens.Count; i++)
                {
                    if (tokens[i].Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (tokens[i].Kind == TokenKind.CloseParen)
                    {
                        depth--;
                    }
                    else if (depth == 0 && tokens[i].IsWord(word))
                    {
                        return i;
                    }
                }

                return -1;
            }

            if (Find("OUTPUT", 1) >= 0)
            {
                failure = "A statement with OUTPUT is not converted; it already returns the changed rows when it runs.";
                return null;
            }

            var parsed = new Parsed();
            var index = 1;
            if (index < tokens.Count && tokens[index].IsWord("TOP"))
            {
                var top = index;
                index++;
                if (index < tokens.Count && tokens[index].Kind == TokenKind.OpenParen)
                {
                    var depth = 0;
                    for (; index < tokens.Count; index++)
                    {
                        if (tokens[index].Kind == TokenKind.OpenParen)
                        {
                            depth++;
                        }
                        else if (tokens[index].Kind == TokenKind.CloseParen && --depth == 0)
                        {
                            index++;
                            break;
                        }
                    }
                }
                else
                {
                    index++;
                }

                if (index < tokens.Count && tokens[index].IsWord("PERCENT"))
                {
                    index++;
                }

                parsed.TopText = text.Substring(tokens[top].Start, tokens[index - 1].End - tokens[top].Start) + " ";
            }

            if (!update && index < tokens.Count && tokens[index].IsWord("FROM"))
            {
                index++;
            }

            // The target: a name of up to four parts.
            var targetStart = index;
            while (index < tokens.Count && tokens[index].IsNamePart)
            {
                index++;
                if (index < tokens.Count && tokens[index].Kind == TokenKind.Dot)
                {
                    index++;
                }
                else
                {
                    break;
                }
            }

            if (index == targetStart)
            {
                failure = "The target of the statement is not a table name.";
                return null;
            }

            parsed.Target = text.Substring(tokens[targetStart].Start, tokens[index - 1].End - tokens[targetStart].Start);
            parsed.TargetReference = parsed.Target;
            var from = Find("FROM", index);
            var where = Find("WHERE", index);
            var option = Find("OPTION", index);
            var set = update ? Find("SET", index) : -1;
            if (update && set != index)
            {
                failure = "The UPDATE has table hints or an alias before SET; it is not converted.";
                return null;
            }

            if (!update && index < tokens.Count && !(tokens[index].IsWord("FROM") || tokens[index].IsWord("WHERE") || tokens[index].IsWord("OPTION")))
            {
                failure = "The DELETE has table hints or an alias before the next clause; it is not converted.";
                return null;
            }

            var end = text.Length;
            var tail = tokens.Count > 0 ? tokens[tokens.Count - 1].End : 0;
            if (update)
            {
                var assignmentsEnd = new[] { from, where, option }.Where(p => p > 0).DefaultIfEmpty(tokens.Count).Min();
                var depth = 0;
                var itemStart = set + 1;
                for (var i = set + 1; i <= assignmentsEnd; i++)
                {
                    var atEnd = i == assignmentsEnd;
                    if (!atEnd)
                    {
                        if (tokens[i].Kind == TokenKind.OpenParen)
                        {
                            depth++;
                        }
                        else if (tokens[i].Kind == TokenKind.CloseParen)
                        {
                            depth--;
                        }
                    }

                    if (atEnd || (depth == 0 && tokens[i].Kind == TokenKind.Comma))
                    {
                        if (i > itemStart)
                        {
                            var equals = -1;
                            for (var k = itemStart; k < i; k++)
                            {
                                if (tokens[k].Kind == TokenKind.Operator && tokens[k].Text == "=")
                                {
                                    equals = k;
                                    break;
                                }
                            }

                            if (equals < 0 || equals == itemStart || equals + 1 >= i)
                            {
                                failure = "An assignment of the SET clause could not be read.";
                                return null;
                            }

                            var column = text.Substring(tokens[itemStart].Start, tokens[equals - 1].End - tokens[itemStart].Start).Trim();
                            if (tokens[itemStart].Kind != TokenKind.Variable)
                            {
                                var value = text.Substring(tokens[equals + 1].Start, tokens[i - 1].End - tokens[equals + 1].Start).Trim();
                                parsed.Assignments.Add(new KeyValuePair<string, string>(column.Replace("[", string.Empty).Replace("]", string.Empty), value));
                            }
                        }

                        itemStart = i + 1;
                    }
                }

                if (parsed.Assignments.Count == 0)
                {
                    failure = "The SET clause assigns only variables; there is nothing to preview.";
                    return null;
                }
            }

            if (from >= 0 && update)
            {
                parsed.SourceHasFrom = true;
                parsed.FromAndAfter = text.Substring(tokens[from].Start, tail - tokens[from].Start);
            }
            else if (from >= 0)
            {
                // DELETE t FROM t JOIN ...: the target reference is what stands after DELETE.
                parsed.SourceHasFrom = true;
                parsed.FromAndAfter = text.Substring(tokens[from].Start, tail - tokens[from].Start);
            }
            else
            {
                var rest = new[] { where, option }.Where(p => p > 0).DefaultIfEmpty(-1).Min();
                parsed.AfterTarget = rest < 0 ? string.Empty : text.Substring(tokens[rest].Start, tail - tokens[rest].Start);
            }

            _ = end;
            return parsed;
        }
    }
}
