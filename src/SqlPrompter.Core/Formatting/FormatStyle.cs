namespace SqlPrompter.Core.Formatting
{
    public enum CaseStyle
    {
        /// <summary>The text is left as it was written.</summary>
        Keep,
        Upper,
        Lower,
    }

    /// <summary>
    /// How the formatter lays out a script. The defaults follow the style the project is tested against (upper-case keywords, tabs, commas
    /// before the items, statements shorter than 75 characters on one line); <c>SqlPromptStyleImporter</c> fills it from a SQL Prompt style.
    /// </summary>
    public sealed class FormatStyle
    {
        public CaseStyle KeywordCase { get; set; } = CaseStyle.Upper;

        public CaseStyle FunctionCase { get; set; } = CaseStyle.Upper;

        public CaseStyle DataTypeCase { get; set; } = CaseStyle.Upper;

        /// <summary>Leading white space is made of tabs where a whole tab fits (the rest is spaces).</summary>
        public bool UseTabs { get; set; } = true;

        /// <summary>The width of one indent level and of a tab stop.</summary>
        public int TabWidth { get; set; } = 4;

        /// <summary><c>, item</c> at the start of a line instead of <c>item,</c> at the end of the previous one.</summary>
        public bool CommaBeforeItems { get; set; } = true;

        /// <summary>A SELECT, INSERT, UPDATE or DELETE whose text on one line is shorter than this stays on one line; 0 turns it off.</summary>
        public int CollapseStatementsShorterThan { get; set; } = 75;

        /// <summary>A subquery in parentheses shorter than this stays on the line of its item; 0 turns it off.</summary>
        public int CollapseSubqueriesShorterThan { get; set; } = 75;

        /// <summary>A CREATE TABLE shorter than this stays on one line; 0 turns it off.</summary>
        public int CollapseDdlShorterThan { get; set; } = 75;

        /// <summary><c>SELECT TOP 10</c> / <c>SELECT DISTINCT</c> are followed by a line break before the first item.</summary>
        public bool NewLineAfterDistinctAndTop { get; set; }

        /// <summary><c>AND</c> and <c>OR</c> start the lines of a condition (otherwise they end them).</summary>
        public bool AndOrBeforeConditions { get; set; } = true;

        /// <summary>The conditions of WHERE and HAVING are put on separate lines when there is more than one.</summary>
        public bool ConditionsOnSeparateLines { get; set; } = true;

        /// <summary>Longer lines of a column list in INSERT and CREATE TABLE are broken.</summary>
        public int MaxLineLength { get; set; } = 120;

        /// <summary>One blank line between statements stays when the script had one (otherwise all blank lines are removed).</summary>
        public bool KeepBlankLines { get; set; } = true;

        /// <summary>The contents of BEGIN … END are indented.</summary>
        public bool IndentBlocks { get; set; } = true;

        public FormatStyle Clone() => (FormatStyle)MemberwiseClone();
    }
}
