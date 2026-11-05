using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>Tools → Options → SQL Prompter → Joins.</summary>
    [ComVisible(true)]
    [Guid("2e7a9c4d-6b1f-4d3a-8c52-9f0e1a7b3c64")]
    public sealed class JoinOptionsPage : DialogPage
    {
        private JoinSettings values = new JoinSettings();

        [Category("Conditions")]
        [DisplayName("New table on the left")]
        [Description("o.IdUser = u.IdUser instead of u.IdUser = o.IdUser (o is the table being joined).")]
        public bool NewTableOnLeft
        {
            get => values.NewTableOnLeft;
            set => values.NewTableOnLeft = value;
        }

        [Category("Conditions")]
        [DisplayName("Foreign keys")]
        [Description("Offer the condition of a foreign key (the columns it references).")]
        public bool ForeignKeyConditions
        {
            get => values.ForeignKeyConditions;
            set => values.ForeignKeyConditions = value;
        }

        [Category("Conditions")]
        [DisplayName("Single columns of multi-column keys")]
        [Description("Besides the whole condition of a foreign key over several columns, offer each column pair on its own.")]
        public bool IncludePartialKeyConditions
        {
            get => values.IncludePartialKeyConditions;
            set => values.IncludePartialKeyConditions = value;
        }

        [Category("Conditions")]
        [DisplayName("Columns with the same name")]
        [Description("Offer a condition for every column name that both tables have, besides the foreign keys.")]
        public bool NameMatchedConditions
        {
            get => values.NameMatchedConditions;
            set => values.NameMatchedConditions = value;
        }

        [Category("Conditions")]
        [DisplayName("Same name needs the same type")]
        [Description("A condition by column name is offered only when both columns have the same type.")]
        public bool RequireSameTypeForNameMatches
        {
            get => values.RequireSameTypeForNameMatches;
            set => values.RequireSameTypeForNameMatches = value;
        }

        public override void LoadSettingsFromStorage() => values = SettingsService.Current.Joins.Clone();

        public override void SaveSettingsToStorage()
        {
            var settings = SettingsService.Current.Clone();
            settings.Joins = values.Clone();
            SettingsService.Update(settings);
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            LoadSettingsFromStorage();
            base.OnActivate(e);
        }
    }
}
