using System.Runtime.Serialization;

namespace SqlPrompter.Core.Settings
{
    /// <summary>What JOIN suggestions and the conditions after ON look like.</summary>
    [DataContract]
    public sealed class JoinSettings
    {
        public JoinSettings()
        {
            SetDefaults();
        }

        /// <summary>The new table stands left of the equals sign (<c>o.IdUser = u.IdUser</c>), otherwise right (<c>u.IdUser = o.IdUser</c>).</summary>
        [DataMember(Name = "newTableOnLeft")]
        public bool NewTableOnLeft { get; set; }

        /// <summary>Offer the individual column pairs of a key over several columns besides the whole condition.</summary>
        [DataMember(Name = "includePartialKeyConditions")]
        public bool IncludePartialKeyConditions { get; set; }

        /// <summary>Offer a condition for every column name that both tables have, besides the foreign keys.</summary>
        [DataMember(Name = "nameMatchedConditions")]
        public bool NameMatchedConditions { get; set; }

        /// <summary>Offer a name-matched condition only when the two columns have the same type.</summary>
        [DataMember(Name = "requireSameTypeForNameMatches")]
        public bool RequireSameTypeForNameMatches { get; set; }

        public JoinSettings Clone() => (JoinSettings)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults()
        {
            // Follows the user's SQL Prompt settings (partial FK conditions, name matched conditions, flipped column order).
            NewTableOnLeft = true;
            IncludePartialKeyConditions = true;
            NameMatchedConditions = true;
            RequireSameTypeForNameMatches = false;
        }
    }
}
