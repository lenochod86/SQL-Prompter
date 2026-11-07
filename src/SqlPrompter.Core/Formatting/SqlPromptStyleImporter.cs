using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SqlPrompter.Core.Formatting
{
    /// <summary>What was taken from a style file and what was not.</summary>
    public sealed class StyleImportResult
    {
        internal StyleImportResult(FormatStyle style, List<string> applied, List<string> notes, string name = null, List<string> approximate = null)
        {
            Style = style;
            Applied = applied;
            Notes = notes;
            Name = name;
            Approximate = approximate ?? new List<string>();
        }

        /// <summary>Options that were mapped to the nearest option of this formatter, so the result may differ a little from SQL Prompt (#305).</summary>
        public IReadOnlyList<string> Approximate { get; }

        /// <summary>The name the file gives the style (<c>metadata.name</c>); null when it has none.</summary>
        public string Name { get; }

        public FormatStyle Style { get; }

        /// <summary>The options that were set, as <c>option = value</c>.</summary>
        public IReadOnlyList<string> Applied { get; }

        /// <summary>Options of the file that this formatter does not take over, each with the reason or the decision (<c>path: reason</c>).</summary>
        public IReadOnlyList<string> Notes { get; }
    }

    /// <summary>
    /// Reads a SQL Prompt style (<c>.json</c>) into a <see cref="FormatStyle"/>. Only the options the formatter understands are used; the
    /// others are reported. Options the file does not mention keep the values of <paramref name="baseStyle"/>.
    /// </summary>
    public static class SqlPromptStyleImporter
    {
        private static readonly string[] Groups =
        {
            "whitespace", "lists", "parentheses", "casing", "dml", "ddl", "controlFlow", "cte", "variables", "joinStatements", "insertStatements", "caseExpressions", "operators",
        };

        /// <summary>
        /// Reads the style from <paramref name="json"/> and adds it to <paramref name="styles"/> as a new named style (#304): the name comes from the file's
        /// metadata, otherwise from <paramref name="fileName"/> (without extension), with a number added when the name is taken. The existing styles are not touched.
        /// </summary>
        /// <exception cref="FormatException">The text is not JSON, or is JSON that is not a style.</exception>
        public static NamedFormatStyle ImportInto(IList<NamedFormatStyle> styles, string json, string fileName, out StyleImportResult result, FormatStyle baseStyle = null)
        {
            result = Import(json, baseStyle);
            var name = string.IsNullOrWhiteSpace(result.Name) ? NameOfFile(fileName) : result.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Imported style";
            }

            var unique = name;
            for (var i = 2; styles.Any(s => string.Equals(s.Name, unique, StringComparison.OrdinalIgnoreCase)); i++)
            {
                unique = name + " " + i;
            }

            var imported = new NamedFormatStyle(unique, result.Style);
            styles.Add(imported);
            return imported;
        }

        /// <summary>The file name without folder and extension; both kinds of path separator count, whatever the platform.</summary>
        private static string NameOfFile(string fileName)
        {
            var name = (fileName ?? string.Empty).Substring((fileName ?? string.Empty).LastIndexOfAny(new[] { (char)92, '/' }) + 1);
            var dot = name.LastIndexOf('.');
            return (dot > 0 ? name.Substring(0, dot) : name).Trim();
        }

        /// <exception cref="FormatException">The text is not JSON, or is JSON that is not a style.</exception>
        public static StyleImportResult Import(string json, FormatStyle baseStyle = null)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null)
            {
                throw new FormatException("The style file is not a JSON object.");
            }

            if (!root.ContainsKey("metadata") && !Groups.Any(root.ContainsKey))
            {
                throw new FormatException("The file is not a SQL Prompt style (none of its option groups was found).");
            }

            var style = (baseStyle ?? new FormatStyle()).Clone();
            var applied = new List<string>();
            var approximate = new List<string>();
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
                // SQL Prompt has several alignments of AND / OR; this formatter has one, with AND and OR at the start of the line.
                style.AndOrBeforeConditions = true;
                approximate.Add("operators.andOr.alignment = " + andOr + " -> andOrBeforeConditions = true (AND and OR start the lines; the variants of the alignment are not told apart)");
            }

            var tabStops = Flag("lists.alignItemsToTabStops");
            if (tabStops != null)
            {
                approximate.Add("lists.alignItemsToTabStops = " + tabStops.Value + " -> the formatter always aligns the items of a list to tab stops");
            }

            var groupOrder = Text("dml.listItems.placeGroupByAndOrderByOnNewLine");
            if (groupOrder != null)
            {
                style.GroupOrderBySeparateLines = !string.Equals(groupOrder, "never", StringComparison.OrdinalIgnoreCase);
                applied.Add("groupOrderBySeparateLines = " + style.GroupOrderBySeparateLines);
            }

            var onNewLine = Flag("joinStatements.on.placeOnNewLine");
            if (onNewLine != null)
            {
                style.OnOnNewLine = onNewLine.Value;
                applied.Add("onOnNewLine = " + onNewLine.Value);
            }

            foreach (var path in Leaves(root, string.Empty))
            {
                if (!used.Contains(path) && !path.StartsWith("metadata.", StringComparison.Ordinal))
                {
                    notes.Add(path + ": " + ReasonFor(path));
                }
            }

            return new StyleImportResult(style, applied, notes, (Find(root, "metadata.name", used) as string), approximate);

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

        /// <summary>
        /// The decision for an option that is not taken over (#305): "leave out" has a reason that stays, "planned" options are the next ones to implement in the
        /// formatter (the list is kept in the issue).
        /// </summary>
        private static string ReasonFor(string path)
        {
            switch (path)
            {
                case "casing.useObjectDefinitionCase":
                    return "left out: the case of names from the database definition needs the database, the formatter works on the text only";
                case "whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator":
                    return "left out: the formatter always keeps one blank line after a batch separator";
                case "lists.alignComments":
                    return "left out: comments are kept where they are written";
                case "lists.addSpaceBeforeComma":
                    return "left out: the formatter has one fixed spacing around the commas of a list";
                case "ddl.placeFirstProcedureParameterOnNewLine":
                case "ddl.alignDataTypesAndConstraints":
                case "ddl.placeConstraintColumnsOnNewLines":
                case "ddl.parenthesisStyle":
                case "ddl.indentParenthesesContents":
                    return "planned: the layout of DDL statements";
                case "parentheses.parenthesisStyle":
                case "parentheses.indentParenthesesContents":
                case "parentheses.collapseParenthesesShorterThan":
                    return "planned: the layout of parentheses";
                default:
                    break;
            }

            if (path.StartsWith("cte.", StringComparison.Ordinal))
            {
                return "planned: the layout of CTEs";
            }

            if (path.StartsWith("insertStatements.", StringComparison.Ordinal))
            {
                return "planned: the layout of INSERT columns and values";
            }

            if (path.StartsWith("caseExpressions.", StringComparison.Ordinal))
            {
                return "planned: the layout of CASE expressions";
            }

            if (path.StartsWith("operators.", StringComparison.Ordinal))
            {
                return "planned: the alignment of operators (comparison, BETWEEN, IN)";
            }

            if (path.StartsWith("controlFlow.", StringComparison.Ordinal))
            {
                return "planned: the layout of control flow";
            }

            if (path.StartsWith("joinStatements.", StringComparison.Ordinal) || path.StartsWith("variables.", StringComparison.Ordinal) || path.StartsWith("lists.", StringComparison.Ordinal))
            {
                return "planned";
            }

            return "not supported yet";
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
