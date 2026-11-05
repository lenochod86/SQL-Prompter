using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// The refactoring "qualify object names": a table, view, function or procedure written without a schema gets the schema it resolves to
    /// (<c>FROM Users</c> becomes <c>FROM dbo.Users</c>). Names of CTEs, aliases, temporary tables and variables, and names that do not
    /// exist in the metadata, stay as they are.
    /// </summary>
    public static class NameQualifier
    {
        /// <summary>Qualifies the names that start within <paramref name="from"/>…<paramref name="to"/>.</summary>
        public static string Qualify(string text, int from, int to, IDatabaseMetadata metadata, out int qualified)
        {
            qualified = 0;
            if (string.IsNullOrEmpty(text) || metadata == null)
            {
                return text;
            }

            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            var local = LocalNames(tokens);
            var inserts = new List<KeyValuePair<int, string>>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var anchor = tokens[i];
                if (anchor.Kind != TokenKind.Word)
                {
                    continue;
                }

                var first = NameAfter(tokens, i);
                while (first >= 0)
                {
                    var name = tokens[first];
                    if (name.Start >= from && name.End <= to && !IsQualified(tokens, first) && !local.Contains(name.IdentifierValue) && CanBeObject(tokens, i, first))
                    {
                        var found = metadata.FindObject(null, name.IdentifierValue);
                        if (found != null && !string.IsNullOrEmpty(found.Schema) && !found.IsSystem)
                        {
                            inserts.Add(new KeyValuePair<int, string>(name.Start, SqlIdentifier.Quote(found.Schema) + "."));
                            qualified++;
                        }
                    }

                    // FROM a, b: the next source of a list.
                    first = tokens[i].IsWord("FROM") ? NextInList(tokens, first) : -1;
                }
            }

            if (inserts.Count == 0)
            {
                return text;
            }

            var result = new StringBuilder(text);
            foreach (var insert in inserts.OrderByDescending(x => x.Key).GroupBy(x => x.Key).Select(g => g.First()))
            {
                result.Insert(insert.Key, insert.Value);
            }

            return result.ToString();
        }

        /// <summary>The index of the name token that follows a keyword which is followed by an object name, or -1.</summary>
        private static int NameAfter(List<Token> tokens, int i)
        {
            var word = tokens[i].Upper;
            var next = i + 1;
            switch (word)
            {
                case "FROM":
                case "JOIN":
                case "EXEC":
                case "EXECUTE":
                    break;
                case "INTO":
                    break;
                case "UPDATE":
                    break;
                case "TABLE" when i > 0 && tokens[i - 1].IsWord("TRUNCATE"):
                    break;
                case "DELETE":
                    if (next < tokens.Count && tokens[next].IsWord("FROM"))
                    {
                        return -1; // the name after FROM is found through FROM
                    }

                    break;
                default:
                    return -1;
            }

            return next < tokens.Count && tokens[next].IsNamePart && !(tokens[next].Kind == TokenKind.Word && Keywords.IsReserved(tokens[next].Text)) ? next : -1;
        }

        private static int NextInList(List<Token> tokens, int name)
        {
            var i = name + 1;
            if (i < tokens.Count && tokens[i].IsWord("AS"))
            {
                i++;
            }

            if (i < tokens.Count && tokens[i].IsIdentifier && !Keywords.CannotBeAlias(tokens[i].Text))
            {
                i++;
            }

            return i + 1 < tokens.Count && tokens[i].Kind == TokenKind.Comma && tokens[i + 1].IsNamePart ? i + 1 : -1;
        }

        private static bool IsQualified(List<Token> tokens, int name) =>
            (name + 1 < tokens.Count && tokens[name + 1].Kind == TokenKind.Dot) || (name > 0 && tokens[name - 1].Kind == TokenKind.Dot);

        /// <summary>A name after FROM that is followed by <c>.</c> is a call chain, a name before <c>(</c> after INTO/UPDATE is not an object.</summary>
        private static bool CanBeObject(List<Token> tokens, int anchor, int name)
        {
            var word = tokens[anchor].Upper;
            var after = name + 1 < tokens.Count ? tokens[name + 1] : null;
            if (word == "INTO" || word == "UPDATE")
            {
                // INSERT INTO t / SELECT … INTO t / MERGE INTO t; UPDATE t SET (not UPDATE STATISTICS).
                return word != "UPDATE" || (after != null && !tokens[name].IsWord("STATISTICS"));
            }

            return true;
        }

        /// <summary>Names that look like objects but are defined in the script: CTEs, aliases (table or column) and temporary names.</summary>
        private static HashSet<string> LocalNames(List<Token> tokens)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.Word && token.Text.StartsWith("#", StringComparison.Ordinal))
                {
                    names.Add(token.Text);
                }

                // CTE: WITH name [(columns)] AS ( or , name AS (
                if (token.IsNamePart && i + 2 < tokens.Count && tokens[i + 1].IsWord("AS") && tokens[i + 2].Kind == TokenKind.OpenParen &&
                    i > 0 && (tokens[i - 1].IsWord("WITH") || tokens[i - 1].Kind == TokenKind.Comma))
                {
                    names.Add(token.IdentifierValue);
                }

                // Alias after a table source: FROM|JOIN name[.name…] [AS] alias
                if (token.IsWord("FROM") || token.IsWord("JOIN") || token.IsWord("UPDATE") || token.IsWord("INTO"))
                {
                    var j = i + 1;
                    while (j < tokens.Count && tokens[j].IsNamePart)
                    {
                        j++;
                        if (j < tokens.Count && tokens[j].Kind == TokenKind.Dot)
                        {
                            j++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (j > i + 1 && j < tokens.Count && tokens[j].IsWord("AS"))
                    {
                        j++;
                    }

                    if (j > i + 1 && j < tokens.Count && tokens[j].IsIdentifier && !Keywords.CannotBeAlias(tokens[j].Text))
                    {
                        names.Add(tokens[j].IdentifierValue);
                    }
                }
            }

            return names;
        }
    }
}
