using System.ComponentModel;
using System.Drawing.Design;
using System.Runtime.InteropServices;
using System.Windows.Forms.Design;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>Tools → Options → SQL Prompter → Snippets.</summary>
    [ComVisible(true)]
    [Guid("6f1d3a8e-4c27-4b59-b0e3-7d8a92c15f40")]
    public sealed class SnippetOptionsPage : DialogPage
    {
        private SnippetSettings values = new SnippetSettings();

        [Category("Snippets")]
        [DisplayName("Snippet folder")]
        [Description("Folder with the snippet files, one JSON file per snippet as in SQL Prompt's snippet folder. Empty uses %APPDATA%\\SqlPrompter\\Snippets.")]
        [Editor(typeof(FolderNameEditor), typeof(UITypeEditor))]
        public string Folder
        {
            get => values.Folder;
            set => values.Folder = value ?? string.Empty;
        }

        public override void LoadSettingsFromStorage() => values = SettingsService.Current.Snippets.Clone();

        public override void SaveSettingsToStorage()
        {
            var settings = SettingsService.Current.Clone();
            settings.Snippets = values.Clone();
            SettingsService.Update(settings);
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            LoadSettingsFromStorage();
            base.OnActivate(e);
        }
    }
}
