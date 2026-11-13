using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>Runs the rules of the catalog that are not part of the first set of <see cref="CodeAnalyzer"/>; each rule has its own switch (<see cref="AnalysisRules"/>).</summary>
    internal static partial class CatalogRules
    {
        public static void Run(string script, List<Lexing.Token> allTokens, List<Lexing.Token> tokens, Settings.AnalysisSettings settings, Model.IDatabaseMetadata metadata, List<CodeIssue> issues)
        {
            if (tokens.Count == 0 && allTokens.Count == 0)
            {
                return;
            }

            var ctx = new RuleContext(script, allTokens, tokens, settings, metadata, issues);
            BestPractice(ctx);
            Deprecated(ctx);
            Execution(ctx);
            Miscellaneous(ctx);
            Performance(ctx);
            ScriptRules(ctx);
            Style(ctx);
        }

        private static readonly HashSet<string> NotAColumnNameStarters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "INDEX", "PERIOD", "CLUSTERED", "NONCLUSTERED", "DEFAULT", "CONNECTION",
        };

        private static void BestPractice(RuleContext c)
        {
            var t = c.T;
            var wantsIndex = c.On(CodeIssueKind.BP001);
            var wantsOrder = c.On(CodeIssueKind.BP002);
            var wantsSmall = c.On(CodeIssueKind.BP009);
            var wantsCase = c.On(CodeIssueKind.BP012);
            var wantsExec = c.On(CodeIssueKind.BP013);
            var wantsMoney = c.On(CodeIssueKind.BP022);
            var wantsFloat = c.On(CodeIssueKind.BP023);
            var wantsVariant = c.On(CodeIssueKind.BP024);
            var wantsNullExpr = c.On(CodeIssueKind.EqualsNull);
            var conditions = wantsNullExpr ? CodeAnalyzer.ConditionMap(t) : null;
            for (var i = 0; i < t.Count; i++)
            {
                var token = t[i];
                if (token.Kind == TokenKind.Word)
                {
                    switch (token.Upper)
                    {
                        case "INDEX":
                            if (wantsIndex)
                            {
                                IndexType(c, i);
                            }

                            break;
                        case "ORDER":
                            if (wantsOrder && c.Is(i + 1, "BY"))
                            {
                                OrderByConstants(c, i + 2);
                            }

                            break;
                        case "CASE":
                            if (wantsCase)
                            {
                                CaseWithoutElse(c, i);
                            }

                            break;
                        case "EXEC":
                        case "EXECUTE":
                            if (wantsExec && c.IsKind(i + 1, TokenKind.OpenParen))
                            {
                                c.Add(CodeIssueKind.BP013, token, "EXECUTE ('...') runs dynamic SQL; sp_executesql with parameters is safer and its plans are reused.");
                            }

                            break;
                        case "VARCHAR":
                        case "NVARCHAR":
                        case "VARBINARY":
                            if (wantsSmall && c.IsKind(i + 1, TokenKind.OpenParen) && c.IsKind(i + 2, TokenKind.Number) && (t[i + 2].Text == "1" || t[i + 2].Text == "2") && c.IsKind(i + 3, TokenKind.CloseParen) && IsDeclaredType(c, i))
                            {
                                var fixedType = token.Upper == "VARCHAR" ? "char" : token.Upper == "NVARCHAR" ? "nchar" : "binary";
                                c.Add(CodeIssueKind.BP009, token.Start, t[i + 3].End - token.Start, token.Text + "(" + t[i + 2].Text + ") carries the overhead of a variable-length type; " + fixedType + "(" + t[i + 2].Text + ") is smaller and faster.");
                            }

                            break;
                        case "MONEY":
                        case "SMALLMONEY":
                            if (wantsMoney && c.IsTypePosition(i))
                            {
                                c.Add(CodeIssueKind.BP022, token, token.Text + " rounds intermediate results to four decimals; decimal is exact and portable.");
                            }

                            break;
                        case "FLOAT":
                        case "REAL":
                            if (wantsFloat && c.IsTypePosition(i))
                            {
                                c.Add(CodeIssueKind.BP023, token, token.Text + " is an approximate number; equal values may differ in the last digits. Use decimal where exact values matter.");
                            }

                            break;
                        case "SQL_VARIANT":
                            if (wantsVariant && c.IsTypePosition(i))
                            {
                                c.Add(CodeIssueKind.BP024, token, "sql_variant cannot be used in many indexes and comparisons and is badly supported by clients.");
                            }

                            break;
                        case "NULL":
                            if (wantsNullExpr)
                            {
                                NullInExpression(c, i, conditions);
                            }

                            break;
                    }
                }
            }

            if (c.On(CodeIssueKind.BP003))
            {
                SelectInTrigger(c);
            }

            if (c.On(CodeIssueKind.BP014))
            {
                Nullability(c);
            }

            if (c.On(CodeIssueKind.BP015))
            {
                CursorScope(c);
            }

            if (c.On(CodeIssueKind.BP016))
            {
                ReturnWithoutValue(c);
            }

            if (c.On(CodeIssueKind.BP017) || c.On(CodeIssueKind.BP018))
            {
                DeleteUpdateWithoutWhere(c);
            }
        }

        /// <summary>A type name in a declaration, not in a CAST or CONVERT (BP009).</summary>
        private static bool IsDeclaredType(RuleContext c, int i)
        {
            var owner = c.Owner[i];
            if (owner >= 0 && (c.T[owner].IsWord("CAST") || c.T[owner].IsWord("TRY_CAST") || c.T[owner].IsWord("CONVERT") || c.T[owner].IsWord("TRY_CONVERT")))
            {
                return false;
            }

            return c.IsTypePosition(i);
        }

        private static void IndexType(RuleContext c, int i)
        {
            // CREATE [UNIQUE] [CLUSTERED | NONCLUSTERED] INDEX; the columnstore, XML, spatial and selective XML indexes have their own defaults.
            var j = i - 1;
            if (c.Is(j, "UNIQUE"))
            {
                j--;
            }

            if (c.Is(j, "CLUSTERED") || c.Is(j, "NONCLUSTERED") || c.Is(j, "COLUMNSTORE") || c.Is(j, "XML") || c.Is(j, "SPATIAL") || c.Is(j, "SELECTIVE") || c.Is(j, "FULLTEXT"))
            {
                return;
            }

            if (!c.Is(j, "CREATE"))
            {
                return;
            }

            c.Add(CodeIssueKind.BP001, c.T[i], "The index type is not specified; write CLUSTERED or NONCLUSTERED (NONCLUSTERED is the default).", new CodeFix(c.T[i].Start, 0, "NONCLUSTERED "));
        }

        private static void OrderByConstants(RuleContext c, int from)
        {
            var depth = c.Depth[from - 1];
            var itemStart = from;
            for (var i = from; i <= c.T.Count; i++)
            {
                var end = i == c.T.Count || (c.Depth[i] == depth && (c.IsKind(i, TokenKind.Comma) || c.IsKind(i, TokenKind.Semicolon) || c.IsKind(i, TokenKind.CloseParen) ||
                    (c.T[i].Kind == TokenKind.Word && (c.T[i].Upper == "OFFSET" || c.T[i].Upper == "FOR" || c.T[i].Upper == "OPTION" || c.T[i].Upper == "UNION" || c.T[i].Upper == "EXCEPT" || c.T[i].Upper == "INTERSECT" || c.T[i].Upper == "GO")))) ||
                    (i < c.T.Count && c.Depth[i] < depth);
                if (!end)
                {
                    continue;
                }

                var last = i - 1;
                if (last >= itemStart && (c.Is(last, "ASC") || c.Is(last, "DESC")))
                {
                    last--;
                }

                if (last == itemStart && (c.T[itemStart].Kind == TokenKind.Number || c.T[itemStart].Kind == TokenKind.String))
                {
                    c.Add(CodeIssueKind.BP002, c.T[itemStart], "ORDER BY " + c.T[itemStart].Text + " sorts by a constant position or value; name the column so the order survives a change of the select list.");
                }

                if (i == c.T.Count || !c.IsKind(i, TokenKind.Comma) || c.Depth[i] != depth)
                {
                    return;
                }

                itemStart = i + 1;
            }
        }

        private static void CaseWithoutElse(RuleContext c, int caseIndex)
        {
            var depth = 0;
            for (var i = caseIndex; i < c.T.Count; i++)
            {
                if (c.T[i].IsWord("CASE"))
                {
                    depth++;
                }
                else if (c.T[i].IsWord("END"))
                {
                    depth--;
                    if (depth == 0)
                    {
                        c.Add(CodeIssueKind.BP012, c.T[caseIndex], "The CASE expression has no ELSE branch and returns NULL when no WHEN matches.");
                        return;
                    }
                }
                else if (c.T[i].IsWord("ELSE") && depth == 1)
                {
                    return;
                }
                else if (c.T[i].IsWord("GO") || c.T[i].Kind == TokenKind.Semicolon)
                {
                    return;
                }
            }
        }

        private static void NullInExpression(RuleContext c, int i, bool[] conditions)
        {
            var t = c.T;
            var previous = i > 0 ? t[i - 1] : null;
            var next = i + 1 < t.Count ? t[i + 1] : null;
            if (previous != null && previous.IsWord("IS"))
            {
                return;
            }

            if (previous != null && previous.IsWord("NOT") && i > 1 && t[i - 2].IsWord("IS"))
            {
                return;
            }

            string reason = null;
            if (previous != null && previous.Kind == TokenKind.Operator && (previous.Text == "+" || previous.Text == "*" || previous.Text == "/" || previous.Text == "%" || previous.Text == "-") && i > 1 && IsOperandEnd(t[i - 2]))
            {
                reason = "an expression with NULL is always NULL";
            }
            else if (next != null && next.Kind == TokenKind.Operator && (next.Text == "+" || next.Text == "*" || next.Text == "/" || next.Text == "%" || next.Text == "-") && i + 2 < t.Count && !(next.Text == "-" && t[i + 2].Text == "-"))
            {
                reason = "an expression with NULL is always NULL";
            }
            else if (conditions != null && conditions[i] && previous != null && previous.Kind == TokenKind.Operator && previous.Text == "=" && i > 1 && t[i - 2].Kind == TokenKind.Operator && (t[i - 2].Text == "<" || t[i - 2].Text == ">") && t[i - 2].End == previous.Start)
            {
                reason = "a comparison with NULL is never true";
            }
            else if (conditions != null && conditions[i] && previous != null && previous.Kind == TokenKind.Operator && (previous.Text == "<" || previous.Text == ">") && !(i > 1 && t[i - 2].Kind == TokenKind.Operator && t[i - 2].Text == "<"))
            {
                reason = "a comparison with NULL is never true";
            }
            else if (conditions != null && conditions[i] && next != null && next.Kind == TokenKind.Operator && (next.Text == "<" || next.Text == ">") && !(i + 2 < t.Count && t[i + 2].Text == ">" && next.Text == "<"))
            {
                reason = "a comparison with NULL is never true";
            }
            else if (InsideInList(c, i))
            {
                reason = "a value never equals NULL, so NULL in the list does not match anything (and NOT IN returns no rows)";
            }

            if (reason != null)
            {
                c.Add(CodeIssueKind.EqualsNull, t[i], "NULL here: " + reason + ". Use IS NULL / IS NOT NULL or ISNULL / COALESCE.");
            }
        }

        private static bool IsOperandEnd(Token token) =>
            token.Kind == TokenKind.Number || token.Kind == TokenKind.String || token.Kind == TokenKind.Variable || token.Kind == TokenKind.CloseParen ||
            token.Kind == TokenKind.QuotedIdentifier || (token.Kind == TokenKind.Word && !Sql.Keywords.IsReserved(token.Text));

        private static bool InsideInList(RuleContext c, int i)
        {
            var owner = c.Owner[i];
            if (owner < 0 || !c.T[owner].IsWord("IN"))
            {
                return false;
            }

            // IN (NULL) is flagged, a subquery is not a list.
            var open = owner + 1;
            return !c.Is(open + 1, "SELECT") && c.Depth[i] == c.Depth[open] + 1;
        }

        private static void SelectInTrigger(RuleContext c)
        {
            foreach (var module in c.Modules.Where(m => m.Kind == "TRIGGER" && m.Body >= 0))
            {
                foreach (var s in c.Statements.Where(s => s[0] >= module.Body && s[0] < module.End))
                {
                    if (!c.Is(s[0], "SELECT") || c.Depth[s[0]] != 0)
                    {
                        continue;
                    }

                    var assigns = c.IsKind(s[0] + 1, TokenKind.Variable) && c.T.Count > s[0] + 2 && (c.IsOp(s[0] + 2, "=") || (c.IsKind(s[0] + 2, TokenKind.Operator) && c.IsOp(s[0] + 3, "=")));
                    var into = false;
                    for (var i = s[0]; i < s[1]; i++)
                    {
                        if (c.Is(i, "INTO") && c.Depth[i] == 0)
                        {
                            into = true;
                            break;
                        }
                    }

                    if (!assigns && !into)
                    {
                        c.Add(CodeIssueKind.BP003, c.T[s[0]], "A SELECT in a trigger returns a result set to the client that fired it; assign to variables (SELECT @x = ...) or insert into a table.");
                    }
                }
            }
        }

        /// <summary>Columns of CREATE TABLE and DECLARE @t TABLE without NULL or NOT NULL.</summary>
        private static void NullIfUnspecified(RuleContext c, int open)
        {
            var close = c.Matching(open);
            if (close < 0)
            {
                return;
            }

            var depth = c.Depth[open] + 1;
            var start = open + 1;
            for (var i = open + 1; i <= close; i++)
            {
                if (i < close && !(c.Depth[i] == depth && c.IsKind(i, TokenKind.Comma)))
                {
                    continue;
                }

                if (i > start && c.T[start].IsNamePart && !NotAColumnNameStarters.Contains(c.T[start].Text) && !c.Is(start + 1, "AS"))
                {
                    var hasNull = false;
                    var implied = false;
                    for (var k = start; k < i; k++)
                    {
                        if (c.Is(k, "NULL"))
                        {
                            hasNull = true;
                        }
                        else if (c.Is(k, "IDENTITY") || c.Is(k, "PRIMARY") || c.Is(k, "ROWGUIDCOL") || c.Is(k, "TIMESTAMP") || c.Is(k, "ROWVERSION") || c.Is(k, "GENERATED"))
                        {
                            implied = true;
                        }
                    }

                    if (!hasNull && !implied)
                    {
                        c.Add(CodeIssueKind.BP014, c.T[start], "The column " + c.T[start].Text + " has neither NULL nor NOT NULL; the result depends on the ANSI_NULL_DFLT session settings.");
                    }
                }

                start = i + 1;
            }
        }

        private static void NullDefinitions(RuleContext c, Action<RuleContext, int> visitor)
        {
            for (var i = 0; i < c.T.Count; i++)
            {
                if (c.Is(i, "TABLE") && (c.Is(i - 1, "CREATE") || c.IsKind(i - 1, TokenKind.Variable)))
                {
                    // CREATE TABLE name (...) or DECLARE @t TABLE (...)
                    var j = i + 1;
                    while (j < c.T.Count && (c.T[j].IsNamePart || c.IsKind(j, TokenKind.Dot)))
                    {
                        j++;
                    }

                    if (c.IsKind(j, TokenKind.OpenParen))
                    {
                        visitor(c, j);
                    }
                }
            }
        }

        private static void Nullability(RuleContext c) => NullDefinitions(c, NullIfUnspecified);

        private static void CursorScope(RuleContext c)
        {
            foreach (var cursor in Cursors(c))
            {
                if (!cursor.IsVariable && !cursor.Options.Contains("LOCAL") && !cursor.Options.Contains("GLOBAL"))
                {
                    c.Add(CodeIssueKind.BP015, c.T[cursor.CursorWord], "The scope of the cursor " + cursor.Name + " is not specified; write LOCAL or GLOBAL.");
                }
            }
        }

        private static void ReturnWithoutValue(RuleContext c)
        {
            foreach (var module in c.Modules.Where(m => m.Kind == "PROC" && m.Body >= 0))
            {
                for (var i = module.Body; i < module.End; i++)
                {
                    if (!c.Is(i, "RETURN"))
                    {
                        continue;
                    }

                    var next = i + 1 < module.End ? c.T[i + 1] : null;
                    if (next == null || next.Kind == TokenKind.Semicolon || (next.Kind == TokenKind.Word && (next.Upper == "END" || next.Upper == "GO" || IsStatementKeyword(next.Upper))))
                    {
                        c.Add(CodeIssueKind.BP016, c.T[i], "RETURN without a value returns 0 implicitly; return a result code.", new CodeFix(c.T[i].End, 0, " 0"));
                    }
                }
            }
        }

        private static bool IsStatementKeyword(string upper) =>
            upper == "SELECT" || upper == "INSERT" || upper == "UPDATE" || upper == "DELETE" || upper == "SET" || upper == "DECLARE" || upper == "IF" || upper == "ELSE" || upper == "WHILE" ||
            upper == "BEGIN" || upper == "EXEC" || upper == "EXECUTE" || upper == "PRINT" || upper == "RETURN" || upper == "WITH" || upper == "RAISERROR" || upper == "THROW" ||
            upper == "COMMIT" || upper == "ROLLBACK" || upper == "FETCH" || upper == "CLOSE" || upper == "OPEN" || upper == "DEALLOCATE" || upper == "DROP" || upper == "CREATE" || upper == "TRUNCATE";

        private static void DeleteUpdateWithoutWhere(RuleContext c)
        {
            foreach (var s in c.Statements)
            {
                var head = c.UpperAt(s[0]);
                if (head != "DELETE" && head != "UPDATE" && head != "WITH")
                {
                    continue;
                }

                for (var i = s[0]; i < s[1]; i++)
                {
                    if (c.Depth[i] != 0 || !(c.Is(i, "DELETE") || c.Is(i, "UPDATE")) || c.Is(i - 1, "THEN") || c.Is(i - 1, "ON"))
                    {
                        continue;
                    }

                    var isDelete = c.Is(i, "DELETE");
                    if (c.Is(i + 1, "STATISTICS") || c.Is(i + 1, "TEXT"))
                    {
                        break;
                    }

                    var filtered = false;
                    for (var k = i + 1; k < s[1]; k++)
                    {
                        if (c.Depth[k] == 0 && (c.Is(k, "WHERE") || c.Is(k, "JOIN")))
                        {
                            filtered = true;
                            break;
                        }
                    }

                    var kind = isDelete ? CodeIssueKind.BP017 : CodeIssueKind.BP018;
                    if (!filtered && c.On(kind))
                    {
                        c.Add(kind, c.T[i], (isDelete ? "DELETE" : "UPDATE") + " without WHERE or JOIN " + (isDelete ? "removes" : "changes") + " every row of the table.");
                    }

                    break;
                }
            }
        }
    }
}
