using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    internal static partial class CatalogRules
    {
        private static void Miscellaneous(RuleContext c)
        {
            var t = c.T;
            for (var i = 0; i < t.Count; i++)
            {
                if (t[i].Kind != TokenKind.Word)
                {
                    continue;
                }

                if (t[i].Upper == "WAITFOR" && c.On(CodeIssueKind.MI007) && (c.Is(i + 1, "DELAY") || c.Is(i + 1, "TIME")))
                {
                    c.Add(CodeIssueKind.MI007, t[i].Start, t[i + 1].End - t[i].Start, "WAITFOR " + t[i + 1].Text.ToUpperInvariant() + " keeps the connection and its locks while it waits.");
                }
            }

            if (c.On(CodeIssueKind.MI002))
            {
                UnusedTemporaryTables(c);
            }

            if (c.On(CodeIssueKind.MI006))
            {
                UnusedParameters(c);
            }

            if (c.On(CodeIssueKind.MI008))
            {
                foreach (var m in c.Modules.Where(m => m.Kind != "VIEW"))
                {
                    for (var i = m.Start; i + 1 < m.End; i++)
                    {
                        if (c.Is(i, "SET") && c.Is(i + 1, "QUOTED_IDENTIFIER"))
                        {
                            c.Add(CodeIssueKind.MI008, t[i + 1], "SET QUOTED_IDENTIFIER inside a " + ModuleName(m.Kind) + " has no effect; the setting of the CREATE statement is used.");
                        }
                    }
                }
            }
        }

        private static string ModuleName(string kind) => kind == "PROC" ? "procedure" : kind.ToLowerInvariant();

        private static void UnusedTemporaryTables(RuleContext c)
        {
            var t = c.T;
            var created = new List<int>();
            for (var i = 0; i + 2 < t.Count; i++)
            {
                if (c.Is(i, "CREATE") && c.Is(i + 1, "TABLE") && t[i + 2].Kind == TokenKind.Word && t[i + 2].Text.StartsWith("#", StringComparison.Ordinal) && !t[i + 2].Text.StartsWith("##", StringComparison.Ordinal))
                {
                    created.Add(i + 2);
                }
                else if (c.Is(i, "INTO") && t[i + 1].Kind == TokenKind.Word && t[i + 1].Text.StartsWith("#", StringComparison.Ordinal) && !t[i + 1].Text.StartsWith("##", StringComparison.Ordinal) && !c.Is(i - 1, "INSERT"))
                {
                    created.Add(i + 1);
                }
            }

            foreach (var index in created)
            {
                var name = t[index].Text;
                var read = false;
                for (var j = 0; j < t.Count && !read; j++)
                {
                    if (j == index || t[j].Kind != TokenKind.Word || !string.Equals(t[j].Text, name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // The statements that only write or remove the table do not read it.
                    var writes = c.Is(j - 1, "TABLE") && (c.Is(j - 2, "DROP") || c.Is(j - 2, "TRUNCATE") || c.Is(j - 2, "CREATE") || c.Is(j - 3, "DROP")) ||
                        c.Is(j - 1, "EXISTS") && c.Is(j - 2, "IF") && c.Is(j - 3, "TABLE") ||
                        c.Is(j - 1, "INTO") && c.Is(j - 2, "INSERT") || c.Is(j - 1, "INSERT") || (c.Is(j - 1, "INTO") && !c.Is(j - 2, "INSERT"));
                    if (!writes)
                    {
                        read = true;
                    }
                }

                if (!read)
                {
                    c.Add(CodeIssueKind.MI002, t[index], "The temporary table " + name + " is created but never read.");
                }
            }
        }

        private static void UnusedParameters(RuleContext c)
        {
            var t = c.T;
            foreach (var m in c.Modules.Where(m => (m.Kind == "PROC" || m.Kind == "FUNCTION") && m.Body >= 0 && m.Name >= 0))
            {
                var parameters = new List<int>();
                for (var i = m.Name + 1; i < m.Body; i++)
                {
                    if (t[i].Kind == TokenKind.Variable && (c.Is(i - 1, "AS") == false) && (i == m.Name + 1 || c.IsKind(i - 1, TokenKind.Comma) || c.IsKind(i - 1, TokenKind.OpenParen)) &&
                        (c.Depth[i] == 0 || (c.Depth[i] == 1 && m.Kind == "FUNCTION")))
                    {
                        parameters.Add(i);
                    }
                }

                foreach (var p in parameters)
                {
                    var used = false;
                    for (var j = m.Body; j < m.End && !used; j++)
                    {
                        used = t[j].Kind == TokenKind.Variable && string.Equals(t[j].Text, t[p].Text, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!used)
                    {
                        c.Add(CodeIssueKind.MI006, t[p], "The parameter " + t[p].Text + " is declared but never used.");
                    }
                }
            }
        }
    }
}
