namespace SqlPrompter.Core.Completion
{
    /// <summary>What the editor looks like where the list is shown; multi-line insertions (INSERT, EXEC) are laid out with it.</summary>
    public sealed class EditorLayout
    {
        public static EditorLayout Default { get; } = new EditorLayout();

        /// <summary>Line ending of the document.</summary>
        public string NewLine { get; set; } = "\r\n";

        /// <summary>Whitespace the current line starts with; new lines of an insertion start with it.</summary>
        public string LineIndent { get; set; } = string.Empty;

        /// <summary>The text of the current line before the place where the insertion starts.</summary>
        public string LinePrefix { get; set; } = string.Empty;

        /// <summary>The one level of indentation used inside parentheses (a tab or spaces, as the editor does).</summary>
        public string IndentUnit { get; set; } = "    ";

        /// <summary>Characters that fit on a line of the editor; used by the "fit the width" layout.</summary>
        public int LineWidth { get; set; } = 100;
    }
}
