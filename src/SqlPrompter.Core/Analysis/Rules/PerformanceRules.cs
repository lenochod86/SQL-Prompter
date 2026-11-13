using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Analysis
{
    internal static partial class CatalogRules
    {
        private static readonly HashSet<string> JoinHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LOOP", "HASH", "MERGE", "REMOTE", "REDUCE", "REPLICATE", "REDISTRIBUTE" };

        private static readonly HashSet<string> RecompilingSetOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ANSI_NULLS", "ANSI_PADDING", "ANSI_WARNINGS", "CONCAT_NULL_YIELDS_NULL", "ARITHABORT", "NUMERIC_ROUNDABORT", "ANSI_DEFAULTS", "ANSI_NULL_DFLT_ON", "ANSI_NULL_DFLT_OFF",
        };

        private static readonly HashSet<string> DataStatements = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE" };

        private static void Performance(RuleContext c)
        {
            var t = c.T;
            var cteNames = c.On(CodeIssueKind.PE002) ? CteNames(c) : null;
            for (var i = 0; i < t.Count; i++)
            {
                var token = t[i];
                if (token.Kind != TokenKind.Word)
                {
                    continue;
                }

                var statementStart = c.StatementAt(i)[0] == i;
                switch (token.Upper)
                {
                    case "INTO":
                        if (c.On(CodeIssueKind.PE003) && !c.Is(i - 1, "INSERT") && !c.Is(i - 1, "MERGE") && !c.Is(i - 1, "FETCH") && IsSelectInto(c, i) && i + 1 < t.Count && t[i + 1].Kind != TokenKind.Variable)
                        {
                            c.Add(CodeIssueKind.PE003, token, "SELECT INTO creates the table with column types taken from the query and takes schema locks; create the table first and use INSERT.");
                        }

                        break;
                    case "WITH":
                        if (c.On(CodeIssueKind.PE004) || c.On(CodeIssueKind.PE006))
                        {
                            TableHintList(c, i);
                        }

                        if (c.On(CodeIssueKind.PE021) && c.Is(i + 1, "RECOMPILE") && !c.IsKind(i + 1, TokenKind.OpenParen))
                        {
                            c.Add(CodeIssueKind.PE021, t[i + 1], "WITH RECOMPILE compiles the plan at every execution.");
                        }

                        break;
                    case "JOIN":
                        if (c.On(CodeIssueKind.PE005) && i > 0 && t[i - 1].Kind == TokenKind.Word && JoinHints.Contains(t[i - 1].Text) && (c.Is(i - 2, "INNER") || c.Is(i - 2, "LEFT") || c.Is(i - 2, "RIGHT") || c.Is(i - 2, "FULL") || c.Is(i - 2, "OUTER") || !t[i - 1].IsWord("MERGE")))
                        {
                            c.Add(CodeIssueKind.PE005, t[i - 1], "The " + t[i - 1].Text.ToUpperInvariant() + " join hint overrides the optimizer.");
                        }

                        if (c.On(CodeIssueKind.PE002))
                        {
                            UnqualifiedSource(c, i, cteNames);
                        }

                        break;
                    case "OPTION":
                        if (c.On(CodeIssueKind.PE007) && c.IsKind(i + 1, TokenKind.OpenParen))
                        {
                            c.Add(CodeIssueKind.PE007, token, "A query hint overrides the optimizer.");
                        }

                        break;
                    case "SET":
                        if (statementStart)
                        {
                            SetStatement(c, i);
                        }

                        break;
                    case "FROM":
                        if (c.On(CodeIssueKind.PE002))
                        {
                            UnqualifiedSource(c, i, cteNames);
                        }

                        break;
                    case "UPDATE":
                    case "DELETE":
                        if (c.On(CodeIssueKind.PE002) && statementStart)
                        {
                            UnqualifiedTarget(c, i, cteNames);
                        }

                        break;
                    case "EXEC":
                    case "EXECUTE":
                        if (c.On(CodeIssueKind.PE001) && statementStart)
                        {
                            ProcedureWithoutSchema(c, i);
                        }

                        break;
                    case "IN":
                        if (c.On(CodeIssueKind.PE019) && c.IsKind(i + 1, TokenKind.OpenParen) && c.Is(i + 2, "SELECT") && !c.Is(i - 1, "NOT"))
                        {
                            c.Add(CodeIssueKind.PE019, token, "IN (SELECT ...) with a large subquery; EXISTS can stop at the first match.");
                        }

                        break;
                    case "PRINT":
                        if (c.On(CodeIssueKind.PE011) && statementStart && c.ModuleAt(i)?.Kind == "TRIGGER")
                        {
                            c.Add(CodeIssueKind.PE011, token, "PRINT in a trigger sends a message to every client that fires it.");
                        }

                        break;
                    case "CREATE":
                    case "ALTER":
                        if (c.On(CodeIssueKind.PE023) && c.Is(i + 1, "TABLE"))
                        {
                            DdlWithoutSchema(c, i + 2);
                        }

                        break;
                }

            }

            if (c.On(CodeIssueKind.PE013))
            {
                CountInsteadOfExists(c);
            }

            if (c.On(CodeIssueKind.PE010))
            {
                DdlAfterDml(c);
            }

            if (c.On(CodeIssueKind.PE017))
            {
                ConstantFunctions(c);
            }

            if (c.On(CodeIssueKind.PE020))
            {
                InsertWithOrderBy(c);
            }
        }

        private static bool IsSelectInto(RuleContext c, int into)
        {
            var s = c.StatementAt(into);
            var head = c.UpperAt(s[0]);
            return head == "SELECT" || head == "WITH";
        }

        private static void TableHintList(RuleContext c, int with)
        {
            if (!c.IsKind(with + 1, TokenKind.OpenParen) || with == 0)
            {
                return;
            }

            // WITH (hints) after a table source; WITH (CTE) is "WITH name AS", never "WITH (".
            var close = c.Matching(with + 1);
            if (close < 0)
            {
                return;
            }

            var first = with + 2;
            if (!c.IsKind(first, TokenKind.Word) || !TableHints.Contains(c.T[first].Text))
            {
                return;
            }

            var hasIndex = false;
            for (var i = first; i < close; i++)
            {
                if (c.Is(i, "INDEX") && (c.IsKind(i + 1, TokenKind.OpenParen) || c.IsOp(i + 1, "=")))
                {
                    hasIndex = true;
                }
            }

            if (hasIndex && c.On(CodeIssueKind.PE004))
            {
                var index = Enumerable.Range(first, close - first).First(i => c.Is(i, "INDEX"));
                c.Add(CodeIssueKind.PE004, c.T[index], "An INDEX hint fixes the plan to an index that can be dropped or become a bad choice.");
            }

            if (c.On(CodeIssueKind.PE006))
            {
                c.Add(CodeIssueKind.PE006, c.T[with], "A table hint overrides the locking and the plan of SQL Server.");
            }
        }

        private static void SetStatement(RuleContext c, int set)
        {
            var option = c.UpperAt(set + 1);
            if (option == null)
            {
                return;
            }

            if (option == "NOCOUNT" && c.Is(set + 2, "OFF") && c.On(CodeIssueKind.PE008))
            {
                c.Add(CodeIssueKind.PE008, c.T[set + 1].Start, c.T[set + 2].End - c.T[set + 1].Start, "SET NOCOUNT OFF sends a row count to the client for every statement.");
            }
            else if (option == "FORCEPLAN" && c.On(CodeIssueKind.PE014))
            {
                c.Add(CodeIssueKind.PE014, c.T[set + 1], "SET FORCEPLAN forces the order of the joins as written and disables the optimizer.");
            }
            else if (c.On(CodeIssueKind.PE012) && RecompilingSetOptions.Contains(option) && c.ModuleAt(set) != null)
            {
                c.Add(CodeIssueKind.PE012, c.T[set + 1], "SET " + option + " inside a module recompiles it at every execution.");
            }
        }

        private static HashSet<string> CteNames(RuleContext c)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i + 2 < c.T.Count; i++)
            {
                if (c.T[i].IsNamePart && (c.Is(i - 1, "WITH") || c.IsKind(i - 1, TokenKind.Comma)) && (c.Is(i + 1, "AS") || c.IsKind(i + 1, TokenKind.OpenParen)))
                {
                    names.Add(c.T[i].IdentifierValue);
                }
            }

            return names;
        }

        private static int SourceAfter(RuleContext c, int keyword)
        {
            var j = keyword + 1;
            return j < c.T.Count && c.T[j].IsNamePart && !c.IsKind(j + 1, TokenKind.Dot) && !c.IsKind(j + 1, TokenKind.OpenParen) && c.T[j].Kind == TokenKind.Word && !Sql.Keywords.IsReserved(c.T[j].Text) && !c.T[j].Text.StartsWith("#", StringComparison.Ordinal)
                ? j
                : c.T[Math.Min(j, c.T.Count - 1)].Kind == TokenKind.QuotedIdentifier && !c.IsKind(j + 1, TokenKind.Dot) && !c.IsKind(j + 1, TokenKind.OpenParen) ? j : -1;
        }

        private static void UnqualifiedSource(RuleContext c, int keyword, HashSet<string> cteNames)
        {
            // FROM a, b JOIN c: every source of the clause; the loop stops at the next clause.
            var j = SourceAfter(c, keyword);
            var from = c.Is(keyword, "FROM");
            while (j >= 0)
            {
                ReportUnqualified(c, j, cteNames);
                if (!from)
                {
                    return;
                }

                // Skip the alias, then continue after a comma.
                var k = j + 1;
                if (c.Is(k, "AS"))
                {
                    k += 2;
                }
                else if (k < c.T.Count && c.T[k].IsNamePart && !Sql.Keywords.CannotBeAlias(c.T[k].Text))
                {
                    k++;
                }

                if (c.IsKind(k, TokenKind.Comma) && c.Depth[k] == c.Depth[keyword])
                {
                    j = SourceAfter(c, k);
                }
                else
                {
                    return;
                }
            }
        }

        private static void ReportUnqualified(RuleContext c, int name, HashSet<string> cteNames)
        {
            var token = c.T[name];
            if (cteNames != null && cteNames.Contains(token.IdentifierValue))
            {
                return;
            }

            CodeFix fix = null;
            var found = c.Metadata?.FindObject(null, token.IdentifierValue);
            if (found != null && !string.IsNullOrEmpty(found.Schema))
            {
                fix = new CodeFix(token.Start, 0, found.Schema + ".");
            }

            c.Add(CodeIssueKind.PE002, token, "The schema of " + token.Text + " is not specified; the name is resolved for every user separately.", fix);
        }

        private static void UnqualifiedTarget(RuleContext c, int keyword, HashSet<string> cteNames)
        {
            var s = c.StatementAt(keyword);
            var j = keyword + 1;
            if (c.Is(keyword, "DELETE") && c.Is(j, "FROM"))
            {
                return; // the source after FROM is reported with the other sources
            }

            if (c.Is(keyword, "UPDATE") && c.Is(j, "TOP"))
            {
                return;
            }

            // With a FROM clause the name can be an alias of it.
            for (var i = j; i < s[1]; i++)
            {
                if (c.Depth[i] == c.Depth[keyword] && c.Is(i, "FROM"))
                {
                    return;
                }
            }

            if (j < c.T.Count && c.T[j].IsNamePart && c.T[j].Kind == TokenKind.Word && !Sql.Keywords.IsReserved(c.T[j].Text) && !c.IsKind(j + 1, TokenKind.Dot) && !c.IsKind(j + 1, TokenKind.OpenParen) && !c.T[j].Text.StartsWith("#", StringComparison.Ordinal))
            {
                ReportUnqualified(c, j, cteNames);
            }
        }

        private static void ProcedureWithoutSchema(RuleContext c, int exec)
        {
            var j = exec + 1;
            if (c.IsKind(j, TokenKind.Variable) && c.IsOp(j + 1, "="))
            {
                j += 2;
            }

            if (j >= c.T.Count || c.T[j].Kind != TokenKind.Word || c.IsKind(j + 1, TokenKind.Dot) || c.IsKind(j + 1, TokenKind.OpenParen) || Sql.Keywords.IsReserved(c.T[j].Text))
            {
                return;
            }

            var token = c.T[j];
            if (token.Text.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) || token.Text.StartsWith("xp_", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CodeFix fix = null;
            var procedure = c.Metadata?.Procedures.FirstOrDefault(p => string.Equals(p.Name, token.Text, StringComparison.OrdinalIgnoreCase) && string.Equals(p.Schema, c.Metadata.DefaultSchema, StringComparison.OrdinalIgnoreCase)) ??
                c.Metadata?.Procedures.FirstOrDefault(p => string.Equals(p.Name, token.Text, StringComparison.OrdinalIgnoreCase));
            if (procedure != null)
            {
                fix = new CodeFix(token.Start, 0, procedure.Schema + ".");
            }

            c.Add(CodeIssueKind.PE001, token, "The schema of the procedure " + token.Text + " is not specified; the name is resolved for every user separately.", fix);
        }

        private static void DdlWithoutSchema(RuleContext c, int name)
        {
            if (name >= c.T.Count || c.T[name].Kind != TokenKind.Word || c.IsKind(name + 1, TokenKind.Dot) || c.T[name].Text.StartsWith("#", StringComparison.Ordinal))
            {
                return;
            }

            var schema = c.Metadata?.DefaultSchema;
            c.Add(CodeIssueKind.PE023, c.T[name], "The schema of the table " + c.T[name].Text + " is not specified; it is created in the default schema of the user.",
                string.IsNullOrEmpty(schema) ? null : new CodeFix(c.T[name].Start, 0, schema + "."));
        }

        /// <summary>IF (SELECT COUNT(*) FROM ...) &gt; 0: EXISTS stops at the first row (the repair rewrites the plain forms).</summary>
        private static void CountInsteadOfExists(RuleContext c)
        {
            var t = c.T;
            for (var i = 0; i + 6 < t.Count; i++)
            {
                if (!(t[i].Kind == TokenKind.OpenParen && c.Is(i + 1, "SELECT") && (c.Is(i + 2, "COUNT") || c.Is(i + 2, "COUNT_BIG")) && c.IsKind(i + 3, TokenKind.OpenParen)))
                {
                    continue;
                }

                var countClose = c.Matching(i + 3);
                var close = c.Matching(i);
                if (countClose < 0 || close < 0 || countClose >= close || !(c.IsOp(i + 4, "*") || c.IsKind(i + 4, TokenKind.Number)) || countClose != i + 5 || !c.Is(countClose + 1, "FROM"))
                {
                    continue;
                }

                // > 0, >= 1, <> 0, != 0 mean "exists"; = 0 and < 1 mean "does not exist".
                var op = close + 1;
                var text = string.Empty;
                var k = op;
                while (c.IsKind(k, TokenKind.Operator) && k < op + 2 && (k == op || c.Touches(k - 1, k)))
                {
                    text += t[k].Text;
                    k++;
                }

                if (!c.IsKind(k, TokenKind.Number))
                {
                    continue;
                }

                var number = t[k].Text;
                bool? exists = null;
                if ((text == ">" && number == "0") || (text == ">=" && number == "1") || (text == "<>" && number == "0") || (text == "!=" && number == "0"))
                {
                    exists = true;
                }
                else if ((text == "=" && number == "0") || (text == "<" && number == "1") || (text == "<=" && number == "0"))
                {
                    exists = false;
                }

                if (exists == null)
                {
                    continue;
                }

                var rest = c.Script.Substring(t[countClose].End, t[close].End - t[countClose].End);
                var replacement = (exists.Value ? string.Empty : "NOT ") + "EXISTS (SELECT 1" + rest;
                c.Add(CodeIssueKind.PE013, t[i + 2], "COUNT counts every row only to compare the number with " + number + "; use " + (exists.Value ? string.Empty : "NOT ") + "EXISTS.",
                    new CodeFix(t[i].Start, t[k].End - t[i].Start, replacement));
            }
        }

        private static void DdlAfterDml(RuleContext c)
        {
            foreach (var m in c.Modules.Where(m => (m.Kind == "PROC" || m.Kind == "TRIGGER") && m.Body >= 0))
            {
                var seenData = false;
                foreach (var s in c.Statements.Where(s => s[0] >= m.Body && s[0] < m.End))
                {
                    var head = c.UpperAt(s[0]);
                    if (DataStatements.Contains(head ?? string.Empty))
                    {
                        seenData = true;
                    }
                    else if (seenData && (head == "CREATE" || head == "ALTER" || head == "DROP") && c.Is(s[0] + 1, "TABLE"))
                    {
                        c.Add(CodeIssueKind.PE010, c.T[s[0]], "A " + head + " TABLE after data statements recompiles the " + ModuleName(m.Kind) + "; create the tables at its beginning.");
                    }
                }
            }
        }

        private static void ConstantFunctions(RuleContext c)
        {
            var conditions = CodeAnalyzer.ConditionMap(c.T);
            for (var i = 0; i + 3 < c.T.Count; i++)
            {
                if (!conditions[i] || !c.T[i].IsNamePart || !c.IsKind(i + 1, TokenKind.Dot) || !c.T[i + 2].IsNamePart || !c.IsKind(i + 3, TokenKind.OpenParen) || c.IsKind(i - 1, TokenKind.Dot))
                {
                    continue;
                }

                if (string.Equals(c.T[i].Text, "sys", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var close = c.Matching(i + 3);
                if (close < 0)
                {
                    continue;
                }

                var constant = true;
                for (var k = i + 4; k < close; k++)
                {
                    if (!(c.T[k].Kind == TokenKind.Number || c.T[k].Kind == TokenKind.String || c.T[k].Kind == TokenKind.Comma || c.Is(k, "NULL")))
                    {
                        constant = false;
                        break;
                    }
                }

                if (constant)
                {
                    c.Add(CodeIssueKind.PE017, c.T[i].Start, c.T[close].End - c.T[i].Start, "The function " + c.T[i].Text + "." + c.T[i + 2].Text + " has constant arguments but is evaluated for every row; assign its result to a variable first.");
                }

                i = close;
            }
        }

        private static void InsertWithOrderBy(RuleContext c)
        {
            foreach (var s in c.Statements)
            {
                if (!c.Is(s[0], "INSERT"))
                {
                    continue;
                }

                var top = false;
                for (var i = s[0]; i < s[1]; i++)
                {
                    if (c.Depth[i] == 0 && (c.Is(i, "TOP") || c.Is(i, "OFFSET")))
                    {
                        top = true;
                    }
                }

                for (var i = s[0]; i < s[1] && !top; i++)
                {
                    if (c.Depth[i] == 0 && c.Is(i, "ORDER") && c.Is(i + 1, "BY"))
                    {
                        c.Add(CodeIssueKind.PE020, c.T[i], "The order of the rows inserted by INSERT ... SELECT ... ORDER BY is not guaranteed in the table.");
                    }
                }
            }
        }
    }
}
