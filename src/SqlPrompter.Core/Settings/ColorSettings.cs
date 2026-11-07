using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace SqlPrompter.Core.Settings
{
    /// <summary>A named environment (Production, Test, ...) with the color its windows get.</summary>
    [DataContract]
    public sealed class ColorEnvironment
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>Color as #RRGGBB.</summary>
        [DataMember(Name = "color")]
        public string Color { get; set; }
    }

    /// <summary>Maps a server and database (wildcards * and ?) to an environment by name.</summary>
    [DataContract]
    public sealed class ColorRule
    {
        [DataMember(Name = "server")]
        public string Server { get; set; }

        /// <summary>Empty matches every database.</summary>
        [DataMember(Name = "database")]
        public string Database { get; set; }

        [DataMember(Name = "environment")]
        public string Environment { get; set; }

        /// <summary>Running a statement that changes data or schema in a window that matches this rule asks for confirmation first (#209).</summary>
        [DataMember(Name = "askBeforeChanges")]
        public bool AskBeforeChanges { get; set; }
    }

    /// <summary>Coloring of query windows by their connection.</summary>
    [DataContract]
    public sealed class ColorSettings
    {
        public ColorSettings()
        {
            SetDefaults();
        }

        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>Color the tab (accent, tint and the frame of the editor pane) of a query window.</summary>
        [DataMember(Name = "colorTab")]
        public bool ColorTab { get; set; }

        /// <summary>Color the status strip inside each query window (the one with Connected, the server, login and database); each window by its own connection.</summary>
        [DataMember(Name = "colorWindowStatusBar")]
        public bool ColorWindowStatusBar { get; set; }

        /// <summary>Color the status bar of the SSMS main window (at the bottom) by the connection of the active window.</summary>
        [DataMember(Name = "colorShellStatusBar")]
        public bool ColorShellStatusBar { get; set; }

        [DataMember(Name = "environments")]
        public List<ColorEnvironment> Environments { get; set; }

        /// <summary>The first matching rule wins; rules with a database are tried before rules for a whole server.</summary>
        [DataMember(Name = "rules")]
        public List<ColorRule> Rules { get; set; }

        public ColorSettings Clone() => new ColorSettings
        {
            Enabled = Enabled,
            ColorTab = ColorTab,
            ColorWindowStatusBar = ColorWindowStatusBar,
            ColorShellStatusBar = ColorShellStatusBar,
            Environments = Environments.Select(e => new ColorEnvironment { Name = e.Name, Color = e.Color }).ToList(),
            Rules = Rules.Select(r => new ColorRule { Server = r.Server, Database = r.Database, Environment = r.Environment, AskBeforeChanges = r.AskBeforeChanges }).ToList(),
        };

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Environments = Environments ?? new List<ColorEnvironment>();
            Rules = Rules ?? new List<ColorRule>();
        }

        private void SetDefaults()
        {
            Enabled = true;
            ColorTab = true;
            ColorWindowStatusBar = true;
            ColorShellStatusBar = false;
            Environments = new List<ColorEnvironment>();
            Rules = new List<ColorRule>();
        }
    }
}
