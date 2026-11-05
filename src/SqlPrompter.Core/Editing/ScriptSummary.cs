using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// A summary of a script as a block comment: how many batches and statements of which kind it has, which objects it reads, changes,
    /// executes, creates or drops, and which variables and temporary tables it uses. Built from the text alone (no metadata).
    /// </summary>
    public static class ScriptSummary
    {
        public static string Build(string text, string newLine = "\r\n")
        {
            text = text ?? string.Empty;
            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            var bounds = StatementSplitter.Split(tokens);
            var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var batches = tokens.Count == 0 ? 0 : 1;
            foreach (var b in bounds)
            {
                var head = tokens[b];
                if (head.IsWord("GO"))
                {
                    batches++;
                    continue;
                }

                if (head.Upper != null)
                {
                    kinds.TryGetValue(head.Upper, out var n);
                    kinds[head.Upper] = n + 1;
                }
            }

            var read = new Names();
            var written = new Names();
            var executed = new Names();
            var created = new Names();
            var dropped = new Names();
            var temps = new Names();
            var variables = new Names();
            var ctes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.IsNamePart && i + 2 < tokens.Count && tokens[i + 1].IsWord("AS") && tokens[i + 2].Kind == TokenKind.OpenParen &&
                    i > 0 && (tokens[i - 1].IsWord("WITH") || tokens[i - 1].Kind == TokenKind.Comma))
                {
                    ctes.Add(token.IdentifierValue);
                }

                if (token.Kind == TokenKind.Variable && !token.Text.StartsWith("@@", StringComparison.Ordinal))
                {
                    variables.Add(token.Text);
                }
            }

            for (var i = 0; i < tokens.Count; i++)
            {
                var word = tokens[i].Upper;
                if (word == null)
                {
                    continue;
                }

                var previous = i > 0 ? tokens[i - 1].Upper : null;
                switch (word)
                {
                    case "FROM":
                    case "JOIN":
                        // DELETE FROM t changes the table, other FROM and JOIN read.
                        Collect(tokens, i + 1, previous == "DELETE" ? written : read, ctes, temps, previous != "DELETE" && word == "FROM");
                        break;
                    case "INTO":
                        Collect(tokens, i + 1, written, ctes, temps, false);
                        break;
                    case "UPDATE":
                        if (previous != "ON" && previous != "KEY" && i + 1 < tokens.Count && !tokens[i + 1].IsWord("STATISTICS"))
                        {
                            Collect(tokens, i + 1, written, ctes, temps, false);
                        }

                        break;
                    case "DELETE":
                        if (i + 1 < tokens.Count && !tokens[i + 1].IsWord("FROM"))
                        {
                            Collect(tokens, i + 1, written, ctes, temps, false);
                        }

                        break;
                    case "TRUNCATE":
                        if (i + 2 < tokens.Count && tokens[i + 1].IsWord("TABLE"))
                        {
                            Collect(tokens, i + 2, written, ctes, temps, false);
                        }

                        break;
                    case "EXEC":
                    case "EXECUTE":
                        if (i + 1 < tokens.Count && tokens[i + 1].IsNamePart)
                        {
                            Collect(tokens, i + 1, executed, ctes, temps, false);
                        }

                        break;
                    case "CREATE":
                    case "ALTER":
                    case "DROP":
                        Declared(tokens, i, word == "DROP" ? dropped : created, temps);
                        break;
                }
            }

            var sb = new StringBuilder();
            sb.Append("/*").Append(newLine).Append("Script summary").Append(newLine).Append(newLine);
            sb.Append("Batches: ").Append(batches).Append(", statements: ").Append(kinds.Values.Sum()).Append(newLine);
            if (kinds.Count > 0)
            {
                sb.Append("  ").Append(string.Join(", ", kinds.OrderByDescending(k => k.Value).ThenBy(k => k.Key).Select(k => k.Key + " " + k.Value))).Append(newLine);
            }

            Section(sb, "Reads", read, newLine);
            Section(sb, "Changes", written, newLine);
            Section(sb, "Executes", executed, newLine);
            Section(sb, "Creates or alters", created, newLine);
            Section(sb, "Drops", dropped, newLine);
            Section(sb, "Temporary tables", temps, newLine);
            Section(sb, "Variables", variables, newLine);
            sb.Append("*/").Append(newLine);
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title, Names names, string newLine)
        {
            if (names.Count == 0)
            {
                return;
            }

            sb.Append(newLine).Append(title).Append(" (").Append(names.Count).Append("):").Append(newLine);
            foreach (var name in names.Ordered())
            {
                sb.Append("  ").Append(name).Append(newLine);
            }
        }

        /// <summary>Reads the name that starts at <paramref name="index"/> (and, after FROM, the rest of a comma separated list).</summary>
        private static void Collect(List<Token> tokens, int index, Names target, HashSet<string> ctes, Names temps, bool allowList)
        {
            var i = index;
            while (i < tokens.Count && tokens[i].IsNamePart && !(tokens[i].Kind == TokenKind.Word && Keywords.IsReserved(tokens[i].Text)))
            {
                var parts = new List<string> { tokens[i].Text };
                var j = i + 1;
                while (j + 1 < tokens.Count && tokens[j].Kind == TokenKind.Dot && tokens[j + 1].IsNamePart)
                {
                    parts.Add(tokens[j + 1].Text);
                    j += 2;
                }

                var name = string.Join(".", parts);
                if (parts[0].StartsWith("#", StringComparison.Ordinal))
                {
                    temps.Add(name);
                }
                else if (!(parts.Count == 1 && ctes.Contains(tokens[i].IdentifierValue)))
                {
                    target.Add(name);
                }

                if (!allowList)
                {
                    return;
                }

                if (j < tokens.Count && tokens[j].IsWord("AS"))
                {
                    j++;
                }

                if (j < tokens.Count && tokens[j].IsIdentifier && !Keywords.CannotBeAlias(tokens[j].Text))
                {
                    j++;
                }

                if (j + 1 < tokens.Count && tokens[j].Kind == TokenKind.Comma)
                {
                    i = j + 1;
                    continue;
                }

                return;
            }
        }

        private static void Declared(List<Token> tokens, int i, Names target, Names temps)
        {
            var j = i + 1;
            while (j < tokens.Count && (tokens[j].IsWord("OR") || tokens[j].IsWord("ALTER") || tokens[j].IsWord("UNIQUE") ||
                                        tokens[j].IsWord("CLUSTERED") || tokens[j].IsWord("NONCLUSTERED")))
            {
                j++;
            }

            if (j >= tokens.Count)
            {
                return;
            }

            var type = tokens[j].Upper;
            if (type != "TABLE" && type != "VIEW" && type != "PROC" && type != "PROCEDURE" && type != "FUNCTION" && type != "TRIGGER" &&
                type != "SCHEMA" && type != "SEQUENCE" && type != "TYPE")
            {
                return;
            }

            j++;
            while (j < tokens.Count && (tokens[j].IsWord("IF") || tokens[j].IsWord("NOT") || tokens[j].IsWord("EXISTS")))
            {
                j++;
            }

            if (j < tokens.Count && tokens[j].IsNamePart)
            {
                var parts = new List<string> { tokens[j].Text };
                while (j + 2 < tokens.Count && tokens[j + 1].Kind == TokenKind.Dot && tokens[j + 2].IsNamePart)
                {
                    parts.Add(tokens[j + 2].Text);
                    j += 2;
                }

                var name = string.Join(".", parts);
                if (parts[0].StartsWith("#", StringComparison.Ordinal))
                {
                    temps.Add(name);
                }
                else
                {
                    target.Add(type.ToLowerInvariant() + " " + name);
                }
            }
        }

        private sealed class Names
        {
            private readonly HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public int Count => names.Count;

            public void Add(string name) => names.Add(name);

            public IEnumerable<string> Ordered() => names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        }
    }
}
