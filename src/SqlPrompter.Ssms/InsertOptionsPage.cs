using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>Tools → Options → SQL Prompter → Inserts.</summary>
    [ComVisible(true)]
    [Guid("7b3e5d92-1a4c-4f68-9d07-c2e8a5b1f346")]
    public sealed class InsertOptionsPage : DialogPage
    {
        private InsertSettings values = new InsertSettings();

        [Category("INSERT")]
        [DisplayName("Column list")]
        [Description("Automatic: choosing the table after INSERT INTO inserts it with its column list. Item: the table is inserted alone and a second item " +
                     "\"table (columns)\" inserts the column list. AfterParenthesis: the list is offered after typing ( behind the table name. " +
                     "Identity, computed and rowversion columns are left out.")]
        public InsertColumnsMode ColumnsMode
        {
            get => values.ColumnsMode;
            set => values.ColumnsMode = value;
        }

        [Category("INSERT")]
        [DisplayName("Layout of the lists")]
        [Description("OneLine, EachOnItsOwnLine or FitWidth (as many on a line as fit the editor, more lines when needed).")]
        public ListLayout Layout
        {
            get => values.Layout;
            set => values.Layout = value;
        }

        [Category("INSERT")]
        [DisplayName("After the column list")]
        [Description("None: a new line after the closing parenthesis. Values: VALUES with a placeholder for each column ('' for text, N'' for Unicode text, 0 for numbers, NULL for nullable columns).")]
        public InsertValuesMode ValuesMode
        {
            get => values.ValuesMode;
            set => values.ValuesMode = value;
        }

        public override void LoadSettingsFromStorage() => values = SettingsService.Current.Inserts.Clone();

        public override void SaveSettingsToStorage()
        {
            var settings = SettingsService.Current.Clone();
            settings.Inserts = values.Clone();
            SettingsService.Update(settings);
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            LoadSettingsFromStorage();
            base.OnActivate(e);
        }
    }
}
