using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    internal static partial class CatalogRules
    {
        private static readonly string[] MarkerWords = { "TODO", "WARNING", "BUG", "FIX" };

        private static void ScriptRules(RuleContext c)
        {
            if (c.On(CodeIssueKind.SC004))
            {
                foreach (var comment in c.AllTokens.Where(x => x.Kind == TokenKind.Comment))
                {
                    var text = comment.Text.StartsWith("--", StringComparison.Ordinal) ? comment.Text.Substring(2) : comment.Text.TrimStart('/', '*');
                    text = text.TrimStart();
                    var marker = MarkerWords.FirstOrDefault(w => text.StartsWith(w, StringComparison.OrdinalIgnoreCase) && (text.Length == w.Length || !char.IsLetterOrDigit(text[w.Length])));
                    if (marker != null)
                    {
                        var end = c.Script.IndexOfAny(new[] { '\r', '\n' }, comment.Start);
                        var length = Math.Min(comment.End, end < 0 ? comment.End : end) - comment.Start;
                        c.Add(CodeIssueKind.SC004, comment.Start, Math.Max(length, 1), "A " + marker.ToUpperInvariant() + " comment marks unfinished work.");
                    }
                }
            }

            if (c.On(CodeIssueKind.SC005))
            {
                foreach (var m in c.Modules.Where(m => m.Kind == "PROC" && m.Name >= 0))
                {
                    var name = c.T[m.Name].IdentifierValue;
                    for (var i = m.Start; i < m.End; i++)
                    {
                        if (!c.Is(i, "GRANT") || c.StatementAt(i)[0] != i)
                        {
                            continue;
                        }

                        var end = c.StatementAt(i)[1];
                        for (var k = i + 1; k < end; k++)
                        {
                            if (c.Is(k, "ON"))
                            {
                                var last = k + 1;
                                while (c.IsKind(last + 1, TokenKind.Dot) && last + 2 < end)
                                {
                                    last += 2;
                                }

                                if (last < end && c.T[last].IsNamePart && string.Equals(c.T[last].IdentifierValue, name, StringComparison.OrdinalIgnoreCase))
                                {
                                    c.Add(CodeIssueKind.SC005, c.T[i], "The procedure " + name + " grants permissions on itself; put the GRANT in a separate batch after the procedure.");
                                }

                                break;
                            }
                        }
                    }
                }
            }

            if (c.On(CodeIssueKind.SC006))
            {
                LineEndings(c);
            }
        }

        private static void LineEndings(RuleContext c)
        {
            var s = c.Script;
            var bad = -1;
            for (var i = 0; i < s.Length; i++)
            {
                if ((s[i] == '\n' && (i == 0 || s[i - 1] != '\r')) || (s[i] == '\r' && (i + 1 >= s.Length || s[i + 1] != '\n')))
                {
                    bad = i;
                    break;
                }
            }

            if (bad < 0)
            {
                return;
            }

            var tail = s.Substring(bad);
            var fixedTail = tail.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            c.Add(CodeIssueKind.SC006, bad, 1, "The line break is not CR LF; the repair converts the rest of the script to CR LF.", new CodeFix(bad, tail.Length, fixedTail));
        }

        private static readonly HashSet<string> OldTopStatements = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "INSERT", "UPDATE", "DELETE" };

        private static void Style(RuleContext c)
        {
            var t = c.T;
            if (c.On(CodeIssueKind.ST013))
            {
                for (var i = 0; i + 1 < t.Count; i++)
                {
                    if (c.IsOp(i, "!") && c.IsOp(i + 1, "=") && c.Touches(i, i + 1) && !c.Is(i + 2, "NULL"))
                    {
                        c.Add(CodeIssueKind.ST013, t[i].Start, t[i + 1].End - t[i].Start, "!= is not standard SQL; use <>.", new CodeFix(t[i].Start, t[i + 1].End - t[i].Start, "<>"));
                    }
                }
            }

            if (c.On(CodeIssueKind.ST006))
            {
                foreach (var s in c.Statements)
                {
                    var head = c.UpperAt(s[0]);
                    if (head != null && OldTopStatements.Contains(head))
                    {
                        var top = c.Is(s[0], "INSERT") && c.Is(s[0] + 1, "INTO") ? s[0] + 2 : s[0] + 1;
                        if (c.Is(top, "TOP") && c.IsKind(top + 1, TokenKind.Number))
                        {
                            var number = t[top + 1];
                            c.Add(CodeIssueKind.ST006, t[top], "TOP without parentheses is the old style for " + head + "; write TOP (" + number.Text + ").", new CodeFix(number.Start, number.End - number.Start, "(" + number.Text + ")"));
                        }
                    }
                }
            }

            if (c.On(CodeIssueKind.ST003))
            {
                foreach (var m in c.Modules.Where(m => m.Kind == "PROC" && m.Body >= 0 && m.Body < m.End))
                {
                    if (!c.Is(m.Body, "BEGIN") && !c.Is(m.Body, "EXTERNAL"))
                    {
                        c.Add(CodeIssueKind.ST003, t[m.Body], "The body of the procedure is not enclosed in BEGIN ... END.");
                    }
                }
            }

            if (c.On(CodeIssueKind.ST008))
            {
                for (var i = 0; i < t.Count; i++)
                {
                    if ((c.Is(i, "EXEC") || c.Is(i, "EXECUTE")) && c.StatementAt(i)[0] == i)
                    {
                        PositionalArguments(c, i);
                    }
                }
            }

            if (c.On(CodeIssueKind.ST010))
            {
                SourcesWithoutAlias(c);
            }
        }

        private static void PositionalArguments(RuleContext c, int exec)
        {
            var t = c.T;
            var j = exec + 1;
            if (c.IsKind(j, TokenKind.Variable) && c.IsOp(j + 1, "="))
            {
                j += 2;
            }

            if (j >= t.Count || !t[j].IsNamePart || (t[j].Kind == TokenKind.Word && Sql.Keywords.IsReserved(t[j].Text)))
            {
                return;
            }

            while (c.IsKind(j + 1, TokenKind.Dot) && j + 2 < t.Count)
            {
                j += 2;
            }

            if (t[j].Text.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var end = c.StatementAt(exec)[1];
            var start = j + 1;
            for (var i = j + 1; i <= end; i++)
            {
                if (i < end && !(c.Depth[i] == c.Depth[exec] && c.IsKind(i, TokenKind.Comma)))
                {
                    continue;
                }

                if (i > start && !(t[start].Kind == TokenKind.Variable && c.IsOp(start + 1, "=")))
                {
                    c.Add(CodeIssueKind.ST008, t[start], "The argument has no parameter name; write @parameter = value.");
                    return;
                }

                start = i + 1;
            }
        }

        private static void SourcesWithoutAlias(RuleContext c)
        {
            var t = c.T;
            for (var f = 0; f < t.Count; f++)
            {
                if (!c.Is(f, "FROM"))
                {
                    continue;
                }

                var end = c.ListEnd(f, "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "OPTION", "FOR");
                var depth = c.Depth[f];
                var withoutAlias = new List<int>();
                var endOf = new Dictionary<int, int>();
                var count = 0;
                var i = f + 1;
                while (i < end)
                {
                    if (c.Depth[i] != depth)
                    {
                        i++;
                        continue;
                    }

                    var starts = c.Is(i - 1, "FROM") || c.IsKind(i - 1, TokenKind.Comma) || c.Is(i - 1, "JOIN") || c.Is(i - 1, "APPLY");
                    if (!starts)
                    {
                        i++;
                        continue;
                    }

                    count++;
                    if (!t[i].IsNamePart || (t[i].Kind == TokenKind.Word && Sql.Keywords.IsReserved(t[i].Text)))
                    {
                        i++;
                        continue;
                    }

                    var k = i;
                    while (c.IsKind(k + 1, TokenKind.Dot) && k + 2 < end)
                    {
                        k += 2;
                    }

                    if (c.IsKind(k + 1, TokenKind.OpenParen))
                    {
                        i = k + 1;
                        continue;
                    }

                    var hasAlias = c.Is(k + 1, "AS") || (k + 1 < end && t[k + 1].IsNamePart && !Sql.Keywords.CannotBeAlias(t[k + 1].Text) && !c.Is(k + 1, "WITH"));
                    if (!hasAlias && !t[i].Text.StartsWith("#", StringComparison.Ordinal))
                    {
                        withoutAlias.Add(i);
                        endOf[i] = k;
                    }

                    i = k + 1;
                }

                if (count > 1)
                {
                    foreach (var source in withoutAlias)
                    {
                        c.Add(CodeIssueKind.ST010, t[source].Start, t[endOf[source]].End - t[source].Start, "The table " + t[endOf[source]].Text + " has no alias in a query with more than one source.");
                    }
                }
            }
        }
    }
}
