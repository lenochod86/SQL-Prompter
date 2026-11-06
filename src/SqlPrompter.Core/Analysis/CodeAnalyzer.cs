using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Analysis
{
    public enum CodeIssueKind
    {
        EqualsNull,
        JoinWithoutOn,
        InsertWithoutColumns,
        UnusedVariable,
        SelectStar,
        LockHint,
        UnexpectedEnd,
        FunctionOnColumn,
        LeadingWildcard,
        TopWithoutOrderBy,
        AtAtIdentity,
        TypeWithoutLength,
        NotInSubquery,
        OldStyleJoin,
    }

    public enum CodeIssueSeverity
    {
        Hint,
        Warning,
    }

    /// <summary>The text that repairs an issue: <see cref="Length"/> characters at <see cref="Start"/> are replaced by <see cref="Replacement"/> (an insertion has a length of 0).</summary>
    public sealed class CodeFix
    {
        public CodeFix(int start, int length, string replacement)
        {
            Start = start;
            Length = length;
            Replacement = replacement;
        }

        public int Start { get; }

        public int Length { get; }

        public string Replacement { get; }
    }

    /// <summary>A problem found in the script: the stretch of text to underline and what to tell the user.</summary>
    public sealed class CodeIssue
    {
        public CodeIssue(CodeIssueKind kind, CodeIssueSeverity severity, int start, int length, string message, CodeFix fix = null)
        {
            Fix = fix;
            Kind = kind;
            Severity = severity;
            Start = start;
            Length = length;
            Message = message;
        }

        public CodeIssueKind Kind { get; }

        public CodeIssueSeverity Severity { get; }

        public int Start { get; }

        public int Length { get; }

        public string Message { get; }

        /// <summary>The repair, or null when the issue has none (the message says what to do).</summary>
        public CodeFix Fix { get; }

        public override string ToString() => Kind + "@" + Start + ": " + Message;
    }

    /// <summary>
    /// Finds suspicious code from the text of the script alone (no database needed): <c>= NULL</c>, JOIN without ON, INSERT without a column
    /// list, unused variables, <c>SELECT *</c> and lock hints. Comments and strings are never inspected. Each rule is switched by
    /// <see cref="AnalysisSettings"/>.
    /// </summary>
    public static partial class CodeAnalyzer
    {
        private static readonly HashSet<string> StopsJoin = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "JOIN", "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "SELECT", "INSERT", "UPDATE", "DELETE", "GO", "OPTION", "FOR",
        };

        private static readonly HashSet<string> StartsStatement = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "SET", "EXEC", "EXECUTE", "IF", "WHILE", "BEGIN", "PRINT", "WITH", "MERGE", "RETURN", "USE",
        };

        private static readonly HashSet<string> Clauses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FROM", "WHERE", "GROUP", "ORDER", "HAVING", "ON", "SET", "VALUES", "INTO", "JOIN",
        };

        public static IReadOnlyList<CodeIssue> Analyze(string script, AnalysisSettings settings)
        {
            var issues = new List<CodeIssue>();
            if (settings == null || !settings.Enabled || string.IsNullOrWhiteSpace(script))
            {
                return issues;
            }

            var allTokens = Lexer.Tokenize(script).ToList();
            var tokens = allTokens.Where(t => t.Kind != TokenKind.Comment).ToList();
            if (settings.UnexpectedEnd)
            {
                UnexpectedEnd.Find(script, allTokens, issues);
            }

            if (settings.EqualsNull)
            {
                FindEqualsNull(tokens, issues);
            }

            if (settings.JoinWithoutOn)
            {
                FindJoinWithoutOn(tokens, issues);
            }

            if (settings.InsertWithoutColumns)
            {
                FindInsertWithoutColumns(tokens, issues);
            }

            if (settings.UnusedVariable)
            {
                FindUnusedVariables(tokens, issues);
            }

            if (settings.SelectStar)
            {
                FindSelectStar(tokens, issues);
            }

            if (settings.LockHint)
            {
                FindLockHints(tokens, issues);
            }

            FindMoreIssues(tokens, settings, issues);

            return issues.OrderBy(i => i.Start).ToList();
        }

        private static CodeIssue Issue(CodeIssueKind kind, CodeIssueSeverity severity, Token token, string message) =>
            new CodeIssue(kind, severity, token.Start, token.End - token.Start, message);

        private static bool IsComparison(Token token) =>
            token.Kind == TokenKind.Operator && (token.Text == "=" || token.Text == "<" || token.Text == ">" || token.Text == "!");

        /// <summary>
        /// <c>x = NULL</c> is never true. Reported inside a condition (WHERE, ON, HAVING, WHEN, IF, WHILE), not in an assignment
        /// (<c>SET x = NULL</c>, a select list alias, a default).
        /// </summary>
        private static void FindEqualsNull(List<Token> tokens, List<CodeIssue> issues)
        {
            var saved = new Stack<bool>();
            var inCondition = false;
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
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
                else if (inCondition && IsComparison(token))
                {
                    // The lexer gives one token per character, so <> and != are two adjacent tokens.
                    var last = i;
                    while (last + 1 < tokens.Count && IsComparison(tokens[last + 1]) && tokens[last + 1].Start == tokens[last].End)
                    {
                        last++;
                    }

                    var text = string.Concat(tokens.Skip(i).Take(last - i + 1).Select(t => t.Text));
                    var next = last + 1 < tokens.Count ? tokens[last + 1] : null;
                    var previous = i > 0 ? tokens[i - 1] : null;
                    if ((next != null && next.IsWord("NULL")) || (previous != null && previous.IsWord("NULL")))
                    {
                        var negated = text != "=";
                        // x = NULL has a repair: the operator and the NULL after it become IS [NOT] NULL.
                        var fix = next != null && next.IsWord("NULL") ? new CodeFix(token.Start, next.End - token.Start, "IS " + (negated ? "NOT " : string.Empty) + "NULL") : null;
                        issues.Add(new CodeIssue(
                            CodeIssueKind.EqualsNull,
                            CodeIssueSeverity.Warning,
                            token.Start,
                            tokens[last].End - token.Start,
                            "Comparing with NULL using " + text + " is never true; use IS " + (negated ? "NOT " : string.Empty) + "NULL.",
                            fix));
                    }

                    i = last;
                }
            }
        }

        /// <summary>A JOIN needs ON, except CROSS JOIN.</summary>
        private static void FindJoinWithoutOn(List<Token> tokens, List<CodeIssue> issues)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!tokens[i].IsWord("JOIN") || (i > 0 && tokens[i - 1].IsWord("CROSS")))
                {
                    continue;
                }

                var depth = 0;
                var found = false;
                for (var j = i + 1; j < tokens.Count && !found; j++)
                {
                    var token = tokens[j];
                    if (token.Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (token.Kind == TokenKind.CloseParen)
                    {
                        if (--depth < 0)
                        {
                            break;
                        }
                    }
                    else if (depth == 0)
                    {
                        if (token.Kind == TokenKind.Semicolon)
                        {
                            break;
                        }

                        if (token.IsWord("ON"))
                        {
                            found = true;
                        }
                        else if (token.Kind == TokenKind.Word && StopsJoin.Contains(token.Text))
                        {
                            break;
                        }
                    }
                }

                if (!found)
                {
                    issues.Add(Issue(CodeIssueKind.JoinWithoutOn, CodeIssueSeverity.Warning, tokens[i], "JOIN without ON: every row is combined with every row (a Cartesian product)."));
                }
            }
        }

        /// <summary><c>INSERT INTO t VALUES (…)</c> breaks when a column is added to the table.</summary>
        private static void FindInsertWithoutColumns(List<Token> tokens, List<CodeIssue> issues)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!tokens[i].IsWord("INSERT"))
                {
                    continue;
                }

                var j = i + 1;
                if (j < tokens.Count && tokens[j].IsWord("INTO"))
                {
                    j++;
                }

                if (j >= tokens.Count || !(tokens[j].IsNamePart || tokens[j].Kind == TokenKind.Variable))
                {
                    continue;
                }

                j++;
                while (j + 1 < tokens.Count && tokens[j].Kind == TokenKind.Dot && tokens[j + 1].IsNamePart)
                {
                    j += 2;
                }

                if (j < tokens.Count && (tokens[j].IsWord("VALUES") || tokens[j].IsWord("SELECT") || tokens[j].IsWord("EXEC") || tokens[j].IsWord("EXECUTE") || tokens[j].IsWord("WITH")))
                {
                    issues.Add(Issue(CodeIssueKind.InsertWithoutColumns, CodeIssueSeverity.Hint, tokens[i], "INSERT without a column list depends on the order of the table's columns."));
                }
            }
        }

        /// <summary>A variable declared with DECLARE that the batch (up to GO) never mentions again.</summary>
        private static void FindUnusedVariables(List<Token> tokens, List<CodeIssue> issues)
        {
            var batchStart = 0;
            for (var i = 0; i <= tokens.Count; i++)
            {
                if (i < tokens.Count && !tokens[i].IsWord("GO"))
                {
                    continue;
                }

                var uses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var declarations = new List<Token>();
                var declaring = false;
                var depth = 0;
                for (var j = batchStart; j < i; j++)
                {
                    var token = tokens[j];
                    if (token.IsWord("DECLARE"))
                    {
                        declaring = true;
                        depth = 0;
                    }
                    else if (token.Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (token.Kind == TokenKind.CloseParen)
                    {
                        depth = Math.Max(0, depth - 1);
                    }
                    else if (token.Kind == TokenKind.Variable && !token.Text.StartsWith("@@", StringComparison.Ordinal))
                    {
                        uses.TryGetValue(token.Text, out var count);
                        uses[token.Text] = count + 1;

                        // In a DECLARE a name starts the list or follows a comma at the top level (not a default value that mentions another variable).
                        if (declaring && depth == 0 && j > batchStart && (tokens[j - 1].IsWord("DECLARE") || tokens[j - 1].Kind == TokenKind.Comma))
                        {
                            declarations.Add(token);
                        }
                    }
                    else if (declaring && depth == 0 && token.Kind == TokenKind.Word && StartsStatement.Contains(token.Text))
                    {
                        declaring = false;
                    }
                }

                foreach (var declaration in declarations)
                {
                    if (uses[declaration.Text] == 1)
                    {
                        issues.Add(Issue(CodeIssueKind.UnusedVariable, CodeIssueSeverity.Hint, declaration, "Variable " + declaration.Text + " is declared but never used."));
                    }
                }

                batchStart = i + 1;
            }
        }

        /// <summary><c>SELECT *</c> and <c>alias.*</c> in a select list (not <c>COUNT(*)</c> and not <c>EXISTS (SELECT *</c>).</summary>
        private static void FindSelectStar(List<Token> tokens, List<CodeIssue> issues)
        {
            for (var i = 1; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind != TokenKind.Operator || token.Text != "*")
                {
                    continue;
                }

                var previous = tokens[i - 1];
                var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
                bool endsItem = next == null || next.Kind == TokenKind.Comma || next.Kind == TokenKind.CloseParen || next.Kind == TokenKind.Semicolon ||
                    (next.Kind == TokenKind.Word && StopsItem.Contains(next.Text));
                bool inList;
                if (!endsItem)
                {
                    inList = false; // multiplication
                }
                else if (previous.IsWord("SELECT") || previous.IsWord("DISTINCT") || previous.IsWord("ALL"))
                {
                    inList = true;
                }
                else if (previous.Kind == TokenKind.Comma || previous.Kind == TokenKind.Dot)
                {
                    inList = InSelectList(tokens, i);
                }
                else if (previous.Kind == TokenKind.CloseParen)
                {
                    inList = FollowsTop(tokens, i - 1) && InSelectList(tokens, i);
                }
                else
                {
                    inList = false;
                }

                if (inList && !IsInsideExists(tokens, i))
                {
                    issues.Add(Issue(CodeIssueKind.SelectStar, CodeIssueSeverity.Hint, token, "SELECT * returns every column, including those added later; list the columns you need."));
                }
            }
        }

        private static readonly HashSet<string> StopsItem = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FROM", "INTO", "UNION", "EXCEPT", "INTERSECT", "ORDER", "FOR", "GO", "OPTION", "WHERE",
        };

        /// <summary>The parenthesis closed at <paramref name="close"/> belongs to TOP: <c>TOP (10) *</c>.</summary>
        private static bool FollowsTop(List<Token> tokens, int close)
        {
            var depth = 0;
            for (var k = close; k >= 0; k--)
            {
                if (tokens[k].Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (tokens[k].Kind == TokenKind.OpenParen && --depth == 0)
                {
                    return k > 0 && tokens[k - 1].IsWord("TOP");
                }
            }

            return false;
        }

        /// <summary>The nearest clause word at the same level before the star is SELECT (so a comma, a dot or <c>TOP (n)</c> is part of a select list).</summary>
        private static bool InSelectList(List<Token> tokens, int star)
        {
            var depth = 0;
            for (var k = star - 1; k >= 0; k--)
            {
                var token = tokens[k];
                if (token.Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (token.Kind == TokenKind.OpenParen)
                {
                    if (--depth < 0)
                    {
                        return false; // inside a function call: COUNT(*)
                    }
                }
                else if (depth == 0 && token.Kind == TokenKind.Word)
                {
                    if (token.IsWord("SELECT"))
                    {
                        return true;
                    }

                    if (Clauses.Contains(token.Text))
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static bool IsInsideExists(List<Token> tokens, int star)
        {
            var k = star - 1;
            while (k >= 0 && !tokens[k].IsWord("SELECT"))
            {
                k--;
            }

            return k >= 2 && tokens[k - 1].Kind == TokenKind.OpenParen && tokens[k - 2].IsWord("EXISTS");
        }

        /// <summary>NOLOCK and READUNCOMMITTED read uncommitted data.</summary>
        private static void FindLockHints(List<Token> tokens, List<CodeIssue> issues)
        {
            foreach (var token in tokens)
            {
                if (token.IsWord("NOLOCK") || token.IsWord("READUNCOMMITTED"))
                {
                    issues.Add(Issue(CodeIssueKind.LockHint, CodeIssueSeverity.Hint, token, token.Text.ToUpperInvariant() + " can return uncommitted, missing or duplicated rows."));
                }
            }
        }
    }
}
