using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Tools → Options → SQL Prompter → Coloring. The page is a small editor (<see cref="ColoringEditor"/>) instead of a property grid: environments
    /// with a color picker, rules with a dropdown of the environments, and a check before the page is applied.
    /// </summary>
    [ComVisible(true)]
    [Guid("5e2a9c47-1b6d-4f83-9a05-d7c3e8b14f62")]
    public sealed class ColoringOptionsPage : DialogPage
    {
        private ColoringEditor editor;
        private ColorSettings values = new ColorSettings();

        protected override IWin32Window Window
        {
            get
            {
                if (editor == null)
                {
                    editor = new ColoringEditor();
                    editor.ShowSettings(values);
                }

                return editor;
            }
        }

        public override void LoadSettingsFromStorage()
        {
            values = SettingsService.Current.Coloring.Clone();
            editor?.ShowSettings(values);
        }

        public override void SaveSettingsToStorage()
        {
            var settings = SettingsService.Current.Clone();
            settings.Coloring = values.Clone();
            SettingsService.Update(settings);
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            if (editor != null && e.ApplyBehavior == ApplyKind.Apply)
            {
                var problems = editor.CurrentProblems;
                if (problems.Count > 0)
                {
                    MessageBox.Show("The coloring settings are not valid:\n- " + string.Join("\n- ", problems), "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.ApplyBehavior = ApplyKind.Cancel;
                    return;
                }

                values = editor.ToSettings();
            }

            base.OnApply(e);
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            LoadSettingsFromStorage();
            base.OnActivate(e);
        }
    }
}
