using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// Formats of the selection of the results grid that are needed again and again (#211): an <c>IN</c> list, <c>VALUES</c>, JSON, CSV and a Markdown table.
    /// The input is what "Copy with Headers" puts on the clipboard (tab separated, the column names in the first line). The grid text carries no types,
    /// so a value is written by its look (<see cref="GridScripter.InferType"/>): numbers bare, everything else quoted, <c>NULL</c> as NULL.
    /// </summary>
    public static class GridFormats
    {
        private sealed class Table
        {
            public List<string> Names;
            public List<string[]> Rows;
            public List<bool> Numeric;
        }

        private static Table Read(string gridText)
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

            var names = GridScripter.UniqueNames(lines[0].Split('\t'));
            var rows = lines.Skip(1).Select(l => l.Split('\t')).ToList();
            var numeric = names.Select((n, i) =>
            {
                var type = GridScripter.InferType(rows.Select(r => i < r.Length ? r[i] : "NULL"));
                return type == "int" || type == "bigint" || type.StartsWith("decimal", StringComparison.Ordinal);
            }).ToList();
            return new Table { Names = names, Rows = rows, Numeric = numeric };
        }

        private static string Cell(string[] row, int index) => index < row.Length ? row[index] : "NULL";

        /// <summary>A value as a T-SQL literal: NULL, a bare number, or a string in quotes (N'...' when it has characters outside ASCII), quotes doubled.</summary>
        private static string Literal(string value, bool numeric)
        {
            if (value == "NULL")
            {
                return "NULL";
            }

            if (numeric && value.Length > 0)
            {
                return value;
            }

            var prefix = value.Any(c => c > 127) ? "N" : string.Empty;
            return prefix + "'" + value.Replace("'", "''") + "'";
        }

        /// <summary>
        /// The first column as <c>IN ('a', 'b', 'c')</c>. A list that does not fit on one line of <paramref name="lineWidth"/> has one value per line
        /// between <c>IN (</c> and <c>)</c>, indented by four spaces. Null when the text has no rows.
        /// </summary>
        public static string AsInList(string gridText, bool distinct = true, int lineWidth = 100, string newLine = "\r\n")
        {
            var table = Read(gridText);
            if (table == null)
            {
                return null;
            }

            var values = table.Rows.Select(r => Literal(Cell(r, 0), table.Numeric[0])).ToList();
            if (distinct)
            {
                values = values.Distinct(StringComparer.Ordinal).ToList();
            }

            var whole = "IN (" + string.Join(", ", values) + ")";
            if (whole.Length <= lineWidth)
            {
                return whole;
            }

            return "IN (" + newLine + string.Join("," + newLine, values.Select(v => "    " + v)) + newLine + ")";
        }

        /// <summary>The rows as <c>VALUES (1, 'a'), (2, 'b')</c>, one row per line.</summary>
        public static string AsValues(string gridText, string newLine = "\r\n")
        {
            var table = Read(gridText);
            if (table == null)
            {
                return null;
            }

            return "VALUES" + newLine + string.Join("," + newLine, table.Rows.Select(r => "(" + string.Join(", ", table.Names.Select((n, i) => Literal(Cell(r, i), table.Numeric[i]))) + ")"));
        }

        /// <summary>A JSON array of objects with the column names as keys; numbers bare, NULL as null.</summary>
        public static string AsJson(string gridText, string newLine = "\r\n")
        {
            var table = Read(gridText);
            if (table == null)
            {
                return null;
            }

            var objects = table.Rows.Select(r => "  {" + string.Join(", ", table.Names.Select((n, i) => JsonString(n) + ": " + JsonValue(Cell(r, i), table.Numeric[i]))) + "}");
            return "[" + newLine + string.Join("," + newLine, objects) + newLine + "]";
        }

        private static string JsonValue(string value, bool numeric)
        {
            if (value == "NULL")
            {
                return "null";
            }

            return numeric && value.Length > 0 ? value : JsonString(value);
        }

        private static string JsonString(string value)
        {
            var text = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            text.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            text.Append(c);
                        }

                        break;
                }
            }

            return text.Append('"').ToString();
        }

        /// <summary>CSV with a header line (RFC 4180: a field with a comma, a quote or a line break is quoted); NULL is an empty field.</summary>
        public static string AsCsv(string gridText, string newLine = "\r\n")
        {
            var table = Read(gridText);
            if (table == null)
            {
                return null;
            }

            string Field(string value) => value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
            var lines = new List<string> { string.Join(",", table.Names.Select(Field)) };
            lines.AddRange(table.Rows.Select(r => string.Join(",", table.Names.Select((n, i) => Cell(r, i) == "NULL" ? string.Empty : Field(Cell(r, i))))));
            return string.Join(newLine, lines);
        }

        /// <summary>A Markdown table with the column names as the header; a pipe in a value is escaped, NULL is shown as NULL.</summary>
        public static string AsMarkdown(string gridText, string newLine = "\r\n")
        {
            var table = Read(gridText);
            if (table == null)
            {
                return null;
            }

            string Field(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
            var lines = new List<string>
            {
                "| " + string.Join(" | ", table.Names.Select(Field)) + " |",
                "| " + string.Join(" | ", table.Names.Select((n, i) => table.Numeric[i] ? "---:" : "---")) + " |",
            };
            lines.AddRange(table.Rows.Select(r => "| " + string.Join(" | ", table.Names.Select((n, i) => Field(Cell(r, i)))) + " |"));
            return string.Join(newLine, lines);
        }
    }
}
