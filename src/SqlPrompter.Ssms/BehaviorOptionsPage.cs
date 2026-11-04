using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>Tools → Options → SQL Prompter → Behavior. Values live in the settings file, not in the registry.</summary>
    [ComVisible(true)]
    [Guid("5d2c1e6a-3f0b-4c57-9a9e-6b1b7a0f4d21")]
    public sealed class BehaviorOptionsPage : DialogPage
    {
        private BehaviorSettings values = new BehaviorSettings();

        [Category("Suggestions")]
        [DisplayName("Enable suggestions")]
        [Description("When off, SQL Prompter stays out of the way and the built-in SSMS list is shown.")]
        public bool SuggestionsEnabled
        {
            get => values.SuggestionsEnabled;
            set => values.SuggestionsEnabled = value;
        }

        [Category("Suggestions")]
        [DisplayName("Mark items with SQL Prompter")]
        [Description("Shows \"SQL Prompter\" on the right of every item so that it is obvious the list is ours and not the built-in one.")]
        public bool MarkItems
        {
            get => values.MarkItems;
            set => values.MarkItems = value;
        }

        [Category("Suggestions")]
        [DisplayName("Keyword case")]
        [Description("MatchTyped inserts upper case, or lower case if you started typing in lower case.")]
        public KeywordCase KeywordCase
        {
            get => values.KeywordCase;
            set => values.KeywordCase = value;
        }

        [Category("Insert suggestion on")]
        [DisplayName("Space")]
        [Description("Enter and Tab always insert the selected suggestion.")]
        public bool CommitOnSpace
        {
            get => values.CommitOnSpace;
            set => values.CommitOnSpace = value;
        }

        [Category("Insert suggestion on")]
        [DisplayName("Dot")]
        public bool CommitOnDot
        {
            get => values.CommitOnDot;
            set => values.CommitOnDot = value;
        }

        [Category("Insert suggestion on")]
        [DisplayName("Comma")]
        public bool CommitOnComma
        {
            get => values.CommitOnComma;
            set => values.CommitOnComma = value;
        }

        [Category("Insert suggestion on")]
        [DisplayName("Parentheses")]
        public bool CommitOnParentheses
        {
            get => values.CommitOnParentheses;
            set => values.CommitOnParentheses = value;
        }

        [Category("Insert suggestion on")]
        [DisplayName("Semicolon")]
        public bool CommitOnSemicolon
        {
            get => values.CommitOnSemicolon;
            set => values.CommitOnSemicolon = value;
        }

        [Category("Insert suggestion on")]
        [DisplayName("Closing bracket ]")]
        public bool CommitOnClosingBracket
        {
            get => values.CommitOnClosingBracket;
            set => values.CommitOnClosingBracket = value;
        }

        public override void LoadSettingsFromStorage() => values = SettingsService.Current.Behavior.Clone();

        public override void SaveSettingsToStorage()
        {
            var settings = SettingsService.Current.Clone();
            settings.Behavior = values.Clone();
            SettingsService.Update(settings);
        }

        // The page is created once; reload so that an import or a file edit shows up when the dialog opens again.
        protected override void OnActivate(CancelEventArgs e)
        {
            LoadSettingsFromStorage();
            base.OnActivate(e);
        }
    }
}
