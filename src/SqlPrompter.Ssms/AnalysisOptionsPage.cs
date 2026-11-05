using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>Tools → Options → SQL Prompter → Code Analysis.</summary>
    [ComVisible(true)]
    [Guid("5b8d2f1e-7c43-4a96-b0e5-3d1c9a6f8e27")]
    public sealed class AnalysisOptionsPage : DialogPage
    {
        private AnalysisSettings values = new AnalysisSettings();

        [Category("Code analysis")]
        [DisplayName("Mark problems in the editor")]
        [Description("Underlines suspicious code with a wavy line (orange for warnings, gray for hints); the tooltip says what is wrong. The check runs in the background a moment after you stop typing.")]
        public bool Enabled
        {
            get => values.Enabled;
            set => values.Enabled = value;
        }

        [Category("Warnings")]
        [DisplayName("Comparison with NULL")]
        [Description("= NULL, <> NULL and != NULL in a condition are never true; use IS NULL or IS NOT NULL.")]
        public bool EqualsNull
        {
            get => values.EqualsNull;
            set => values.EqualsNull = value;
        }

        [Category("Warnings")]
        [DisplayName("JOIN without ON")]
        [Description("A JOIN (other than CROSS JOIN) without an ON condition combines every row with every row.")]
        public bool JoinWithoutOn
        {
            get => values.JoinWithoutOn;
            set => values.JoinWithoutOn = value;
        }

        [Category("Hints")]
        [DisplayName("INSERT without a column list")]
        [Description("INSERT INTO table VALUES (...) or SELECT depends on the order of the table's columns and breaks when one is added.")]
        public bool InsertWithoutColumns
        {
            get => values.InsertWithoutColumns;
            set => values.InsertWithoutColumns = value;
        }

        [Category("Hints")]
        [DisplayName("Unused variable")]
        [Description("A variable declared with DECLARE that the batch never mentions again.")]
        public bool UnusedVariable
        {
            get => values.UnusedVariable;
            set => values.UnusedVariable = value;
        }

        [Category("Hints")]
        [DisplayName("SELECT *")]
        [Description("SELECT * and alias.* in a select list (not COUNT(*) and not EXISTS (SELECT *)). Off by default because it is everyday in ad-hoc queries.")]
        public bool SelectStar
        {
            get => values.SelectStar;
            set => values.SelectStar = value;
        }

        [Category("Hints")]
        [DisplayName("NOLOCK and READUNCOMMITTED")]
        [Description("Lock hints that can return uncommitted, missing or duplicated rows. Off by default.")]
        public bool LockHint
        {
            get => values.LockHint;
            set => values.LockHint = value;
        }

        public override void LoadSettingsFromStorage() => values = SettingsService.Current.Analysis.Clone();

        public override void SaveSettingsToStorage()
        {
            var settings = SettingsService.Current.Clone();
            settings.Analysis = values.Clone();
            SettingsService.Update(settings);
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            LoadSettingsFromStorage();
            base.OnActivate(e);
        }
    }
}
