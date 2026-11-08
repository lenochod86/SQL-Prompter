using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>More rules (#208): the mistakes that are made all the time in T-SQL; some of them come with a repair (<see cref="CodeFix"/>).</summary>
    public static partial class CodeAnalyzer
    {
        private static readonly HashSet<string> SargabilityKillers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "YEAR", "MONTH", "DAY", "UPPER", "LOWER", "LTRIM", "RTRIM", "TRIM", "DATEPART", "DATENAME", "DATEDIFF", "DATEADD", "LEN", "SUBSTRING", "LEFT", "RIGHT",
            "CONVERT", "CAST", "TRY_CAST", "TRY_CONVERT", "ISNULL", "COALESCE", "REPLACE", "FORMAT", "EOMONTH",
        };

        private static readonly HashSet<string> FirstArgumentIsADatePart = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DATEPART", "DATENAME", "DATEDIFF", "DATEADD",
        };

        private static readonly HashSet<string> SizedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "VARCHAR", "NVARCHAR", "CHAR", "NCHAR", "VARBINARY", "BINARY",
        };

        private static void FindMoreIssues(string script, List<Token> tokens, AnalysisSettings settings, SqlPrompter.Core.Model.IDatabaseMetadata metadata, List<CodeIssue> issues)
        {
            if (settings.FunctionOnColumn)
            {
                FindFunctionOnColumn(tokens, issues);
            }

            if (settings.LeadingWildcard)
            {
                FindLeadingWildcard(tokens, issues);
            }

            if (settings.TopWithoutOrderBy)
            {
                FindTopWithoutOrderBy(tokens, metadata, issues);
            }

            if (settings.AtAtIdentity)
            {
                FindAtAtIdentity(tokens, issues);
            }

            if (settings.TypeWithoutLength)
            {
                FindTypeWithoutLength(tokens, issues);
            }

            if (settings.NotInSubquery)
            {
                FindNotInSubquery(script, tokens, issues);
            }

            if (settings.OldStyleJoin)
            {
                FindOldStyleJoin(script, tokens, issues);
            }

            FindWithMetadata(script, tokens, settings, metadata, issues);
        }

        private static int Matching(List<Token> tokens, int open)
        {
            var depth = 0;
            for (var i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.CloseParen && --depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>For every token whether it is in a condition (after WHERE, ON, HAVING ...), the same notion as for the comparison with NULL.</summary>
        private static bool[] ConditionMap(List<Token> tokens)
        {
            var result = new bool[tokens.Count];
            var saved = new Stack<bool>();
            var inCondition = false;
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                result[i] = inCondition;
                if (token.Kind == TokenKind.OpenParen)
                {
                    saved.Push(inCondition);
                }
                else if (token.Kind == TokenKind.CloseParen)
                {
                    inCondition = saved.Count > 0 && saved.Pop();
                }
                else if (token.Kind == TokenKind.Semicolon)
                {
                    inCondition = false;
                }
                else if (token.Kind == TokenKind.Word)
                {
                    switch (token.Upper)
                    {
                        case "WHERE":
                        case "ON":
                        case "HAVING":
                        case "WHEN":
                        case "IF":
                        case "WHILE":
                            inCondition = true;
                            break;
                        case "SELECT":
                        case "SET":
                        case "THEN":
                        case "ELSE":
                        case "VALUES":
                        case "FROM":
                        case "JOIN":
                        case "GROUP":
                        case "ORDER":
                        case "INTO":
                        case "BEGIN":
                        case "DECLARE":
                        case "OUTPUT":
                            inCondition = false;
                            break;
                    }
                }
            }

            return result;
        }

        private static bool IsComparisonStart(Token token) =>
            token != null && (token.Kind == TokenKind.Operator && (token.Text == "=" || token.Text == "<" || token.Text == ">" || token.Text == "!") ||
                              token.IsWord("LIKE") || token.IsWord("IN") || token.IsWord("BETWEEN") || token.IsWord("IS") || token.IsWord("NOT"));

        /// <summary>WHERE YEAR(d) = 2025, UPPER(c) = 'X': a function on a column that is compared prevents the use of an index on it.</summary>
        private static void FindFunctionOnColumn(List<Token> tokens, List<CodeIssue> issues)
        {
            var condition = ConditionMap(tokens);
            for (var i = 1; i + 1 < tokens.Count; i++)
            {
                var token = tokens[i];
                if (!condition[i] || token.Kind != TokenKind.Word || !SargabilityKillers.Contains(token.Text) || tokens[i + 1].Kind != TokenKind.OpenParen)
                {
                    continue;
                }

                var previous = tokens[i - 1];
                if (!(previous.IsWord("WHERE") || previous.IsWord("AND") || previous.IsWord("OR") || previous.IsWord("ON") || previous.IsWord("HAVING") || previous.IsWord("NOT") || previous.Kind == TokenKind.OpenParen))
                {
                    continue;
                }

                var close = Matching(tokens, i + 1);
                if (close < 0 || close + 1 >= tokens.Count || !IsComparisonStart(tokens[close + 1]))
                {
                    continue;
                }

                // The arguments must mention a column: a name that is no keyword, no function, no type after AS and no date part.
                var hasColumn = false;
                for (var j = i + 2; j < close; j++)
                {
                    var t = tokens[j];
                    if (!t.IsNamePart || (t.Kind == TokenKind.Word && (Keywords.IsReserved(t.Text) || t.Upper == "AS")) || (j + 1 <= close && tokens[j + 1].Kind == TokenKind.OpenParen) ||
                        (tokens[j - 1].IsWord("AS")) || (j == i + 2 && FirstArgumentIsADatePart.Contains(token.Text)) || (t.Kind == TokenKind.Word && Keywords.DataTypes.Contains(t.Text, StringComparer.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    hasColumn = true;
                    break;
                }

                if (!hasColumn)
                {
                    continue;
                }

                var dates = token.IsWord("YEAR") || token.IsWord("MONTH") || token.IsWord("DAY") || token.IsWord("DATEPART") || token.IsWord("EOMONTH");
                issues.Add(Issue(CodeIssueKind.FunctionOnColumn, CodeIssueSeverity.Hint, token,
                    "A function on the column prevents the use of an index on it." + (dates ? " Compare the column with a range instead (col >= '2025-01-01' AND col < '2026-01-01')." : " Compare the column itself, or put the inverse function on the other side."),
                    tokens[close].End - token.Start));
            }
        }

        /// <summary>LIKE '%text': the leading wildcard cannot use an index.</summary>
        private static void FindLeadingWildcard(List<Token> tokens, List<CodeIssue> issues)
        {
            for (var i = 0; i + 1 < tokens.Count; i++)
            {
                if (!tokens[i].IsWord("LIKE") || tokens[i + 1].Kind != TokenKind.String)
                {
                    continue;
                }

                var text = tokens[i + 1].Text;
                var quote = text.IndexOf('\'');
                if (quote >= 0 && quote + 1 < text.Length && text[quote + 1] == '%')
                {
                    issues.Add(new CodeIssue(CodeIssueKind.LeadingWildcard, CodeIssueSeverity.Hint, tokens[i + 1].Start, tokens[i + 1].End - tokens[i + 1].Start,
                        "A pattern that starts with % cannot use an index, so the whole table or index is scanned."));
                }
            }
        }

        private static readonly HashSet<string> EndsABlock = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INSERT", "UPDATE", "DELETE", "SELECT", "EXEC", "EXECUTE", "DECLARE", "SET", "IF", "WHILE", "BEGIN", "END", "PRINT", "GO", "MERGE", "WITH", "RETURN",
            "UNION", "EXCEPT", "INTERSECT",
        };

        /// <summary>SELECT TOP n ... without ORDER BY (outside EXISTS and a UNION): the rows returned are arbitrary.</summary>
        private static void FindTopWithoutOrderBy(List<Token> tokens, SqlPrompter.Core.Model.IDatabaseMetadata metadata, List<CodeIssue> issues)
        {
            for (var i = 0; i + 2 < tokens.Count; i++)
            {
                if (!tokens[i].IsWord("SELECT") || !tokens[i + 1].IsWord("TOP"))
                {
                    continue;
                }

                var topEnd = tokens[i + 2].Kind == TokenKind.OpenParen ? Matching(tokens, i + 2) : i + 2;
                if (topEnd < 0)
                {
                    continue;
                }

                // In EXISTS (SELECT TOP 1 ...) the order does not matter.
                if (i >= 2 && tokens[i - 1].Kind == TokenKind.OpenParen && tokens[i - 2].IsWord("EXISTS"))
                {
                    continue;
                }

                var depth = 0;
                var ordered = false;
                var done = false;
                var blockEnd = tokens.Count;
                for (var j = topEnd + 1; j < tokens.Count && !done; j++)
                {
                    var t = tokens[j];
                    blockEnd = j + 1;
                    if (t.Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (t.Kind == TokenKind.CloseParen)
                    {
                        if (--depth < 0)
                        {
                            done = true;
                            blockEnd = j;
                        }
                    }
                    else if (depth == 0 && t.Kind == TokenKind.Semicolon)
                    {
                        done = true;
                        blockEnd = j;
                    }
                    else if (depth == 0 && t.IsWord("ORDER") && j + 1 < tokens.Count && tokens[j + 1].IsWord("BY"))
                    {
                        ordered = true;
                        done = true;
                    }
                    else if (depth == 0 && t.Kind == TokenKind.Word && EndsABlock.Contains(t.Text) && !(t.IsWord("SELECT") && j > 0 && tokens[j - 1].IsWord("INTO")))
                    {
                        // UNION and friends: the ORDER BY of the whole query decides; a new statement ends the block.
                        ordered = t.IsWord("UNION") || t.IsWord("EXCEPT") || t.IsWord("INTERSECT");
                        done = true;
                        blockEnd = j;
                    }
                }

                if (!ordered)
                {
                    issues.Add(new CodeIssue(CodeIssueKind.TopWithoutOrderBy, CodeIssueSeverity.Hint, tokens[i + 1].Start, tokens[topEnd].End - tokens[i + 1].Start,
                        "TOP without ORDER BY returns arbitrary rows; add an ORDER BY to say which ones.", TopFix(tokens, topEnd + 1, blockEnd, metadata)));
                }
            }
        }

        /// <summary>@@IDENTITY also returns identities made by triggers; SCOPE_IDENTITY() is the one of this scope.</summary>
        private static void FindAtAtIdentity(List<Token> tokens, List<CodeIssue> issues)
        {
            foreach (var token in tokens.Where(t => t.Kind == TokenKind.Variable && string.Equals(t.Text, "@@IDENTITY", StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new CodeIssue(CodeIssueKind.AtAtIdentity, CodeIssueSeverity.Warning, token.Start, token.End - token.Start,
                    "@@IDENTITY also returns identity values generated by triggers; use SCOPE_IDENTITY().", new CodeFix(token.Start, token.End - token.Start, "SCOPE_IDENTITY()")));
            }
        }

        /// <summary>varchar, nvarchar, char ... without a length: the length is 1 in DECLARE and 30 in CAST and CONVERT.</summary>
        private static void FindTypeWithoutLength(List<Token> tokens, List<CodeIssue> issues)
        {
            for (var i = 1; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Kind != TokenKind.Word || !SizedTypes.Contains(t.Text) || (i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.OpenParen))
                {
                    continue;
                }

                var previous = tokens[i - 1];
                var declared = previous.Kind == TokenKind.Variable || (previous.IsWord("AS") && i >= 2 && tokens[i - 2].Kind == TokenKind.Variable);
                var cast = previous.IsWord("AS") && InsideCast(tokens, i - 1);
                var convert = previous.Kind == TokenKind.OpenParen && i >= 2 && (tokens[i - 2].IsWord("CONVERT") || tokens[i - 2].IsWord("TRY_CONVERT")) && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Comma;
                if (!declared && !cast && !convert)
                {
                    continue;
                }

                var length = t.IsWord("CHAR") || t.IsWord("NCHAR") ? "(10)" : "(100)";
                issues.Add(new CodeIssue(CodeIssueKind.TypeWithoutLength, CodeIssueSeverity.Warning, t.Start, t.End - t.Start,
                    t.Text + " without a length is 1 character in a declaration and 30 in CAST and CONVERT; write " + t.Text + length + " (or (MAX)).", new CodeFix(t.End, 0, length)));
            }
        }

        private static bool InsideCast(List<Token> tokens, int asIndex)
        {
            var depth = 0;
            for (var i = asIndex - 1; i >= 1; i--)
            {
                if (tokens[i].Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    if (depth == 0)
                    {
                        return tokens[i - 1].IsWord("CAST") || tokens[i - 1].IsWord("TRY_CAST");
                    }

                    depth--;
                }
            }

            return false;
        }

        /// <summary>NOT IN (SELECT ...): when the subquery returns a NULL the condition is never true.</summary>
        private static void FindNotInSubquery(string script, List<Token> tokens, List<CodeIssue> issues)
        {
            for (var i = 0; i + 3 < tokens.Count; i++)
            {
                if (tokens[i].IsWord("NOT") && tokens[i + 1].IsWord("IN") && tokens[i + 2].Kind == TokenKind.OpenParen && tokens[i + 3].IsWord("SELECT"))
                {
                    issues.Add(new CodeIssue(CodeIssueKind.NotInSubquery, CodeIssueSeverity.Hint, tokens[i].Start, tokens[i + 1].End - tokens[i].Start,
                        "NOT IN (subquery) returns no rows at all when the subquery returns a NULL; NOT EXISTS does not have that problem.", NotInFix(script, tokens, i)));
                }
            }
        }

        private static readonly HashSet<string> EndsFrom = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "OPTION", "FOR", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "ON",
            "SET", "SELECT", "INSERT", "UPDATE", "DELETE", "WITH", "VALUES", "OUTPUT",
        };

        /// <summary>FROM a, b: the tables are joined in the WHERE clause; JOIN ... ON says what is meant.</summary>
        private static void FindOldStyleJoin(string script, List<Token> tokens, List<CodeIssue> issues)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!tokens[i].IsWord("FROM") || (i > 0 && tokens[i - 1].IsWord("DELETE")))
                {
                    continue;
                }

                var depth = 0;
                for (var j = i + 1; j < tokens.Count; j++)
                {
                    var t = tokens[j];
                    if (t.Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (t.Kind == TokenKind.CloseParen)
                    {
                        if (--depth < 0)
                        {
                            break;
                        }
                    }
                    else if (depth == 0 && (t.Kind == TokenKind.Semicolon || (t.Kind == TokenKind.Word && EndsFrom.Contains(t.Text) && !t.IsWord("ON"))))
                    {
                        break;
                    }
                    else if (depth == 0 && t.IsWord("ON"))
                    {
                        break;
                    }
                    else if (depth == 0 && t.Kind == TokenKind.Comma)
                    {
                        issues.Add(new CodeIssue(CodeIssueKind.OldStyleJoin, CodeIssueSeverity.Hint, t.Start, t.End - t.Start,
                            "A comma between tables is an old-style join; use JOIN ... ON so that the condition cannot be forgotten.", OldJoinFix(script, tokens, j)));
                        break;
                    }
                }
            }
        }

        private static CodeIssue Issue(CodeIssueKind kind, CodeIssueSeverity severity, Token token, string message, int length) =>
            new CodeIssue(kind, severity, token.Start, length, message);
    }
}
