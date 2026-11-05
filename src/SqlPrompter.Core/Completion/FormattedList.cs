using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Formatting;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Lays out a list of items (the columns of an expanded <c>*</c>, the parameters of an <c>EXEC</c>) over several lines the way the formatter
    /// lays out a select list: the first item stays where the insertion starts and the others are aligned under it, with the commas after the
    /// items or before them (<c>, item</c>, the comma two columns to the left), indented with tabs or spaces as the formatting style says.
    /// Without a style the commas are at the ends of the lines and the alignment copies the tabs of the line.
    /// </summary>
    public static class FormattedList
    {
        /// <summary>
        /// The items on separate lines. <paramref name="prefix"/> is the text of the line before the first item (the first item is not
        /// included in the result's alignment, only its column is used).
        /// </summary>
        public static string Join(IReadOnlyList<string> items, string prefix, EditorLayout editor)
        {
            var style = editor.Style;
            var result = new StringBuilder();
            string lead;
            if (style == null)
            {
                lead = new string(prefix.Select(c => c == '\t' ? '\t' : ' ').ToArray());
            }
            else
            {
                var column = Width(prefix, style.TabWidth);
                lead = Indent(style.CommaBeforeItems ? column - 2 : column, style) + (style.CommaBeforeItems ? ", " : string.Empty);
            }

            var commaAfter = style == null || !style.CommaBeforeItems;
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    result.Append(editor.NewLine).Append(lead);
                }

                result.Append(items[i]);
                if (commaAfter && i < items.Count - 1)
                {
                    result.Append(',');
                }
            }

            return result.ToString();
        }

        /// <summary>The column the text ends at (tabs go to the next tab stop).</summary>
        private static int Width(string text, int tabWidth)
        {
            var column = 0;
            var tab = tabWidth < 1 ? 4 : tabWidth;
            foreach (var c in text)
            {
                column = c == '\t' ? (column / tab + 1) * tab : column + 1;
            }

            return column;
        }

        private static string Indent(int column, FormatStyle style)
        {
            if (column <= 0)
            {
                return string.Empty;
            }

            var tab = style.TabWidth < 1 ? 4 : style.TabWidth;
            return style.UseTabs ? new string('\t', column / tab) + new string(' ', column % tab) : new string(' ', column);
        }
    }
}
