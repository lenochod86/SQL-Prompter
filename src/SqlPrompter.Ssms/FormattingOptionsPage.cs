using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Formatting;

namespace SqlPrompter.Ssms
{
    /// <summary>Tools → Options → SQL Prompter → Formatting. A SQL Prompt style can be imported from the SQL Prompter menu.</summary>
    [ComVisible(true)]
    [Guid("c2a7e9d4-3b15-4f68-9e0a-6d8b1f4c7a53")]
    public sealed class FormattingOptionsPage : DialogPage
    {
        private FormatStyle values = new FormatStyle();

        [Category("Casing")]
        [DisplayName("Keywords")]
        [Description("SELECT, FROM, WHERE and the other reserved words.")]
        public CaseStyle KeywordCase
        {
            get => values.KeywordCase;
            set => values.KeywordCase = value;
        }

        [Category("Casing")]
        [DisplayName("Built-in functions")]
        [Description("COUNT, ISNULL, DATEADD and so on (only when followed by a parenthesis).")]
        public CaseStyle FunctionCase
        {
            get => values.FunctionCase;
            set => values.FunctionCase = value;
        }

        [Category("Casing")]
        [DisplayName("Data types")]
        [Description("int, varchar and so on where a type is expected (after a variable, in CAST and CONVERT, in table definitions).")]
        public CaseStyle DataTypeCase
        {
            get => values.DataTypeCase;
            set => values.DataTypeCase = value;
        }

        [Category("Layout")]
        [DisplayName("Use tabs")]
        [Description("Indentation and alignment are made of tabs where a whole tab fits.")]
        public bool UseTabs
        {
            get => values.UseTabs;
            set => values.UseTabs = value;
        }

        [Category("Layout")]
        [DisplayName("Tab width")]
        [Description("The width of one indent level (1 to 16).")]
        public int TabWidth
        {
            get => values.TabWidth;
            set => values.TabWidth = System.Math.Max(1, System.Math.Min(16, value));
        }

        [Category("Layout")]
        [DisplayName("Commas before items")]
        [Description("Lists put the comma at the start of the next line (, item) instead of the end of the line.")]
        public bool CommaBeforeItems
        {
            get => values.CommaBeforeItems;
            set => values.CommaBeforeItems = value;
        }

        [Category("Layout")]
        [DisplayName("AND / OR start the line")]
        [Description("Conditions on several lines begin with AND or OR instead of ending with them.")]
        public bool AndOrBeforeConditions
        {
            get => values.AndOrBeforeConditions;
            set => values.AndOrBeforeConditions = value;
        }

        [Category("Layout")]
        [DisplayName("Conditions on separate lines")]
        [Description("The conditions of WHERE and HAVING each get a line when there are several.")]
        public bool ConditionsOnSeparateLines
        {
            get => values.ConditionsOnSeparateLines;
            set => values.ConditionsOnSeparateLines = value;
        }

        [Category("Layout")]
        [DisplayName("Line break after DISTINCT and TOP")]
        [Description("SELECT TOP 10 / SELECT DISTINCT end the line; the first item starts the next one.")]
        public bool NewLineAfterDistinctAndTop
        {
            get => values.NewLineAfterDistinctAndTop;
            set => values.NewLineAfterDistinctAndTop = value;
        }

        [Category("Layout")]
        [DisplayName("Indent BEGIN ... END")]
        [Description("The statements between BEGIN and END are indented.")]
        public bool IndentBlocks
        {
            get => values.IndentBlocks;
            set => values.IndentBlocks = value;
        }

        [Category("Layout")]
        [DisplayName("Keep one blank line")]
        [Description("A blank line between statements stays (as one line); otherwise blank lines are removed.")]
        public bool KeepBlankLines
        {
            get => values.KeepBlankLines;
            set => values.KeepBlankLines = value;
        }

        [Category("Short statements")]
        [DisplayName("Keep statements shorter than")]
        [Description("A SELECT, INSERT, UPDATE or DELETE that fits in fewer characters stays on one line. 0 turns it off.")]
        public int CollapseStatementsShorterThan
        {
            get => values.CollapseStatementsShorterThan;
            set => values.CollapseStatementsShorterThan = System.Math.Max(0, value);
        }

        [Category("Short statements")]
        [DisplayName("Keep subqueries shorter than")]
        [Description("A subquery or CTE that fits in fewer characters stays on the line of its item. 0 turns it off.")]
        public int CollapseSubqueriesShorterThan
        {
            get => values.CollapseSubqueriesShorterThan;
            set => values.CollapseSubqueriesShorterThan = System.Math.Max(0, value);
        }

        [Category("Short statements")]
        [DisplayName("Keep CREATE TABLE shorter than")]
        [Description("A CREATE TABLE that fits in fewer characters stays on one line. 0 turns it off.")]
        public int CollapseDdlShorterThan
        {
            get => values.CollapseDdlShorterThan;
            set => values.CollapseDdlShorterThan = System.Math.Max(0, value);
        }

        public override void LoadSettingsFromStorage() => values = SettingsService.Current.Formatting.Clone();

        public override void SaveSettingsToStorage()
        {
            var settings = SettingsService.Current.Clone();
            settings.Formatting = values.Clone();
            SettingsService.Update(settings);
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            LoadSettingsFromStorage();
            base.OnActivate(e);
        }
    }
}
