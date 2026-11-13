using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    internal static partial class CatalogRules
    {
        /// <summary>A cursor declared in the script (DECLARE name CURSOR ... FOR select, or a variable of the type CURSOR).</summary>
        private sealed class CursorInfo
        {
            public string Name;
            public int NameIndex;
            public int CursorWord;
            public bool IsVariable;
            public int Batch;
            public HashSet<string> Options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>The select list of the cursor (first and last token), or -1.</summary>
            public int SelectFrom = -1;
            public int SelectTo = -1;
        }

        private static List<CursorInfo> Cursors(RuleContext c)
        {
            if (c.CursorCache != null)
            {
                return (List<CursorInfo>)c.CursorCache;
            }

            var result = new List<CursorInfo>();
            var t = c.T;
            for (var i = 0; i < t.Count; i++)
            {
                CursorInfo info = null;
                if (c.Is(i, "DECLARE") && i + 2 < t.Count && (t[i + 1].IsNamePart || t[i + 1].Kind == TokenKind.Variable))
                {
                    var k = i + 2;
                    while (c.Is(k, "INSENSITIVE") || c.Is(k, "SCROLL"))
                    {
                        k++;
                    }

                    if (c.Is(k, "CURSOR"))
                    {
                        info = new CursorInfo { Name = t[i + 1].Text, NameIndex = i + 1, CursorWord = k, IsVariable = t[i + 1].Kind == TokenKind.Variable };
                        for (var o = i + 2; o < k; o++)
                        {
                            info.Options.Add(t[o].Text);
                        }
                    }
                }
                else if (t[i].Kind == TokenKind.Variable && c.Is(i + 1, "CURSOR") && !c.Is(i - 1, "DECLARE"))
                {
                    // A cursor parameter or a column of a table type: it exists, nothing is known about it.
                    info = new CursorInfo { Name = t[i].Text, NameIndex = i, CursorWord = i + 1, IsVariable = true };
                }

                if (info == null)
                {
                    continue;
                }

                info.Batch = c.BatchStart(i);
                var j = info.CursorWord + 1;
                while (j < t.Count && !c.Is(j, "FOR") && !c.IsStatementStart(j) && !t[j].IsWord("GO") && t[j].Kind != TokenKind.Semicolon && t[j].Kind != TokenKind.Comma && t[j].Kind != TokenKind.CloseParen)
                {
                    info.Options.Add(t[j].Text);
                    j++;
                }

                if (c.Is(j, "FOR"))
                {
                    var end = c.StatementAt(j)[1];
                    var from = j + 1;
                    var to = end;
                    for (var k = from; k < end; k++)
                    {
                        if (c.Depth[k] == c.Depth[j] && c.Is(k, "FOR"))
                        {
                            if (c.Is(k + 1, "READ") && (c.Is(k + 2, "ONLY") || c.Is(k + 2, "ONLY")))
                            {
                                info.Options.Add("READ_ONLY");
                            }
                            else if (c.Is(k + 1, "UPDATE"))
                            {
                                info.Options.Add("UPDATE");
                            }

                            to = k;
                            break;
                        }
                    }

                    info.SelectFrom = from;
                    info.SelectTo = to;
                }

                result.Add(info);
            }

            c.CursorCache = result;
            return result;
        }

        private static int ListItems(RuleContext c, int from, int to)
        {
            // The select list of a cursor: -1 when it has a star (the count is unknown).
            var select = from;
            while (select < to && !c.Is(select, "SELECT"))
            {
                select++;
            }

            if (select >= to)
            {
                return -1;
            }

            var end = c.ListEnd(select, RuleContext.SelectStops);
            var depth = c.Depth[select];
            var count = 1;
            for (var i = select + 1; i < end && i < to; i++)
            {
                if (c.Depth[i] == depth && c.IsOp(i, "*") && (c.IsKind(i - 1, TokenKind.Comma) || c.Is(i - 1, "SELECT") || c.IsKind(i - 1, TokenKind.Dot)))
                {
                    return -1;
                }

                if (c.Depth[i] == depth && c.IsKind(i, TokenKind.Comma))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>The token that names the cursor in FETCH, OPEN, CLOSE, DEALLOCATE (-1 when it is not there).</summary>
        private static int CursorNameAfter(RuleContext c, int keyword)
        {
            var j = keyword + 1;
            if (c.Is(keyword, "FETCH"))
            {
                if (c.Is(j, "NEXT") || c.Is(j, "PRIOR") || c.Is(j, "FIRST") || c.Is(j, "LAST"))
                {
                    j++;
                }
                else if (c.Is(j, "ABSOLUTE") || c.Is(j, "RELATIVE"))
                {
                    j += 2;
                }

                if (c.Is(j, "FROM"))
                {
                    j++;
                }
            }

            if (c.Is(j, "GLOBAL"))
            {
                j++;
            }

            return j < c.T.Count && (c.T[j].IsNamePart || c.T[j].Kind == TokenKind.Variable) ? j : -1;
        }

        private static void Execution(RuleContext c)
        {
            var t = c.T;
            var cursorRules = c.On(CodeIssueKind.EI011) || c.On(CodeIssueKind.EI012) || c.On(CodeIssueKind.EI013) || c.On(CodeIssueKind.EI015) || c.On(CodeIssueKind.EI021) || c.On(CodeIssueKind.EI023) ||
                c.On(CodeIssueKind.PE015) || c.On(CodeIssueKind.PE016) || c.On(CodeIssueKind.PE018) || c.On(CodeIssueKind.ST007) || c.On(CodeIssueKind.BP015);
            if (cursorRules)
            {
                CursorRules(c);
            }

            for (var i = 0; i < t.Count; i++)
            {
                var token = t[i];
                if (c.On(CodeIssueKind.EI003) && token.Kind == TokenKind.OpenParen && c.Is(i + 1, "SELECT"))
                {
                    ScalarSubquery(c, i);
                }

                if (token.Kind != TokenKind.Word)
                {
                    continue;
                }

                if (token.Upper == "ISNUMERIC" && c.On(CodeIssueKind.EI029) && c.IsKind(i + 1, TokenKind.OpenParen))
                {
                    c.Add(CodeIssueKind.EI029, token, "ISNUMERIC returns 1 for strings that cannot be converted to a number (e.g. '$' or ','); use TRY_CONVERT or TRY_CAST.");
                }
                else if ((token.Upper == "EXEC" || token.Upper == "EXECUTE") && c.On(CodeIssueKind.EI016) && c.StatementAt(i)[0] == i)
                {
                    OtherDatabaseProcedure(c, i);
                }
            }

            if (c.On(CodeIssueKind.EI024))
            {
                foreach (var m in c.Modules.Where(m => m.Kind == "PROC" && m.Name >= 0 && c.T[m.Name].Text.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)))
                {
                    c.Add(CodeIssueKind.EI024, c.T[m.Name], "The name " + c.T[m.Name].Text + " starts with sp_: SQL Server looks for it in master first and a system procedure of the same name wins.");
                }
            }

            if (c.On(CodeIssueKind.EI028))
            {
                AddNotNullColumn(c);
            }

            if (c.On(CodeIssueKind.EI030))
            {
                OrderByInView(c);
            }
        }

        private static void CursorRules(RuleContext c)
        {
            var t = c.T;
            var cursors = Cursors(c);
            if (cursors.Count == 0 && !t.Any(x => x.IsWord("FETCH") || x.IsWord("CLOSE") || x.IsWord("DEALLOCATE") || x.IsWord("OPEN")))
            {
                return;
            }

            CursorInfo Find(string name, int at)
            {
                var batch = c.BatchStart(at);
                return cursors.FirstOrDefault(x => x.Batch == batch && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            var currentOf = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 2 < t.Count; i++)
            {
                if (c.Is(i, "CURRENT") && c.Is(i + 1, "OF"))
                {
                    currentOf.Add(t[i + 2].Text);
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cursor in cursors.Where(x => !x.IsVariable || c.Is(x.NameIndex - 1, "DECLARE")))
            {
                if (!seen.Add(cursor.Batch + "|" + cursor.Name) && c.On(CodeIssueKind.ST007))
                {
                    c.Add(CodeIssueKind.ST007, t[cursor.NameIndex], "The cursor name " + cursor.Name + " is already declared in this batch.");
                }

                if (cursor.IsVariable)
                {
                    continue;
                }

                if (c.On(CodeIssueKind.PE018) && !cursor.Options.Overlaps(new[] { "READ_ONLY", "STATIC", "FAST_FORWARD", "INSENSITIVE", "UPDATE", "KEYSET", "DYNAMIC" }) && !currentOf.Contains(cursor.Name))
                {
                    c.Add(CodeIssueKind.PE018, t[cursor.CursorWord], "The cursor " + cursor.Name + " is not declared read-only; add FOR READ_ONLY (or STATIC / FAST_FORWARD) if it is not updated.");
                }
            }

            if (c.On(CodeIssueKind.PE015))
            {
                CursorScroll(c);
            }

            var opened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < t.Count; i++)
            {
                if (t[i].Kind != TokenKind.Word || c.StatementAt(i)[0] != i)
                {
                    continue;
                }

                var upper = t[i].Upper;
                if (upper != "FETCH" && upper != "OPEN" && upper != "CLOSE" && upper != "DEALLOCATE")
                {
                    continue;
                }

                var nameIndex = CursorNameAfter(c, i);
                if (nameIndex < 0)
                {
                    continue;
                }

                var name = t[nameIndex];
                var cursor = Find(name.Text, i);
                var key = c.BatchStart(i) + "|" + name.Text;
                var kind = upper == "FETCH" ? CodeIssueKind.EI011 : upper == "CLOSE" ? CodeIssueKind.EI012 : upper == "DEALLOCATE" ? CodeIssueKind.EI013 : (CodeIssueKind?)null;
                if (cursor == null)
                {
                    if (kind != null && c.On(kind.Value))
                    {
                        c.Add(kind.Value, name, upper + " of the cursor " + name.Text + ", which is not declared in this batch.");
                    }

                    continue;
                }

                if (upper == "OPEN")
                {
                    opened.Add(key);
                    if (c.On(CodeIssueKind.PE016) && !HasDeallocate(c, cursor))
                    {
                        c.Add(CodeIssueKind.PE016, name, "The cursor " + name.Text + " is opened but never deallocated in this batch.");
                    }
                }
                else if (upper == "CLOSE")
                {
                    if (c.On(CodeIssueKind.EI021) && !opened.Contains(key) && !cursor.IsVariable)
                    {
                        c.Add(CodeIssueKind.EI021, name, "CLOSE of the cursor " + name.Text + " that was not opened before.");
                    }
                }
                else if (upper == "FETCH")
                {
                    Fetch(c, i, nameIndex, cursor);
                }
            }

            if (c.On(CodeIssueKind.EI023))
            {
                for (var i = 0; i + 2 < t.Count; i++)
                {
                    if (c.Is(i, "CURRENT") && c.Is(i + 1, "OF") && c.Is(i - 1, "WHERE"))
                    {
                        var k = i + 2 < t.Count && c.Is(i + 2, "GLOBAL") ? i + 3 : i + 2;
                        var cursor = k < t.Count ? Find(t[k].Text, i) : null;
                        if (cursor != null && cursor.Options.Overlaps(new[] { "READ_ONLY", "STATIC", "FAST_FORWARD", "INSENSITIVE" }))
                        {
                            c.Add(CodeIssueKind.EI023, t[k], "WHERE CURRENT OF on the cursor " + t[k].Text + ", which is read-only.");
                        }
                    }
                }
            }
        }

        private static bool UsesCurrentOf(RuleContext c, CursorInfo cursor)
        {
            for (var i = 0; i + 2 < c.T.Count; i++)
            {
                if (c.Is(i, "CURRENT") && c.Is(i + 1, "OF") && string.Equals(c.T[i + 2].Text, cursor.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDeallocate(RuleContext c, CursorInfo cursor)
        {
            var end = c.BatchEnd(cursor.NameIndex);
            for (var i = cursor.Batch; i < end; i++)
            {
                if (c.Is(i, "DEALLOCATE"))
                {
                    var k = CursorNameAfter(c, i);
                    if (k >= 0 && string.Equals(c.T[k].Text, cursor.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void Fetch(RuleContext c, int fetch, int nameIndex, CursorInfo cursor)
        {
            var t = c.T;
            if (!c.On(CodeIssueKind.EI015) || cursor.SelectFrom < 0 || !c.Is(nameIndex + 1, "INTO"))
            {
                return;
            }

            var expected = ListItems(c, cursor.SelectFrom, cursor.SelectTo);
            if (expected < 0)
            {
                return;
            }

            var end = c.StatementAt(fetch)[1];
            var count = 1;
            for (var i = nameIndex + 2; i < end; i++)
            {
                if (c.Depth[i] == c.Depth[fetch] && c.IsKind(i, TokenKind.Comma))
                {
                    count++;
                }
            }

            if (count != expected)
            {
                c.Add(CodeIssueKind.EI015, t[nameIndex + 1], "FETCH INTO has " + count + " variable" + (count == 1 ? string.Empty : "s") + " but the select list of the cursor has " + expected + ".");
            }
        }

        private static void CursorScroll(RuleContext c)
        {
            // PE015: a cursor that is not forward-only although it is never fetched in another direction.
            foreach (var cursor in Cursors(c).Where(x => !x.IsVariable))
            {
                if (cursor.Options.Overlaps(new[] { "FORWARD_ONLY", "FAST_FORWARD" }))
                {
                    continue;
                }

                var end = c.BatchEnd(cursor.NameIndex);
                var scrolls = false;
                for (var i = cursor.Batch; i < end; i++)
                {
                    if (c.Is(i, "FETCH") && (c.Is(i + 1, "PRIOR") || c.Is(i + 1, "FIRST") || c.Is(i + 1, "LAST") || c.Is(i + 1, "ABSOLUTE") || c.Is(i + 1, "RELATIVE")))
                    {
                        var k = CursorNameAfter(c, i);
                        if (k >= 0 && string.Equals(c.T[k].Text, cursor.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            scrolls = true;
                        }
                    }
                }

                if (!scrolls)
                {
                    c.Add(CodeIssueKind.PE015, c.T[cursor.CursorWord], "The cursor " + cursor.Name + " is only fetched forward; declare it FORWARD_ONLY or FAST_FORWARD.");
                }
            }
        }

        private static void ScalarSubquery(RuleContext c, int open)
        {
            var previous = open > 0 ? c.T[open - 1] : null;
            if (previous == null)
            {
                return;
            }

            var comparison = previous.Kind == TokenKind.Operator && (previous.Text == "=" || previous.Text == "<" || previous.Text == ">");
            var inList = previous.Kind == TokenKind.Comma || previous.IsWord("SELECT");
            if (!comparison && !inList)
            {
                return;
            }

            var close = c.Matching(open);
            if (close < 0)
            {
                return;
            }

            for (var i = open + 2; i < close; i++)
            {
                if (c.Depth[i] != c.Depth[open] + 1)
                {
                    continue;
                }

                var word = c.T[i].Upper;
                if (word == "TOP" || word == "COUNT" || word == "SUM" || word == "MIN" || word == "MAX" || word == "AVG" || word == "STRING_AGG" || word == "COUNT_BIG" || word == "OFFSET")
                {
                    return;
                }

                if (word == "FOR" && c.Is(i + 1, "XML"))
                {
                    return;
                }
            }

            c.Add(CodeIssueKind.EI003, c.T[open + 1], "This subquery can return more than one row but is used as a single value; add TOP (1) or an aggregate, or use IN / EXISTS.");
        }

        private static void OtherDatabaseProcedure(RuleContext c, int exec)
        {
            var j = exec + 1;
            if (c.IsKind(j, TokenKind.Variable) && c.IsOp(j + 1, "="))
            {
                j += 2;
            }

            if (j >= c.T.Count || !c.T[j].IsNamePart)
            {
                return;
            }

            var parts = new List<int> { j };
            var k = j;
            while (c.IsKind(k + 1, TokenKind.Dot) && k + 2 < c.T.Count && (c.T[k + 2].IsNamePart || c.IsKind(k + 2, TokenKind.Dot)))
            {
                k += 2;
                parts.Add(k);
            }

            if (parts.Count == 3 && !c.T[parts[2]].Text.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) && !c.T[parts[2]].Text.StartsWith("xp_", StringComparison.OrdinalIgnoreCase))
            {
                c.Add(CodeIssueKind.EI016, c.T[j].Start, c.T[k].End - c.T[j].Start, "The procedure is in the database " + c.T[parts[0]].Text + ", a hard dependency on that database.");
            }
        }

        private static void AddNotNullColumn(RuleContext c)
        {
            var t = c.T;
            foreach (var s in c.Statements)
            {
                if (!c.Is(s[0], "ALTER") || !c.Is(s[0] + 1, "TABLE"))
                {
                    continue;
                }

                var add = -1;
                for (var i = s[0] + 2; i < s[1]; i++)
                {
                    if (c.Is(i, "ADD") && c.Depth[i] == 0)
                    {
                        add = i;
                        break;
                    }
                }

                if (add < 0)
                {
                    continue;
                }

                var start = add + 1;
                for (var i = add + 1; i <= s[1]; i++)
                {
                    if (i < s[1] && !(c.Depth[i] == 0 && c.IsKind(i, TokenKind.Comma)))
                    {
                        continue;
                    }

                    if (i > start && t[start].IsNamePart && !NotAColumnNameStarters.Contains(t[start].Text) && !c.Is(start + 1, "AS"))
                    {
                        var notNull = false;
                        var safe = false;
                        for (var k = start; k < i; k++)
                        {
                            if (c.Is(k, "NOT") && c.Is(k + 1, "NULL"))
                            {
                                notNull = true;
                            }
                            else if (c.Is(k, "DEFAULT") || c.Is(k, "IDENTITY") || c.Is(k, "ROWGUIDCOL") || c.Is(k, "TIMESTAMP") || c.Is(k, "ROWVERSION") || c.Is(k, "GENERATED"))
                            {
                                safe = true;
                            }
                        }

                        if (notNull && !safe)
                        {
                            c.Add(CodeIssueKind.EI028, t[start], "The NOT NULL column " + t[start].Text + " has no DEFAULT; adding it fails when the table has rows.");
                        }
                    }

                    start = i + 1;
                }
            }
        }

        private static void OrderByInView(RuleContext c)
        {
            foreach (var m in c.Modules)
            {
                if (m.Body < 0 || (m.Kind != "VIEW" && m.Kind != "FUNCTION"))
                {
                    continue;
                }

                var inline = m.Kind == "FUNCTION" && c.T.Skip(m.Start).Take(m.End - m.Start).Any(x => x.IsWord("RETURNS")) && ReturnsTable(c, m);
                if (m.Kind == "FUNCTION" && !inline)
                {
                    continue;
                }

                var level = m.Kind == "VIEW" ? 0 : 1;
                var hasTop = false;
                for (var i = m.Body; i < m.End; i++)
                {
                    if (c.Depth[i] == level && (c.Is(i, "TOP") || c.Is(i, "OFFSET") || c.Is(i, "FOR")))
                    {
                        hasTop = true;
                    }
                }

                for (var i = m.Body; i < m.End && !hasTop; i++)
                {
                    if (c.Depth[i] == level && c.Is(i, "ORDER") && c.Is(i + 1, "BY"))
                    {
                        c.Add(CodeIssueKind.EI030, c.T[i], (m.Kind == "VIEW" ? "A view" : "An inline function") + " cannot be ordered with ORDER BY unless it has TOP or OFFSET; the order is not guaranteed.");
                    }
                }
            }
        }

        private static bool ReturnsTable(RuleContext c, ModuleInfo m)
        {
            for (var i = m.Start; i < m.End - 1; i++)
            {
                if (c.Is(i, "RETURNS"))
                {
                    return c.Is(i + 1, "TABLE");
                }
            }

            return false;
        }
    }
}
