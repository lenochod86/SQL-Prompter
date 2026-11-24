using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>A procedure, function, trigger or view that the script creates or alters.</summary>
    internal sealed class ModuleInfo
    {
        /// <summary>PROC, FUNCTION, TRIGGER or VIEW.</summary>
        public string Kind;

        /// <summary>Index of the CREATE / ALTER token.</summary>
        public int Start;

        /// <summary>Index after the last token of the module (the next GO or module, or the end).</summary>
        public int End;

        /// <summary>Index of the last part of the name; -1 when there is none.</summary>
        public int Name = -1;

        /// <summary>Index of the first token of the body (after the AS of the header); -1 when there is none.</summary>
        public int Body = -1;
    }

    /// <summary>What the rules of the catalog share: the tokens, the statements and modules of the script, the parenthesis structure and the way to report an issue.</summary>
    internal sealed class RuleContext
    {
        private static readonly HashSet<string> StatementWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "DECLARE", "SET", "IF", "ELSE", "WHILE", "BEGIN", "END", "EXEC", "EXECUTE", "PRINT", "RETURN",
            "USE", "TRUNCATE", "CREATE", "ALTER", "DROP", "GRANT", "DENY", "REVOKE", "COMMIT", "ROLLBACK", "SAVE", "RAISERROR", "THROW", "GOTO", "BREAK", "CONTINUE",
            "WAITFOR", "OPEN", "CLOSE", "FETCH", "DEALLOCATE", "GO", "BACKUP", "RESTORE", "DBCC",
        };

        public RuleContext(string script, List<Token> allTokens, List<Token> tokens, AnalysisSettings settings, IDatabaseMetadata metadata, List<CodeIssue> issues)
        {
            Script = script;
            AllTokens = allTokens;
            T = tokens;
            Settings = settings;
            Metadata = metadata;
            Issues = issues;
            var n = tokens.Count;
            Depth = new int[n];
            Owner = new int[n];
            var open = new Stack<int>();
            for (var i = 0; i < n; i++)
            {
                Depth[i] = open.Count;
                Owner[i] = open.Count == 0 ? -1 : open.Peek() - 1;
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    open.Push(i);
                }
                else if (tokens[i].Kind == TokenKind.CloseParen && open.Count > 0)
                {
                    open.Pop();
                }
            }

            var bounds = StatementSplitter.Split(tokens);
            Statements = new List<int[]>();
            statementOf = new int[n];
            for (var b = 0; b < bounds.Count; b++)
            {
                var range = new[] { bounds[b], b + 1 < bounds.Count ? bounds[b + 1] : n };
                Statements.Add(range);
                for (var k = range[0]; k < range[1]; k++)
                {
                    statementOf[k] = b;
                }
            }

            Modules = FindModules();
        }

        /// <summary>The cursors of the script, computed once by the cursor rules.</summary>
        public object CursorCache { get; set; }

        public string Script { get; }

        public List<Token> AllTokens { get; }

        /// <summary>The tokens without comments.</summary>
        public List<Token> T { get; }

        public AnalysisSettings Settings { get; }

        public IDatabaseMetadata Metadata { get; }

        public List<CodeIssue> Issues { get; }

        /// <summary>Parenthesis depth in front of every token.</summary>
        public int[] Depth { get; }

        /// <summary>For every token the index of the token before the innermost parenthesis that encloses it (-1 at the top level).</summary>
        public int[] Owner { get; }

        /// <summary>Start (inclusive) and end (exclusive) token index of every statement.</summary>
        public List<int[]> Statements { get; }

        public List<ModuleInfo> Modules { get; }

        public bool On(CodeIssueKind kind) => Settings.IsOn(kind);

        public bool Is(int i, string word) => i >= 0 && i < T.Count && T[i].IsWord(word);

        public bool IsKind(int i, TokenKind kind) => i >= 0 && i < T.Count && T[i].Kind == kind;

        public bool IsOp(int i, string text) => i >= 0 && i < T.Count && T[i].Kind == TokenKind.Operator && T[i].Text == text;

        public string UpperAt(int i) => i >= 0 && i < T.Count ? T[i].Upper : null;

        /// <summary>Whether the two tokens touch (no space between them): <c>!=</c> is the two tokens <c>!</c> and <c>=</c>.</summary>
        public bool Touches(int a, int b) => a >= 0 && b < T.Count && T[a].End == T[b].Start;

        public void Add(CodeIssueKind kind, Token token, string message, CodeFix fix = null) => Add(kind, token.Start, token.End - token.Start, message, fix);

        public void Add(CodeIssueKind kind, int start, int length, string message, CodeFix fix = null)
        {
            var rule = AnalysisRules.Of(kind);
            Issues.Add(new CodeIssue(kind, rule?.Severity ?? CodeIssueSeverity.Hint, start, length, message, fix));
        }

        public ModuleInfo ModuleAt(int index) => Modules.FirstOrDefault(m => index >= m.Start && index < m.End);

        /// <summary>Index of the batch end (the next GO or the end of the script) at or after the token.</summary>
        public int BatchEnd(int from)
        {
            for (var i = from; i < T.Count; i++)
            {
                if (T[i].IsWord("GO"))
                {
                    return i;
                }
            }

            return T.Count;
        }

        public int BatchStart(int at)
        {
            for (var i = Math.Min(at, T.Count) - 1; i >= 0; i--)
            {
                if (T[i].IsWord("GO"))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        /// <summary>Index of the statement that contains the token.</summary>
        public int[] StatementAt(int index) => index >= 0 && index < statementOf.Length && Statements.Count > 0 ? Statements[statementOf[index]] : new[] { index, index + 1 };

        private readonly int[] statementOf;

        /// <summary>The matching close parenthesis of the open one at the index, or -1.</summary>
        public int Matching(int open)
        {
            var depth = 0;
            for (var i = open; i < T.Count; i++)
            {
                if (T[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (T[i].Kind == TokenKind.CloseParen && --depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>End (exclusive) of the list that starts after the keyword at <paramref name="keyword"/> (the select list, the SET list): up to the next clause or statement at the same depth.</summary>
        public int ListEnd(int keyword, params string[] stops)
        {
            var depth = Depth[keyword];
            for (var i = keyword + 1; i < T.Count; i++)
            {
                var t = T[i];
                if (t.Kind == TokenKind.CloseParen && Depth[i] == depth)
                {
                    return i;
                }

                if (Depth[i] != depth)
                {
                    continue;
                }

                if (t.Kind == TokenKind.Semicolon)
                {
                    return i;
                }

                if (t.Kind == TokenKind.Word && (StatementWords.Contains(t.Text) && !t.IsWord("WITH") || stops.Contains(t.Upper)))
                {
                    return i;
                }
            }

            return T.Count;
        }

        public static readonly string[] SelectStops = { "FROM", "INTO", "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "FOR", "OPTION" };

        /// <summary>Whether the word at the index is in the type position of a declaration (after a variable or a column name, AS, RETURNS).</summary>
        public bool IsTypePosition(int i)
        {
            if (i <= 0 || !T[i].IsNamePart || IsKind(i + 1, TokenKind.Dot) || IsKind(i - 1, TokenKind.Dot))
            {
                return false;
            }

            var p = T[i - 1];
            return p.Kind == TokenKind.Variable || p.IsWord("AS") || p.IsWord("RETURNS") || p.Kind == TokenKind.QuotedIdentifier ||
                (p.Kind == TokenKind.Word && !Sql.Keywords.IsReserved(p.Text) && !StatementWords.Contains(p.Text));
        }

        private List<ModuleInfo> FindModules()
        {
            var modules = new List<ModuleInfo>();
            for (var i = 0; i < T.Count; i++)
            {
                if (!(T[i].IsWord("CREATE") || T[i].IsWord("ALTER")) || (i > 0 && !IsStatementStart(i)))
                {
                    continue;
                }

                var j = i + 1;
                if (Is(j, "OR") && Is(j + 1, "ALTER"))
                {
                    j += 2;
                }

                var kind = UpperAt(j);
                if (kind == "PROCEDURE")
                {
                    kind = "PROC";
                }

                if (kind != "PROC" && kind != "FUNCTION" && kind != "TRIGGER" && kind != "VIEW")
                {
                    continue;
                }

                var module = new ModuleInfo { Kind = kind, Start = i };
                j++;
                while (j < T.Count && T[j].IsNamePart)
                {
                    module.Name = j;
                    j++;
                    if (IsKind(j, TokenKind.Dot))
                    {
                        j++;
                    }
                    else
                    {
                        break;
                    }
                }

                var end = j;
                while (end < T.Count && !T[end].IsWord("GO"))
                {
                    if (end > j && (T[end].IsWord("CREATE") || T[end].IsWord("ALTER")) && IsStatementStart(end) && IsModuleKeyword(end))
                    {
                        break;
                    }

                    end++;
                }

                module.End = end;
                for (var k = j; k < end; k++)
                {
                    if (T[k].IsWord("AS") && Depth[k] == 0 && k > 0 && T[k - 1].Kind != TokenKind.Variable)
                    {
                        module.Body = k + 1;
                        break;
                    }
                }

                modules.Add(module);
                i = end - 1;
            }

            return modules;
        }

        private HashSet<int> starts;

        public bool IsStatementStart(int i) => (starts ?? (starts = new HashSet<int>(Statements.Select(s => s[0])))).Contains(i);

        private bool IsModuleKeyword(int i)
        {
            var j = i + 1;
            if (Is(j, "OR") && Is(j + 1, "ALTER"))
            {
                j += 2;
            }

            var k = UpperAt(j);
            return k == "PROC" || k == "PROCEDURE" || k == "FUNCTION" || k == "TRIGGER" || k == "VIEW";
        }
    }
}
