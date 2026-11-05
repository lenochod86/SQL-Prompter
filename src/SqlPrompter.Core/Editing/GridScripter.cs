using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// Turns rows copied from the results grid (tab separated, the first line holds the column names, as "Copy with Headers" makes them)
    /// into INSERT statements. The grid text carries no types, so a value is written by its look: <c>NULL</c> as NULL, a plain number
    /// as a number, everything else as an <c>N'...'</c> string (a number with a leading zero stays a string).
    /// </summary>
    public static class GridScripter
    {
        public const int RowsPerStatement = 1000;

        /// <summary>Null when the text is not a header line followed by at least one row.</summary>
        public static string ToInsert(string gridText, string table)
        {
            var lines = (gridText ?? string.Empty).Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            if (lines.Count < 2)
            {
                return null;
            }

            var columns = lines[0].Split('\t').Select(c => SqlIdentifier.Quote(c.Trim())).ToList();
            var rows = lines.Skip(1).Select(l => l.Split('\t')).ToList();
            var target = string.IsNullOrWhiteSpace(table) ? "dbo.TableName" : table;
            var text = new StringBuilder();
            for (var start = 0; start < rows.Count; start += RowsPerStatement)
            {
                if (start > 0)
                {
                    text.Append("\r\n");
                }

                text.Append("INSERT INTO ").Append(target).Append(" (").Append(string.Join(", ", columns)).Append(")\r\nVALUES\r\n");
                var chunk = rows.Skip(start).Take(RowsPerStatement).ToList();
                for (var i = 0; i < chunk.Count; i++)
                {
                    var cells = columns.Select((c, index) => index < chunk[i].Length ? Literal(chunk[i][index]) : "NULL");
                    text.Append("    (").Append(string.Join(", ", cells)).Append(i == chunk.Count - 1 ? ");" : "),").Append("\r\n");
                }
            }

            return text.ToString();
        }

        public static string Literal(string value)
        {
            if (value == "NULL")
            {
                return "NULL";
            }

            if (IsPlainNumber(value))
            {
                return value;
            }

            return "N'" + value.Replace("'", "''") + "'";
        }

        private static bool IsPlainNumber(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 18)
            {
                return false;
            }

            var digits = value[0] == '-' ? value.Substring(1) : value;
            var dot = digits.IndexOf('.');
            var whole = dot < 0 ? digits : digits.Substring(0, dot);
            var fraction = dot < 0 ? string.Empty : digits.Substring(dot + 1);
            if (whole.Length == 0 || !whole.All(char.IsDigit) || (dot >= 0 && (fraction.Length == 0 || !fraction.All(char.IsDigit))))
            {
                return false;
            }

            return whole == "0" || whole[0] != '0';
        }

        /// <summary>The first table after FROM in the query (schema and name as written), or null.</summary>
        public static string GuessTable(string query)
        {
            var tokens = Lexer.Tokenize(query ?? string.Empty).Where(t => t.Kind != TokenKind.Comment).ToList();
            for (var i = 0; i + 1 < tokens.Count; i++)
            {
                if (!tokens[i].IsWord("FROM") || !tokens[i + 1].IsNamePart || tokens[i + 1].IsWord("SELECT"))
                {
                    continue;
                }

                var parts = new List<string> { tokens[i + 1].Text };
                var j = i + 2;
                while (j + 1 < tokens.Count && tokens[j].Kind == TokenKind.Dot && tokens[j + 1].IsNamePart)
                {
                    parts.Add(tokens[j + 1].Text);
                    j += 2;
                }

                return string.Join(".", parts);
            }

            return null;
        }
    }
}
