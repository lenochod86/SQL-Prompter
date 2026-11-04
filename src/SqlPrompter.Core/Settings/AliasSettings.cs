using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace SqlPrompter.Core.Settings
{
    /// <summary>A fixed alias for one table or view, e.g. OrderItems → oi.</summary>
    [DataContract]
    public sealed class AliasRule
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "alias")]
        public string Alias { get; set; }
    }

    /// <summary>Automatic aliases and qualification of inserted names.</summary>
    [DataContract]
    public sealed class AliasSettings
    {
        public AliasSettings()
        {
            SetDefaults();
        }

        /// <summary>Add an alias when a table or view is inserted after FROM or JOIN.</summary>
        [DataMember(Name = "addAliases")]
        public bool AddAliases { get; set; }

        /// <summary>Insert "AS alias" instead of "alias".</summary>
        [DataMember(Name = "includeAs")]
        public bool IncludeAs { get; set; }

        /// <summary>Generated aliases in upper case (custom aliases are inserted as written).</summary>
        [DataMember(Name = "capitaliseGeneratedAliases")]
        public bool CapitaliseGeneratedAliases { get; set; }

        /// <summary>Insert tables and views as schema.name.</summary>
        [DataMember(Name = "qualifyObjectNames")]
        public bool QualifyObjectNames { get; set; }

        /// <summary>Insert columns as alias.column.</summary>
        [DataMember(Name = "qualifyColumnsWithAliases")]
        public bool QualifyColumnsWithAliases { get; set; }

        /// <summary>Alias for a given table name; takes precedence over generating one.</summary>
        [DataMember(Name = "customAliases")]
        public List<AliasRule> CustomAliases { get; set; }

        /// <summary>Prefixes dropped from a name before the alias is generated, e.g. tbl_.</summary>
        [DataMember(Name = "ignoredPrefixes")]
        public List<string> IgnoredPrefixes { get; set; }

        public AliasSettings Clone()
        {
            var copy = (AliasSettings)MemberwiseClone();
            copy.CustomAliases = CustomAliases.Select(r => new AliasRule { Name = r.Name, Alias = r.Alias }).ToList();
            copy.IgnoredPrefixes = IgnoredPrefixes.ToList();
            return copy;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            CustomAliases = (CustomAliases ?? new List<AliasRule>()).Where(r => r != null && !string.IsNullOrEmpty(r.Name) && !string.IsNullOrEmpty(r.Alias)).ToList();
            IgnoredPrefixes = (IgnoredPrefixes ?? new List<string>()).Where(p => !string.IsNullOrEmpty(p)).ToList();
        }

        private void SetDefaults()
        {
            // Follow the user's SQL Prompt settings: aliases with AS, objects with schema, columns with alias.
            AddAliases = true;
            IncludeAs = true;
            CapitaliseGeneratedAliases = false;
            QualifyObjectNames = true;
            QualifyColumnsWithAliases = true;
            CustomAliases = new List<AliasRule>();
            IgnoredPrefixes = new List<string>();
        }
    }
}
