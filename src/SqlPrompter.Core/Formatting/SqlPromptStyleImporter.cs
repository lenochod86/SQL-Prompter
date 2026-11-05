using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SqlPrompter.Core.Formatting
{
    /// <summary>What was taken from a style file and what was not.</summary>
    public sealed class StyleImportResult
    {
        internal StyleImportResult(FormatStyle style, List<string> applied, List<string> notes)
        {
            Style = style;
            Applied = applied;
            Notes = notes;
        }

        public FormatStyle Style { get; }

        /// <summary>The options that were set, as <c>option = value</c>.</summary>
        public IReadOnlyList<string> Applied { get; }

        /// <summary>Options of the file that this formatter does not have or cannot map.</summary>
        public IReadOnlyList<string> Notes { get; }
    }

    /// <summary>
    /// Reads a SQL Prompt style (<c>.json</c>) into a <see cref="FormatStyle"/>. Only the options the formatter understands are used; the
    /// others are reported. Options the file does not mention keep the values of <paramref name="baseStyle"/>.
    /// </summary>
    public static class SqlPromptStyleImporter
    {
        /// <exception cref="FormatException">The text is not JSON.</exception>
        public static StyleImportResult Import(string json, FormatStyle baseStyle = null)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null)
            {
                throw new FormatException("The style file is not a JSON object.");
            }

            var style = (baseStyle ?? new FormatStyle()).Clone();
            var applied = new List<string>();
            var notes = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);

            string Text(string path) => Find(root, path, used) as string;
            bool? Flag(string path) => Find(root, path, used) as bool?;
            int? Number(string path) => Find(root, path, used) is double d ? (int?)(int)d : null;

            var spaces = Text("whitespace.spacesOrTabs");
            if (spaces != null)
            {
                style.UseTabs = !string.Equals(spaces, "spaces", StringComparison.OrdinalIgnoreCase);
                applied.Add("useTabs = " + style.UseTabs);
            }

            var commas = Flag("lists.placeCommasBeforeItems");
            if (commas != null)
            {
                style.CommaBeforeItems = commas.Value;
                applied.Add("commaBeforeItems = " + commas.Value);
            }

            SetCase("casing.reservedKeywords", v => style.KeywordCase = v, "keywordCase");
            SetCase("casing.builtInFunctions", v => style.FunctionCase = v, "functionCase");
            SetCase("casing.builtInDataTypes", v => style.DataTypeCase = v, "dataTypeCase");

            SetLimit("dml.collapseStatementsShorterThan", "dml.collapseShortStatements", v => style.CollapseStatementsShorterThan = v, "collapseStatementsShorterThan");
            SetLimit("dml.collapseSubqueriesShorterThan", "dml.collapseShortSubqueries", v => style.CollapseSubqueriesShorterThan = v, "collapseSubqueriesShorterThan");
            SetLimit("ddl.collapseStatementsShorterThan", "ddl.collapseShortStatements", v => style.CollapseDdlShorterThan = v, "collapseDdlShorterThan");

            var distinct = Flag("dml.addNewLineAfterDistinctAndTopClauses");
            if (distinct != null)
            {
                style.NewLineAfterDistinctAndTop = distinct.Value;
                applied.Add("newLineAfterDistinctAndTop = " + distinct.Value);
            }

            var where = Text("dml.listItems.placeWhereConditionOnNewLine");
            if (where != null)
            {
                style.ConditionsOnSeparateLines = !string.Equals(where, "never", StringComparison.OrdinalIgnoreCase);
                applied.Add("conditionsOnSeparateLines = " + style.ConditionsOnSeparateLines);
            }

            var andOr = Text("operators.andOr.alignment");
            if (andOr != null)
            {
                style.AndOrBeforeConditions = true;
                applied.Add("andOrBeforeConditions = true (AND and OR start the lines)");
            }

            foreach (var path in Leaves(root, string.Empty))
            {
                if (!used.Contains(path) && !path.StartsWith("metadata.", StringComparison.Ordinal))
                {
                    notes.Add(path);
                }
            }

            return new StyleImportResult(style, applied, notes);

            void SetCase(string path, Action<CaseStyle> set, string name)
            {
                var value = Text(path);
                if (value == null)
                {
                    return;
                }

                CaseStyle parsed;
                if (string.Equals(value, "uppercase", StringComparison.OrdinalIgnoreCase))
                {
                    parsed = CaseStyle.Upper;
                }
                else if (string.Equals(value, "lowercase", StringComparison.OrdinalIgnoreCase))
                {
                    parsed = CaseStyle.Lower;
                }
                else
                {
                    parsed = CaseStyle.Keep;
                }

                set(parsed);
                applied.Add(name + " = " + parsed);
            }

            void SetLimit(string limitPath, string switchPath, Action<int> set, string name)
            {
                var limit = Number(limitPath);
                var on = Flag(switchPath);
                if (limit == null && on == null)
                {
                    return;
                }

                var value = on == false ? 0 : limit ?? 75;
                set(value);
                applied.Add(name + " = " + value);
            }
        }

        private static object Find(Dictionary<string, object> root, string path, HashSet<string> used)
        {
            object current = root;
            foreach (var part in path.Split('.'))
            {
                if (!(current is Dictionary<string, object> map) || !map.TryGetValue(part, out current))
                {
                    return null;
                }
            }

            used.Add(path);
            return current;
        }

        private static IEnumerable<string> Leaves(Dictionary<string, object> map, string prefix)
        {
            foreach (var pair in map)
            {
                var path = prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key;
                if (pair.Value is Dictionary<string, object> child)
                {
                    foreach (var leaf in Leaves(child, path))
                    {
                        yield return leaf;
                    }
                }
                else
                {
                    yield return path;
                }
            }
        }
    }

    /// <summary>A small JSON reader (objects as dictionaries, arrays as lists, numbers as double) so that Core needs no JSON library for free-form files.</summary>
    internal static class MiniJson
    {
        public static object Parse(string text)
        {
            var i = 0;
            text = (text ?? string.Empty).TrimStart('﻿');
            var value = ReadValue(text, ref i);
            SkipSpace(text, ref i);
            if (i != text.Length)
            {
                throw new FormatException("Unexpected text after the JSON value at " + i + ".");
            }

            return value;
        }

        private static object ReadValue(string t, ref int i)
        {
            SkipSpace(t, ref i);
            if (i >= t.Length)
            {
                throw new FormatException("Unexpected end of the JSON text.");
            }

            switch (t[i])
            {
                case '{':
                    var map = new Dictionary<string, object>(StringComparer.Ordinal);
                    i++;
                    SkipSpace(t, ref i);
                    if (i < t.Length && t[i] == '}')
                    {
                        i++;
                        return map;
                    }

                    while (true)
                    {
                        SkipSpace(t, ref i);
                        var key = ReadString(t, ref i);
                        SkipSpace(t, ref i);
                        Expect(t, ref i, ':');
                        map[key] = ReadValue(t, ref i);
                        SkipSpace(t, ref i);
                        if (i < t.Length && t[i] == ',')
                        {
                            i++;
                            continue;
                        }

                        Expect(t, ref i, '}');
                        return map;
                    }

                case '[':
                    var list = new List<object>();
                    i++;
                    SkipSpace(t, ref i);
                    if (i < t.Length && t[i] == ']')
                    {
                        i++;
                        return list;
                    }

                    while (true)
                    {
                        list.Add(ReadValue(t, ref i));
                        SkipSpace(t, ref i);
                        if (i < t.Length && t[i] == ',')
                        {
                            i++;
                            continue;
                        }

                        Expect(t, ref i, ']');
                        return list;
                    }

                case '"':
                    return ReadString(t, ref i);
                default:
                    var start = i;
                    while (i < t.Length && (char.IsLetterOrDigit(t[i]) || t[i] == '-' || t[i] == '+' || t[i] == '.'))
                    {
                        i++;
                    }

                    var word = t.Substring(start, i - start);
                    if (word == "true")
                    {
                        return true;
                    }

                    if (word == "false")
                    {
                        return false;
                    }

                    if (word == "null")
                    {
                        return null;
                    }

                    if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        return number;
                    }

                    throw new FormatException("Unexpected '" + word + "' in the JSON text at " + start + ".");
            }
        }

        private static string ReadString(string t, ref int i)
        {
            Expect(t, ref i, '"');
            var sb = new StringBuilder();
            while (i < t.Length && t[i] != '"')
            {
                if (t[i] == '\\' && i + 1 < t.Length)
                {
                    i++;
                    switch (t[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u' when i + 4 < t.Length:
                            sb.Append((char)Convert.ToInt32(t.Substring(i + 1, 4), 16));
                            i += 4;
                            break;
                        default: sb.Append(t[i]); break;
                    }
                }
                else
                {
                    sb.Append(t[i]);
                }

                i++;
            }

            Expect(t, ref i, '"');
            return sb.ToString();
        }

        private static void Expect(string t, ref int i, char c)
        {
            if (i >= t.Length || t[i] != c)
            {
                throw new FormatException("Expected '" + c + "' in the JSON text at " + i + ".");
            }

            i++;
        }

        private static void SkipSpace(string t, ref int i)
        {
            while (i < t.Length && char.IsWhiteSpace(t[i]))
            {
                i++;
            }
        }
    }
}
