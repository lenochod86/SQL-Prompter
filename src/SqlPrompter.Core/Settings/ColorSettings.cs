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

        [DataMember(Name = "environments")]
        public List<ColorEnvironment> Environments { get; set; }

        /// <summary>The first rule that matches wins.</summary>
        [DataMember(Name = "rules")]
        public List<ColorRule> Rules { get; set; }

        public ColorSettings Clone() => new ColorSettings
        {
            Enabled = Enabled,
            Environments = Environments.Select(e => new ColorEnvironment { Name = e.Name, Color = e.Color }).ToList(),
            Rules = Rules.Select(r => new ColorRule { Server = r.Server, Database = r.Database, Environment = r.Environment }).ToList(),
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
            Environments = new List<ColorEnvironment>();
            Rules = new List<ColorRule>();
        }
    }
}
