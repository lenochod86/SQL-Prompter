using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Coloring;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>Tools → Options → SQL Prompter → Coloring.</summary>
    [ComVisible(true)]
    [Guid("5e2a9c47-1b6d-4f83-9a05-d7c3e8b14f62")]
    public sealed class ColoringOptionsPage : DialogPage
    {
        private ColorSettings values = new ColorSettings();

        [Category("Coloring")]
        [DisplayName("Color query windows")]
        [Description("Colors the tab, the frame and the status bar of a query window by its server and database.")]
        public bool Enabled
        {
            get => values.Enabled;
            set => values.Enabled = value;
        }

        [Category("Coloring")]
        [DisplayName("Environments")]
        [Description("One entry per line as Name=#RRGGBB, e.g. Production=#E5484D.")]
        public string[] Environments
        {
            get => values.Environments.Select(e => e.Name + "=" + e.Color).ToArray();
            set => values.Environments = (value ?? new string[0])
                .Select(s => s.Split(new[] { '=' }, 2))
                .Where(p => p.Length == 2 && p[0].Trim().Length > 0 && EnvironmentMatcher.TryParseColor(p[1], out _, out _, out _))
                .Select(p => new ColorEnvironment { Name = p[0].Trim(), Color = "#" + p[1].Trim().TrimStart('#').ToUpperInvariant() })
                .ToList();
        }

        [Category("Coloring")]
        [DisplayName("Rules")]
        [Description("One entry per line as server | database | environment. Use * and ? as wildcards; leave the database empty for every database. A rule with a database wins over a rule for the whole server; otherwise the first match wins.")]
        public string[] Rules
        {
            get => values.Rules.Select(r => r.Server + " | " + r.Database + " | " + r.Environment).ToArray();
            set => values.Rules = (value ?? new string[0])
                .Select(s => s.Split('|'))
                .Where(p => p.Length == 3 && p[0].Trim().Length > 0 && p[2].Trim().Length > 0)
                .Select(p => new ColorRule { Server = p[0].Trim(), Database = p[1].Trim(), Environment = p[2].Trim() })
                .ToList();
        }

        public override void LoadSettingsFromStorage() => values = SettingsService.Current.Coloring.Clone();

        public override void SaveSettingsToStorage()
        {
            var settings = SettingsService.Current.Clone();
            settings.Coloring = values.Clone();
            SettingsService.Update(settings);
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            LoadSettingsFromStorage();
            base.OnActivate(e);
        }
    }
}
