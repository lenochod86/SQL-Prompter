using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>Tools → Options → SQL Prompter → Aliases and Qualification.</summary>
    [ComVisible(true)]
    [Guid("8c3f5b1e-72d4-4a0e-b6a3-1f9d2e4c7a58")]
    public sealed class AliasOptionsPage : DialogPage
    {
        private AliasSettings values = new AliasSettings();

        [Category("Aliases")]
        [DisplayName("Add aliases to tables")]
        [Description("Inserts an alias with a table or view chosen after FROM or JOIN (OrderItems → oi).")]
        public bool AddAliases
        {
            get => values.AddAliases;
            set => values.AddAliases = value;
        }

        [Category("Aliases")]
        [DisplayName("Include AS")]
        [Description("Inserts \"AS oi\" instead of \"oi\".")]
        public bool IncludeAs
        {
            get => values.IncludeAs;
            set => values.IncludeAs = value;
        }

        [Category("Aliases")]
        [DisplayName("Capitalise generated aliases")]
        public bool CapitaliseGeneratedAliases
        {
            get => values.CapitaliseGeneratedAliases;
            set => values.CapitaliseGeneratedAliases = value;
        }

        [Category("Aliases")]
        [DisplayName("Custom aliases")]
        [Description("One entry per line as Name=alias, e.g. OrderItems=oit. Custom aliases take precedence over generated ones.")]
        public string[] CustomAliases
        {
            get => values.CustomAliases.Select(r => r.Name + "=" + r.Alias).ToArray();
            set => values.CustomAliases = (value ?? new string[0])
                .Select(s => s.Split(new[] { '=' }, 2))
                .Where(p => p.Length == 2 && p[0].Trim().Length > 0 && p[1].Trim().Length > 0)
                .Select(p => new AliasRule { Name = p[0].Trim(), Alias = p[1].Trim() })
                .ToList();
        }

        [Category("Aliases")]
        [DisplayName("Ignored prefixes")]
        [Description("Prefixes dropped from a table name before the alias is generated, one per line (e.g. tbl_).")]
        public string[] IgnoredPrefixes
        {
            get => values.IgnoredPrefixes.ToArray();
            set => values.IgnoredPrefixes = (value ?? new string[0]).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }

        [Category("Qualification")]
        [DisplayName("Qualify objects with schema")]
        [Description("Inserts tables and views as schema.name.")]
        public bool QualifyObjectNames
        {
            get => values.QualifyObjectNames;
            set => values.QualifyObjectNames = value;
        }

        [Category("Qualification")]
        [DisplayName("Qualify columns with alias")]
        [Description("Inserts columns as alias.column. Columns that exist in several tables of the query are always qualified.")]
        public bool QualifyColumnsWithAliases
        {
            get => values.QualifyColumnsWithAliases;
            set => values.QualifyColumnsWithAliases = value;
        }

        public override void LoadSettingsFromStorage() => values = SettingsService.Current.Aliases.Clone();

        public override void SaveSettingsToStorage()
        {
            var settings = SettingsService.Current.Clone();
            settings.Aliases = values.Clone();
            SettingsService.Update(settings);
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            LoadSettingsFromStorage();
            base.OnActivate(e);
        }
    }
}
