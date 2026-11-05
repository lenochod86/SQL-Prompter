using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Formatting
{
    /// <summary>The result of formatting: the new text and what happened to the statements.</summary>
    public sealed class FormatResult
    {
        internal FormatResult(string text, int formatted, int kept)
        {
            Text = text;
            Formatted = formatted;
            Kept = kept;
        }

        public string Text { get; }

        /// <summary>Statements laid out by the formatter.</summary>
        public int Formatted { get; }

        /// <summary>Statements left as they were (only the case of words changed) because they contain comments inside, or because the formatted text was not the same script.</summary>
        public int Kept { get; }
    }

    /// <summary>
    /// Formats a script statement by statement. The formatter only moves white space and changes the case of words; after laying out a
    /// statement it lexes the result and compares it with the original tokens, and a statement that differs is left as it was.
    /// </summary>
    public static class SqlFormatter
    {
        public static FormatResult Format(string text, FormatStyle style, string newLine = null)
        {
            style = style ?? new FormatStyle();
            if (string.IsNullOrWhiteSpace(text))
            {
                return new FormatResult(text, 0, 0);
            }

            newLine = newLine ?? (text.Contains("\r\n") ? "\r\n" : text.Contains("\n") ? "\n" : "\r\n");
            return new Engine(text, style, newLine).Run();
        }

        private sealed class Statement
        {
            public List<Token> Leading = new List<Token>();
            public List<Token> Body = new List<Token>();
            public List<Token> Trailing = new List<Token>();
        }

        private sealed class Clause
        {
            public List<R> Keyword = new List<R>();
            public List<R> Body = new List<R>();
        }

        private sealed class Engine
        {
            private readonly string text;
            private readonly FormatStyle style;
            private readonly string nl;
            private int formatted;
            private int kept;

            public Engine(string text, FormatStyle style, string newLine)
            {
                this.text = text;
                this.style = style;
                nl = newLine;
            }

            public FormatResult Run()
            {
                var all = Lexer.Tokenize(text).ToList();
                var code = all.Where(t => t.Kind != TokenKind.Comment).ToList();
                if (code.Count == 0)
                {
                    return new FormatResult(text, 0, 0);
                }

                var statements = Split(all, code);
                var output = new StringBuilder();
                var level = 0;
                var blocks = new Stack<int>();
                var bodyDepth = 0;
                string carry = null; // "ELSE " in front of the IF that follows
                var previousEnd = -1;
                var previousWasGo = false;
                for (var s = 0; s < statements.Count; s++)
                {
                    var statement = statements[s];
                    var first = statement.Leading.Count > 0 ? statement.Leading[0].Start : statement.Body[0].Start;
                    if (output.Length > 0)
                    {
                        output.Append(nl);
                        if (style.KeepBlankLines && !previousWasGo && BlankLineBetween(previousEnd, first))
                        {
                            output.Append(nl);
                        }
                    }

                    var head = statement.Body[0].Kind == TokenKind.Word ? statement.Body[0].Upper : string.Empty;
                    var second = statement.Body.FirstOrDefault(t => t != statement.Body[0] && t.Kind != TokenKind.Comment);
                    var secondWord = second != null && second.Kind == TokenKind.Word ? second.Upper : string.Empty;
                    var isGo = head == "GO";
                    var isBlockStart = head == "BEGIN" && secondWord != "TRAN" && secondWord != "TRANSACTION" && secondWord != "DISTRIBUTED" &&
                                       secondWord != "DIALOG" && secondWord != "CONVERSATION";
                    var isBlockEnd = head == "END";
                    var isHeader = head == "IF" || head == "WHILE" || head == "ELSE";
                    if (isBlockEnd && blocks.Count > 0 && style.IndentBlocks)
                    {
                        level -= blocks.Pop();
                    }

                    var effective = isGo ? 0 : level + (isBlockEnd ? 0 : isBlockStart ? Math.Max(0, bodyDepth - 1) : bodyDepth);
                    foreach (var comment in statement.Leading)
                    {
                        output.Append(Indent(effective * style.TabWidth)).Append(comment.Text).Append(nl);
                    }

                    string body;
                    if (isGo)
                    {
                        body = FormatTokens.Apply("GO", style.KeywordCase) + string.Concat(statement.Body.Skip(1).Select(t => " " + t.Text));
                        level = 0;
                        blocks.Clear();
                        bodyDepth = 0;
                    }
                    else
                    {
                        body = FormatStatement(statement, effective * style.TabWidth, carry, s + 1 < statements.Count ? statements[s + 1] : null, out var consumed);
                        if (consumed)
                        {
                            // "ELSE" is written in front of the IF that follows it.
                            carry = body;
                            previousEnd = LastEnd(statement);
                            previousWasGo = false;
                            continue;
                        }

                        carry = null;
                    }

                    output.Append(body);
                    foreach (var comment in statement.Trailing)
                    {
                        output.Append(' ').Append(comment.Text);
                    }

                    if (!isGo)
                    {
                        if (isHeader)
                        {
                            bodyDepth++;
                        }
                        else if (isBlockStart)
                        {
                            if (style.IndentBlocks)
                            {
                                var offset = Math.Max(0, bodyDepth - 1);
                                blocks.Push(offset + 1);
                                level += offset + 1;
                            }

                            bodyDepth = 0;
                        }
                        else
                        {
                            bodyDepth = 0;
                        }
                    }

                    previousEnd = LastEnd(statement);
                    previousWasGo = isGo;
                }

                // Comments after the last statement.
                var tail = statements[statements.Count - 1];
                _ = tail;
                return new FormatResult(output.ToString(), formatted, kept);
            }

            private static int LastEnd(Statement s) => s.Trailing.Count > 0 ? s.Trailing[s.Trailing.Count - 1].End : s.Body[s.Body.Count - 1].End;

            private bool BlankLineBetween(int previousEnd, int nextStart)
            {
                if (previousEnd < 0 || nextStart <= previousEnd)
                {
                    return false;
                }

                var lines = 0;
                for (var i = previousEnd; i < nextStart; i++)
                {
                    if (text[i] == '\n')
                    {
                        lines++;
                    }
                }

                return lines >= 2;
            }

            /// <summary>Statements with the comments around them: a comment on the line where a statement ends belongs to it, the others lead the next one.</summary>
            private List<Statement> Split(List<Token> all, List<Token> code)
            {
                var bounds = StatementSplitter.Split(code);
                var index = new Dictionary<Token, int>();
                for (var i = 0; i < all.Count; i++)
                {
                    index[all[i]] = i;
                }

                var result = new List<Statement>();
                var pendingLeading = new List<Token>();
                var firstAll = index[code[0]];
                for (var i = 0; i < firstAll; i++)
                {
                    pendingLeading.Add(all[i]);
                }

                for (var s = 0; s < bounds.Count; s++)
                {
                    var from = index[code[bounds[s]]];
                    var lastCode = (s + 1 < bounds.Count ? bounds[s + 1] : code.Count) - 1;
                    var to = index[code[lastCode]];
                    var statement = new Statement { Leading = pendingLeading };
                    pendingLeading = new List<Token>();
                    for (var i = from; i <= to; i++)
                    {
                        statement.Body.Add(all[i]);
                    }

                    var limit = s + 1 < bounds.Count ? index[code[bounds[s + 1]]] : all.Count;
                    var previousEnd = all[to].End;
                    var trailing = true;
                    for (var i = to + 1; i < limit; i++)
                    {
                        var comment = all[i];
                        if (trailing && !HasNewline(previousEnd, comment.Start) && (s + 1 < bounds.Count || true))
                        {
                            statement.Trailing.Add(comment);
                            previousEnd = comment.End;
                        }
                        else
                        {
                            trailing = false;
                            pendingLeading.Add(comment);
                        }
                    }

                    result.Add(statement);
                }

                // Comments left after the last statement go with it, each on its own line.
                if (pendingLeading.Count > 0)
                {
                    var last = result[result.Count - 1];
                    foreach (var comment in pendingLeading)
                    {
                        last.Trailing.Add(comment);
                    }
                }

                return result;
            }

            private bool HasNewline(int from, int to)
            {
                for (var i = from; i < to && i < text.Length; i++)
                {
                    if (text[i] == '\n' || text[i] == '\r')
                    {
                        return true;
                    }
                }

                return false;
            }

            private string Indent(int column)
            {
                if (column <= 0)
                {
                    return string.Empty;
                }

                return style.UseTabs
                    ? new string('\t', column / style.TabWidth) + new string(' ', column % style.TabWidth)
                    : new string(' ', column);
            }

            private int NextStop(int column) => (column / style.TabWidth + 1) * style.TabWidth;

            /// <summary>The text from the keyword's end to <paramref name="toColumn"/>: one tab, or spaces when tabs are not used.</summary>
            private string Pad(int fromColumn, int toColumn) =>
                style.UseTabs && toColumn - fromColumn <= style.TabWidth ? "\t" : new string(' ', Math.Max(1, toColumn - fromColumn));

            private string FormatStatement(Statement statement, int column, string carry, Statement next, out bool consumed)
            {
                consumed = false;
                var body = statement.Body;
                var codeTokens = body.Where(t => t.Kind != TokenKind.Comment).ToList();
                var original = text.Substring(body[0].Start, body[body.Count - 1].End - body[0].Start);
                var prefix = carry ?? string.Empty;
                string layout = null;
                var hasInnerComment = body.Any(t => t.Kind == TokenKind.Comment);
                if (!hasInnerComment)
                {
                    try
                    {
                        layout = Layout(codeTokens, column);
                    }
                    catch (Exception)
                    {
                        layout = null;
                    }
                }

                if (layout != null)
                {
                    var check = Lexer.Tokenize(layout).ToList();
                    if (FormatTokens.Equivalent(codeTokens, check))
                    {
                        formatted++;
                        if (codeTokens[0].IsWord("ELSE") && codeTokens.Count == 1 && next != null && next.Body[0].IsWord("IF") && next.Leading.Count == 0)
                        {
                            consumed = true;
                            return FormatTokens.Apply("ELSE", style.KeywordCase) + " ";
                        }

                        return carry != null ? PutOnLineOf(prefix, layout) : layout;
                    }
                }

                kept++;
                return Indent(column) + prefix + Verbatim(body);
            }

            private string PutOnLineOf(string prefix, string layout)
            {
                var trimmed = layout.TrimStart('\t', ' ');
                var indent = layout.Substring(0, layout.Length - trimmed.Length);
                return indent + prefix + trimmed;
            }

            /// <summary>The statement as written with the case of the words applied; later lines keep their own indentation.</summary>
            private string Verbatim(List<Token> tokens)
            {
                var code = tokens.Where(t => t.Kind != TokenKind.Comment).ToList();
                var cased = FormatTokens.CaseAll(code, style);
                var map = new Dictionary<Token, string>();
                for (var i = 0; i < code.Count; i++)
                {
                    map[code[i]] = cased[i];
                }

                var sb = new StringBuilder();
                var position = tokens[0].Start;
                foreach (var token in tokens)
                {
                    sb.Append(text, position, token.Start - position);
                    sb.Append(map.TryGetValue(token, out var changed) ? changed : token.Text);
                    position = token.End;
                }

                return sb.ToString();
            }

            // ------------------------------------------------------------------ layout of one statement

            /// <summary>The formatted statement (the first line indented by <paramref name="column"/>), or null to keep it.</summary>
            private string Layout(List<Token> code, int column)
            {
                var r = FormatTokens.Prepare(code, style);
                var semicolon = r.Count > 0 && r[r.Count - 1].Kind == TokenKind.Semicolon;
                if (semicolon)
                {
                    r.RemoveAt(r.Count - 1);
                }

                List<string> lines = null;
                var head = r[0].Kind == TokenKind.Word ? r[0].Upper : string.Empty;
                if (head == "SELECT" || head == "INSERT" || head == "UPDATE" || head == "DELETE" || head == "WITH")
                {
                    lines = LayoutDml(r, column);
                }
                else if (head == "CREATE" && r.Count > 2 && r[1].IsWord("TABLE"))
                {
                    lines = LayoutCreateTable(r, column);
                }

                if (lines == null || lines.Count == 0)
                {
                    lines = new List<string> { Indent(column) + Flat(r, 0, r.Count) };
                }

                if (semicolon)
                {
                    lines[lines.Count - 1] += ";";
                }

                return string.Join(nl, lines);
            }

            private List<string> LayoutDml(List<R> r, int column)
            {
                var flat = Flat(r, 0, r.Count);
                if (style.CollapseStatementsShorterThan > 0 && flat.Length < style.CollapseStatementsShorterThan)
                {
                    return new List<string> { Indent(column) + flat };
                }

                if (r[0].IsWord("WITH"))
                {
                    return LayoutCte(r, column);
                }

                var clauses = SplitClauses(r);
                if (clauses == null || clauses.Count == 0)
                {
                    return null;
                }

                var lines = new List<string>();
                foreach (var clause in clauses)
                {
                    LayoutClause(clause, column, lines);
                }

                return lines;
            }

            private List<string> LayoutCte(List<R> r, int column)
            {
                var lines = new List<string>();
                var i = 1;
                var firstCte = true;
                while (true)
                {
                    var asIndex = -1;
                    var depth = 0;
                    for (var k = i; k < r.Count; k++)
                    {
                        if (r[k].Kind == TokenKind.OpenParen)
                        {
                            depth++;
                        }
                        else if (r[k].Kind == TokenKind.CloseParen)
                        {
                            depth--;
                        }
                        else if (depth == 0 && r[k].IsWord("AS"))
                        {
                            asIndex = k;
                            break;
                        }
                    }

                    if (asIndex < 0 || asIndex + 1 >= r.Count || r[asIndex + 1].Kind != TokenKind.OpenParen)
                    {
                        return null;
                    }

                    var open = asIndex + 1;
                    var close = FindClose(r, open);
                    if (close < 0)
                    {
                        return null;
                    }

                    var header = Flat(r, i, asIndex + 1);
                    var inner = r.GetRange(open + 1, close - open - 1);
                    var innerFlat = Flat(inner, 0, inner.Count);
                    var lead = firstCte ? FormatTokens.Apply("WITH", style.KeywordCase) + " " : style.CommaBeforeItems ? ", " : string.Empty;
                    var width = lead.Length;
                    var comma = !firstCte || true;
                    _ = comma;
                    if (style.CollapseSubqueriesShorterThan > 0 && innerFlat.Length < style.CollapseSubqueriesShorterThan)
                    {
                        lines.Add(Indent(column) + lead + header + " (" + innerFlat + ")");
                    }
                    else
                    {
                        lines.Add(Indent(column) + lead + header);
                        lines.Add(Indent(column) + "(");
                        var nested = LayoutDml(inner, column + style.TabWidth) ?? new List<string> { Indent(column + style.TabWidth) + innerFlat };
                        lines.AddRange(nested);
                        lines.Add(Indent(column) + ")");
                    }

                    _ = width;
                    firstCte = false;
                    var after = close + 1;
                    if (after < r.Count && r[after].Kind == TokenKind.Comma)
                    {
                        if (!style.CommaBeforeItems)
                        {
                            lines[lines.Count - 1] += ",";
                        }

                        i = after + 1;
                        continue;
                    }

                    if (after >= r.Count)
                    {
                        return lines;
                    }

                    var main = LayoutDml(r.GetRange(after, r.Count - after), column);
                    if (main == null)
                    {
                        return null;
                    }

                    lines.AddRange(main);
                    return lines;
                }
            }

            private List<Clause> SplitClauses(List<R> r)
            {
                var clauses = new List<Clause>();
                Clause current = null;
                var depth = 0;
                var caseDepth = 0;
                for (var i = 0; i < r.Count; i++)
                {
                    var token = r[i];
                    if (token.Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (token.Kind == TokenKind.CloseParen)
                    {
                        depth--;
                    }
                    else if (token.IsWord("CASE"))
                    {
                        caseDepth++;
                    }
                    else if (token.IsWord("END") && caseDepth > 0)
                    {
                        caseDepth--;
                    }

                    var length = depth == 0 && caseDepth == 0 ? ClauseStartLength(r, i, clauses.Count) : 0;
                    if (length > 0)
                    {
                        current = new Clause();
                        current.Keyword.AddRange(r.GetRange(i, length));
                        clauses.Add(current);
                        i += length - 1;
                        continue;
                    }

                    if (current == null)
                    {
                        return null;
                    }

                    current.Body.Add(token);
                }

                return clauses;
            }

            private static int ClauseStartLength(List<R> r, int i, int clausesSoFar)
            {
                var token = r[i];
                if (token.Kind != TokenKind.Word)
                {
                    return 0;
                }

                var next = i + 1 < r.Count ? r[i + 1] : null;
                switch (token.Upper)
                {
                    case "SELECT":
                    case "WHERE":
                    case "HAVING":
                    case "VALUES":
                    case "OPTION":
                    case "OUTPUT":
                    case "FROM":
                    case "UPDATE":
                        return 1;
                    case "SET":
                        return clausesSoFar > 0 ? 1 : 0;
                    case "INTO":
                        return clausesSoFar > 0 ? 1 : 0;
                    case "INSERT":
                        return next != null && next.IsWord("INTO") ? 2 : 1;
                    case "DELETE":
                        return next != null && next.IsWord("FROM") ? 2 : 1;
                    case "GROUP":
                    case "ORDER":
                        return next != null && next.IsWord("BY") ? 2 : 0;
                    case "UNION":
                        return next != null && next.IsWord("ALL") ? 2 : 1;
                    case "EXCEPT":
                    case "INTERSECT":
                        return 1;
                    case "FOR":
                        return next != null && (next.IsWord("XML") || next.IsWord("JSON") || next.IsWord("BROWSE")) ? 1 : 0;
                    case "INNER":
                    case "LEFT":
                    case "RIGHT":
                    case "FULL":
                    case "CROSS":
                    case "OUTER":
                    case "JOIN":
                        var j = i;
                        while (j < r.Count && r[j].IsWord("INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER"))
                        {
                            j++;
                        }

                        return j < r.Count && r[j].IsWord("JOIN", "APPLY") ? j - i + 1 : 0;
                    default:
                        return 0;
                }
            }

            private void LayoutClause(Clause clause, int column, List<string> lines)
            {
                var keyword = string.Join(" ", clause.Keyword.Select(k => k.Text));
                var body = clause.Body;
                var name = clause.Keyword[0].Upper;
                var isList = name == "SELECT" || name == "GROUP" || name == "ORDER" || name == "SET" || name == "OUTPUT" || name == "VALUES" || name == "FROM";
                var isConditions = name == "WHERE" || name == "HAVING";
                var isJoin = clause.Keyword[clause.Keyword.Count - 1].IsWord("JOIN", "APPLY");
                var modifiers = 0;
                if (name == "SELECT")
                {
                    modifiers = SelectModifierCount(body);
                    if (modifiers > 0)
                    {
                        keyword += " " + Flat(body, 0, modifiers);
                        body = body.GetRange(modifiers, body.Count - modifiers);
                    }
                }

                var contentColumn = NextStop(column + keyword.Length);
                var head = Indent(column) + keyword;
                if (body.Count == 0)
                {
                    lines.Add(head);
                    return;
                }

                List<List<R>> items;
                string[] separators = null;
                if (isConditions && style.ConditionsOnSeparateLines)
                {
                    items = SplitConditions(body, out separators);
                }
                else if (isList)
                {
                    items = SplitCommas(body);
                }
                else if (isJoin)
                {
                    LayoutJoin(head, column, contentColumn, body, lines);
                    return;
                }
                else
                {
                    items = new List<List<R>> { body };
                }

                var newLineFirst = name == "SELECT" && modifiers > 0 && style.NewLineAfterDistinctAndTop && items.Count > 0;
                if (newLineFirst)
                {
                    lines.Add(head);
                    contentColumn = column + style.TabWidth;
                    head = null;
                }

                for (var k = 0; k < items.Count; k++)
                {
                    var itemStart = contentColumn;
                    string lead;
                    string trail = string.Empty;
                    if (k == 0 && head != null)
                    {
                        lead = head + Pad(column + keyword.Length, contentColumn);
                    }
                    else if (separators != null)
                    {
                        var op = separators[k - 1];
                        if (style.AndOrBeforeConditions)
                        {
                            var opColumn = Math.Max(column, contentColumn - op.Length - 1);
                            lead = Indent(opColumn) + op + new string(' ', Math.Max(1, contentColumn - opColumn - op.Length));
                        }
                        else
                        {
                            lead = Indent(contentColumn);
                        }
                    }
                    else if (style.CommaBeforeItems)
                    {
                        lead = Indent(Math.Max(column, contentColumn - 2)) + ", ";
                    }
                    else
                    {
                        lead = Indent(contentColumn);
                    }

                    if (k < items.Count - 1)
                    {
                        if (separators != null && !style.AndOrBeforeConditions)
                        {
                            trail = " " + separators[k];
                        }
                        else if (separators == null && !style.CommaBeforeItems)
                        {
                            trail = ",";
                        }
                    }

                    var rendered = Expand(items[k], itemStart);
                    rendered[0] = lead + rendered[0];
                    rendered[rendered.Count - 1] += trail;
                    lines.AddRange(rendered);
                }
            }

            private void LayoutJoin(string head, int column, int contentColumn, List<R> body, List<string> lines)
            {
                var on = -1;
                var depth = 0;
                for (var i = 0; i < body.Count; i++)
                {
                    if (body[i].Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (body[i].Kind == TokenKind.CloseParen)
                    {
                        depth--;
                    }
                    else if (depth == 0 && body[i].IsWord("ON"))
                    {
                        on = i;
                        break;
                    }
                }

                var source = on < 0 ? body : body.GetRange(0, on);
                var keywordWidth = head.Length - Indent(column).Length;
                var rendered = Expand(source, contentColumn);
                rendered[0] = head + Pad(column + keywordWidth, contentColumn) + rendered[0];
                if (on < 0)
                {
                    lines.AddRange(rendered);
                    return;
                }

                var conditions = SplitConditions(body.GetRange(on + 1, body.Count - on - 1), out var separators);
                var onText = body[on].Text;
                var last = rendered.Count - 1;
                rendered[last] += " " + onText + " " + Flat(conditions[0], 0, conditions[0].Count);
                lines.AddRange(rendered);
                for (var k = 1; k < conditions.Count; k++)
                {
                    var text = Flat(conditions[k], 0, conditions[k].Count);
                    if (style.AndOrBeforeConditions)
                    {
                        lines.Add(Indent(contentColumn) + separators[k - 1] + " " + text);
                    }
                    else
                    {
                        lines[lines.Count - 1] += " " + separators[k - 1];
                        lines.Add(Indent(contentColumn) + text);
                    }
                }
            }

            private static int SelectModifierCount(List<R> body)
            {
                var i = 0;
                while (i < body.Count)
                {
                    if (body[i].IsWord("DISTINCT", "ALL"))
                    {
                        i++;
                    }
                    else if (body[i].IsWord("TOP") && i + 1 < body.Count)
                    {
                        i++;
                        if (body[i].Kind == TokenKind.OpenParen)
                        {
                            var close = FindClose(body, i);
                            if (close < 0)
                            {
                                return 0;
                            }

                            i = close + 1;
                        }
                        else
                        {
                            i++;
                        }

                        if (i < body.Count && body[i].IsWord("PERCENT"))
                        {
                            i++;
                        }

                        if (i + 1 < body.Count && body[i].IsWord("WITH") && body[i + 1].IsWord("TIES"))
                        {
                            i += 2;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                return i;
            }

            private static List<List<R>> SplitCommas(List<R> body)
            {
                var items = new List<List<R>> { new List<R>() };
                var depth = 0;
                foreach (var token in body)
                {
                    if (token.Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (token.Kind == TokenKind.CloseParen)
                    {
                        depth--;
                    }

                    if (depth == 0 && token.Kind == TokenKind.Comma)
                    {
                        items.Add(new List<R>());
                        continue;
                    }

                    items[items.Count - 1].Add(token);
                }

                return items;
            }

            /// <summary>Splits at the AND and OR of the top level; the AND of a BETWEEN and anything inside a CASE or parentheses stays.</summary>
            private static List<List<R>> SplitConditions(List<R> body, out string[] separators)
            {
                var items = new List<List<R>> { new List<R>() };
                var ops = new List<string>();
                var depth = 0;
                var caseDepth = 0;
                var between = 0;
                foreach (var token in body)
                {
                    if (token.Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (token.Kind == TokenKind.CloseParen)
                    {
                        depth--;
                    }
                    else if (token.IsWord("CASE"))
                    {
                        caseDepth++;
                    }
                    else if (token.IsWord("END") && caseDepth > 0)
                    {
                        caseDepth--;
                    }
                    else if (depth == 0 && caseDepth == 0 && token.IsWord("BETWEEN"))
                    {
                        between++;
                    }

                    if (depth == 0 && caseDepth == 0 && (token.IsWord("AND") || token.IsWord("OR")))
                    {
                        if (token.IsWord("AND") && between > 0)
                        {
                            between--;
                        }
                        else
                        {
                            ops.Add(token.Text);
                            items.Add(new List<R>());
                            continue;
                        }
                    }

                    items[items.Count - 1].Add(token);
                }

                separators = ops.ToArray();
                return items;
            }

            /// <summary>
            /// The lines of an item that starts at <paramref name="startColumn"/>: the first one without indentation (the caller puts the keyword
            /// or comma in front), the others complete. A long subquery in parentheses is laid out on its own lines.
            /// </summary>
            private List<string> Expand(List<R> item, int startColumn)
            {
                var lines = new List<string>();
                var from = 0;
                var current = string.Empty;
                for (var i = 0; i < item.Count; i++)
                {
                    if (item[i].Kind != TokenKind.OpenParen || i + 1 >= item.Count || !item[i + 1].IsWord("SELECT", "WITH"))
                    {
                        continue;
                    }

                    var close = FindClose(item, i);
                    if (close < 0)
                    {
                        continue;
                    }

                    var inner = item.GetRange(i + 1, close - i - 1);
                    var innerFlat = Flat(inner, 0, inner.Count);
                    if (style.CollapseSubqueriesShorterThan > 0 && innerFlat.Length < style.CollapseSubqueriesShorterThan)
                    {
                        i = close;
                        continue;
                    }

                    var nested = LayoutDml(inner, startColumn + style.TabWidth);
                    if (nested == null)
                    {
                        continue;
                    }

                    var before = Flat(item, from, i + 1);
                    current += (current.Length > 0 && from > 0 && NeedsSpace(item, from - 1, from) ? " " : string.Empty) + before;
                    lines.Add(current);
                    lines.AddRange(nested);
                    var rest = Flat(item, close, item.Count);
                    var tailRest = Expand(item.GetRange(close + 1, item.Count - close - 1), startColumn);
                    var closing = Indent(startColumn) + ")";
                    if (item.Count > close + 1)
                    {
                        var after = tailRest[0];
                        closing += (NeedsSpace(item, close, close + 1) ? " " : string.Empty) + after;
                        lines.Add(closing);
                        lines.AddRange(tailRest.Skip(1));
                    }
                    else
                    {
                        _ = rest;
                        lines.Add(closing);
                    }

                    return lines;
                }

                lines.Add(Flat(item, 0, item.Count));
                return lines;
            }

            private List<string> LayoutCreateTable(List<R> r, int column)
            {
                var flat = Flat(r, 0, r.Count);
                if (style.CollapseDdlShorterThan > 0 && flat.Length < style.CollapseDdlShorterThan)
                {
                    return new List<string> { Indent(column) + flat };
                }

                var open = r.FindIndex(t => t.Kind == TokenKind.OpenParen);
                if (open < 0)
                {
                    return null;
                }

                var close = FindClose(r, open);
                if (close < 0)
                {
                    return null;
                }

                var items = SplitCommas(r.GetRange(open + 1, close - open - 1));
                var lines = new List<string> { Indent(column) + Flat(r, 0, open), Indent(column) + "(" };
                var nameWidth = items
                    .Where(i => i.Count > 1 && i[0].Kind != TokenKind.Word || (i.Count > 1 && !i[0].IsWord("CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "INDEX")))
                    .Select(i => i[0].Text.Length)
                    .DefaultIfEmpty(0)
                    .Max();
                var itemColumn = column + style.TabWidth;
                for (var k = 0; k < items.Count; k++)
                {
                    var item = items[k];
                    string rendered;
                    if (item.Count > 1 && !item[0].IsWord("CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "INDEX"))
                    {
                        rendered = item[0].Text.PadRight(nameWidth) + " " + Flat(item, 1, item.Count);
                    }
                    else
                    {
                        rendered = Flat(item, 0, item.Count);
                    }

                    string lead;
                    if (style.CommaBeforeItems)
                    {
                        lead = k == 0 ? Indent(itemColumn) + "  " : Indent(itemColumn) + ", ";
                    }
                    else
                    {
                        lead = Indent(itemColumn);
                    }

                    lines.Add(lead + rendered + (!style.CommaBeforeItems && k < items.Count - 1 ? "," : string.Empty));
                }

                lines.Add(Indent(column) + ")" + (close + 1 < r.Count ? (NeedsSpace(r, close, close + 1) ? " " : string.Empty) + Flat(r, close + 1, r.Count) : string.Empty));
                return lines;
            }

            // ------------------------------------------------------------------ flat text

            private static int FindClose(List<R> r, int open)
            {
                var depth = 0;
                for (var i = open; i < r.Count; i++)
                {
                    if (r[i].Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (r[i].Kind == TokenKind.CloseParen && --depth == 0)
                    {
                        return i;
                    }
                }

                return -1;
            }

            /// <summary>The items from..to on one line with normal spacing.</summary>
            private static string Flat(List<R> r, int from, int to)
            {
                var sb = new StringBuilder();
                for (var i = from; i < to; i++)
                {
                    if (i > from && NeedsSpace(r, i - 1, i))
                    {
                        sb.Append(' ');
                    }

                    sb.Append(r[i].Text);
                }

                return sb.ToString();
            }

            private static bool IsUnary(List<R> r, int i)
            {
                var token = r[i];
                if (token.Kind != TokenKind.Operator || (token.Text != "-" && token.Text != "+" && token.Text != "~"))
                {
                    return false;
                }

                if (i == 0)
                {
                    return true;
                }

                var previous = r[i - 1];
                switch (previous.Kind)
                {
                    case TokenKind.Operator:
                    case TokenKind.OpenParen:
                    case TokenKind.Comma:
                        return true;
                    case TokenKind.Word:
                        return previous.IsWord("SELECT", "WHERE", "AND", "OR", "NOT", "THEN", "ELSE", "WHEN", "ON", "BY", "RETURN", "IN", "SET", "VALUES", "BETWEEN", "HAVING", "CASE", "TOP");
                    default:
                        return false;
                }
            }

            private static bool NeedsSpace(List<R> r, int p, int c)
            {
                var previous = r[p];
                var current = r[c];
                if (current.Kind == TokenKind.Comma || current.Kind == TokenKind.CloseParen || current.Kind == TokenKind.Semicolon || current.Kind == TokenKind.Dot)
                {
                    return false;
                }

                if (previous.Kind == TokenKind.OpenParen || previous.Kind == TokenKind.Dot)
                {
                    return false;
                }

                if (previous.Kind == TokenKind.Operator && IsUnary(r, p))
                {
                    return false;
                }

                if (current.Kind == TokenKind.OpenParen)
                {
                    if (previous.Kind == TokenKind.Word)
                    {
                        var isFunction = Keywords.BuiltInFunctions.Contains(previous.Upper, StringComparer.OrdinalIgnoreCase) ||
                                         previous.IsWord("LEFT", "RIGHT", "COALESCE", "NULLIF", "CONVERT", "TRY_CONVERT", "CAST", "TRY_CAST") ||
                                         Keywords.DataTypes.Contains(previous.Upper, StringComparer.OrdinalIgnoreCase);
                        if (isFunction)
                        {
                            return false;
                        }

                        return Keywords.IsReserved(previous.Upper) || current.SpaceBefore;
                    }

                    if (previous.Kind == TokenKind.QuotedIdentifier || previous.Kind == TokenKind.Variable || previous.Kind == TokenKind.CloseParen)
                    {
                        return current.SpaceBefore;
                    }

                    return true;
                }

                if (previous.Kind == TokenKind.Operator && current.Kind == TokenKind.Operator)
                {
                    return current.SpaceBefore;
                }

                if (current.Kind == TokenKind.Operator && current.Text == ":")
                {
                    return false;
                }

                return true;
            }
        }
    }
}
