using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Completion
{
    /// <summary>The text of an INSERT with its column list and of an EXEC with its parameters.</summary>
    public static class InsertBuilder
    {
        /// <summary>The columns an INSERT supplies: all except identity, computed and rowversion columns.</summary>
        public static IReadOnlyList<DbColumn> InsertableColumns(IReadOnlyList<DbColumn> columns) =>
            (columns ?? Array.Empty<DbColumn>()).Where(c => c.IsInsertable).ToList();

        /// <summary>
        /// The table name followed by its column list and, per the settings, by VALUES with one placeholder per column or a new line.
        /// </summary>
        public static string TableWithColumns(string tableText, IReadOnlyList<DbColumn> columns, InsertSettings settings, EditorLayout editor, bool quote)
        {
            var text = new StringBuilder(tableText);
            var names = columns.Select(c => SqlIdentifier.Quote(c.Name, quote)).ToList();
            text.Append(ParenthesizedList(names, settings.Layout, editor, editor.LinePrefix.Length + tableText.Length));
            AppendEnding(text, columns, settings, editor);
            return text.ToString();
        }

        /// <summary>
        /// What goes after an opening parenthesis the user typed behind the table name: the columns, the closing parenthesis and the
        /// ending of the statement.
        /// </summary>
        public static string ColumnsAfterParenthesis(IReadOnlyList<DbColumn> columns, InsertSettings settings, EditorLayout editor, bool quote)
        {
            var names = columns.Select(c => SqlIdentifier.Quote(c.Name, quote)).ToList();
            var text = new StringBuilder();
            switch (settings.Layout)
            {
                case ListLayout.OneLine:
                    text.Append(string.Join(", ", names)).Append(')');
                    break;
                case ListLayout.EachOnItsOwnLine:
                    for (var i = 0; i < names.Count; i++)
                    {
                        text.Append(editor.NewLine).Append(editor.LineIndent).Append(editor.IndentUnit).Append(names[i]).Append(i < names.Count - 1 ? "," : string.Empty);
                    }

                    text.Append(editor.NewLine).Append(editor.LineIndent).Append(')');
                    break;
                default:
                    text.Append(Wrap(names, editor, editor.LinePrefix.Length)).Append(')');
                    break;
            }

            AppendEnding(text, columns, settings, editor);
            return text.ToString();
        }

        private static void AppendEnding(StringBuilder text, IReadOnlyList<DbColumn> columns, InsertSettings settings, EditorLayout editor)
        {
            text.Append(editor.NewLine).Append(editor.LineIndent);
            if (settings.ValuesMode == InsertValuesMode.Values)
            {
                text.Append("VALUES");
                var values = columns.Select(c => SqlDefaults.ValueFor(c.TypeName, c.IsNullable)).ToList();
                text.Append(ParenthesizedList(values, settings.Layout, editor, editor.LineIndent.Length + "VALUES".Length));
            }
        }

        /// <summary>
        /// " (a, b)" in one line, "(" and ")" on lines of their own with one item per line, or the items wrapped to the width of the editor.
        /// </summary>
        internal static string ParenthesizedList(IReadOnlyList<string> items, ListLayout layout, EditorLayout editor, int column)
        {
            switch (layout)
            {
                case ListLayout.OneLine:
                    return " (" + string.Join(", ", items) + ")";
                case ListLayout.EachOnItsOwnLine:
                    var text = new StringBuilder();
                    text.Append(editor.NewLine).Append(editor.LineIndent).Append('(');
                    for (var i = 0; i < items.Count; i++)
                    {
                        text.Append(editor.NewLine).Append(editor.LineIndent).Append(editor.IndentUnit).Append(items[i]).Append(i < items.Count - 1 ? "," : string.Empty);
                    }

                    return text.Append(editor.NewLine).Append(editor.LineIndent).Append(')').ToString();
                default:
                    return " (" + Wrap(items, editor, column + 2) + ")";
            }
        }

        /// <summary>The items separated by commas, continued on a new line when the next one would pass the width of the editor.</summary>
        private static string Wrap(IReadOnlyList<string> items, EditorLayout editor, int startColumn)
        {
            var text = new StringBuilder();
            var column = startColumn;
            for (var i = 0; i < items.Count; i++)
            {
                var piece = items[i] + (i < items.Count - 1 ? "," : string.Empty);
                if (i > 0)
                {
                    if (column + 1 + piece.Length > editor.LineWidth)
                    {
                        text.Append(editor.NewLine).Append(editor.LineIndent).Append(editor.IndentUnit);
                        column = editor.LineIndent.Length + editor.IndentUnit.Length;
                    }
                    else
                    {
                        text.Append(' ');
                        column++;
                    }
                }

                text.Append(piece);
                column += piece.Length;
            }

            return text.ToString();
        }

        /// <summary>
        /// The procedure name followed by all its parameters as <c>@name = value</c>, the first on the same line and the others
        /// aligned with it by the formatting style (<see cref="FormattedList"/>). A parameter with a default value gets it as written, an OUTPUT parameter the variable of the same name.
        /// </summary>
        public static string ProcedureCall(string procedureText, IReadOnlyList<DbParameter> parameters, EditorLayout editor)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return procedureText;
            }

            var items = parameters.Select(parameter => parameter.Name + " = " + (parameter.IsOutput
                ? parameter.Name + " OUTPUT"
                : parameter.HasDefault ? parameter.DefaultText ?? "NULL" : SqlDefaults.ValueFor(parameter.TypeName, false))).ToList();
            return procedureText + " " + FormattedList.Join(items, editor.LinePrefix + procedureText + " ", editor);
        }
        /// <summary>The <c>DECLARE</c> lines for the OUTPUT parameters, to go above the EXEC; empty when there are none.</summary>
        public static string OutputDeclarations(IReadOnlyList<DbParameter> parameters, EditorLayout editor)
        {
            var text = new StringBuilder();
            foreach (var parameter in (parameters ?? Array.Empty<DbParameter>()).Where(p => p.IsOutput))
            {
                text.Append(editor.LineIndent).Append("DECLARE ").Append(parameter.Name).Append(' ').Append(parameter.TypeName).Append(';').Append(editor.NewLine);
            }

            return text.ToString();
        }

        /// <summary>Whitespace as wide as the text; tabs stay tabs so that the alignment holds in the editor.</summary>
        private static string Align(string text) => new string(text.Select(c => c == '\t' ? '\t' : ' ').ToArray());
    }
}
