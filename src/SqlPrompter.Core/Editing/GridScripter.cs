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
            return BuildInsert(target, columns, rows, (value, index) => Literal(value));
        }

        public const string TemporaryTableName = "#temptable";

        /// <summary>
        /// The selection as a temporary table: <c>CREATE TABLE</c> with the columns (types inferred from the values, every column nullable,
        /// unique names), the multi-row <c>INSERT</c> and a final <c>DROP TABLE</c>. Null when the text is not a header line followed by a row.
        /// </summary>
        public static string ToTemporaryTable(string gridText)
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

            var names = UniqueNames(lines[0].Split('\t'));
            var rows = lines.Skip(1).Select(l => l.Split('\t')).ToList();
            var types = names.Select((n, i) => InferType(rows.Select(r => i < r.Length ? r[i] : "NULL"))).ToList();
            var text = new StringBuilder();
            text.Append("CREATE TABLE ").Append(TemporaryTableName).Append(" (\r\n");
            for (var i = 0; i < names.Count; i++)
            {
                text.Append("    ").Append(SqlIdentifier.Quote(names[i])).Append(' ').Append(types[i]).Append(" NULL").Append(i < names.Count - 1 ? "," : string.Empty).Append("\r\n");
            }

            text.Append(");\r\n\r\n");
            var isText = types.Select(t => t.StartsWith("nvarchar", StringComparison.Ordinal)).ToList();
            text.Append(BuildInsert(TemporaryTableName, names.Select(n => SqlIdentifier.Quote(n)).ToList(), rows, (value, index) => !isText[index] && value.Length == 0 ? "NULL" : Literal(value)));
            text.Append("\r\n-- Work with the data here.\r\n\r\nDROP TABLE ").Append(TemporaryTableName).Append(";\r\n");
            return text.ToString();
        }

        /// <summary>Column names made valid and unique: an empty name or "(No column name)" becomes ColumnN, a repeated one gets _2, _3 …</summary>
        internal static List<string> UniqueNames(IReadOnlyList<string> header)
        {
            var result = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++)
            {
                var name = header[i].Trim();
                if (name.Length == 0 || string.Equals(name, "(No column name)", StringComparison.OrdinalIgnoreCase))
                {
                    name = "Column" + (i + 1);
                }

                var candidate = name;
                for (var n = 2; !used.Add(candidate); n++)
                {
                    candidate = name + "_" + n;
                }

                result.Add(candidate);
            }

            return result;
        }

        /// <summary>The type that holds all the values of a column (NULL and empty cells are ignored); nvarchar when nothing fits better.</summary>
        internal static string InferType(IEnumerable<string> values)
        {
            var present = values.Where(v => v != "NULL" && v.Length > 0).ToList();
            if (present.Count == 0)
            {
                return "nvarchar(max)";
            }

            if (present.All(IsPlainNumber))
            {
                if (present.All(v => v.IndexOf('.') < 0))
                {
                    return present.All(v => long.TryParse(v, out var n) && n >= int.MinValue && n <= int.MaxValue) ? "int" : "bigint";
                }

                var scale = present.Max(v => v.IndexOf('.') < 0 ? 0 : v.Length - v.IndexOf('.') - 1);
                var whole = present.Max(v => (v.IndexOf('.') < 0 ? v.Length : v.IndexOf('.')) - (v[0] == '-' ? 1 : 0));
                return "decimal(" + Math.Min(38, whole + scale) + ", " + scale + ")";
            }

            if (present.All(v => Guid.TryParseExact(v, "D", out _)))
            {
                return "uniqueidentifier";
            }

            if (present.All(IsDateOnly))
            {
                return "date";
            }

            if (present.All(IsDateTime))
            {
                var fraction = present.Max(v => v.IndexOf('.') < 0 ? 0 : v.Length - v.IndexOf('.') - 1);
                return "datetime2(" + Math.Min(7, fraction) + ")";
            }

            var longest = present.Max(v => v.Length);
            return longest > 2000 ? "nvarchar(max)" : "nvarchar(" + Math.Max(50, (longest + 49) / 50 * 50) + ")";
        }

        private static bool IsDateOnly(string v) =>
            v.Length == 10 && DateTime.TryParseExact(v, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

        private static bool IsDateTime(string v) =>
            v.Length >= 19 && v.Length <= 27 && DateTime.TryParseExact(v.Length > 19 ? v.Substring(0, 19) : v, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _) &&
            (v.Length == 19 || (v[19] == '.' && v.Substring(20).All(char.IsDigit) && v.Length > 20));

        private static string BuildInsert(string target, IReadOnlyList<string> columns, IReadOnlyList<string[]> rows, Func<string, int, string> literal)
        {
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
                    var cells = columns.Select((c, index) => index < chunk[i].Length ? literal(chunk[i][index], index) : "NULL");
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
