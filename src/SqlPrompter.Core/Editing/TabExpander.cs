using System.Text;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// Shows a text the way the editor does: a tab moves to the next tab stop (a multiple of the tab size from the start of the line), not by a fixed
    /// width. For previews of SQL that are drawn in a control that does not align tabs (#450); the stored text keeps its tabs.
    /// </summary>
    public static class TabExpander
    {
        public const int DefaultTabSize = 4;

        /// <summary>The text with every tab replaced by the spaces up to the next tab stop, line by line (CR LF, LF and CR stay as they are).</summary>
        public static string Expand(string text, int tabSize)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\t') < 0)
            {
                return text ?? string.Empty;
            }

            if (tabSize < 1)
            {
                tabSize = DefaultTabSize;
            }

            var result = new StringBuilder(text.Length + 16);
            var column = 0;
            foreach (var c in text)
            {
                if (c == '\t')
                {
                    var spaces = tabSize - column % tabSize;
                    result.Append(' ', spaces);
                    column += spaces;
                }
                else
                {
                    result.Append(c);
                    column = c == '\n' || c == '\r' ? 0 : column + 1;
                }
            }

            return result.ToString();
        }
    }
}
