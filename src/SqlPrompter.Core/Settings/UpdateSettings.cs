using System.Runtime.Serialization;

namespace SqlPrompter.Core.Settings
{
    /// <summary>The check for a new version (#297): whether it runs and what the user decided about the versions announced so far.</summary>
    [DataContract]
    public sealed class UpdateSettings
    {
        public UpdateSettings()
        {
            SetDefaults();
        }

        /// <summary>Ask GitHub once a day whether a newer release exists (one anonymous HTTPS request, nothing else is sent).</summary>
        [DataMember(Name = "checkForUpdates")]
        public bool CheckForUpdates { get; set; }

        /// <summary>The version the user chose to skip; a newer one is announced again. Empty when none.</summary>
        [DataMember(Name = "skippedVersion")]
        public string SkippedVersion { get; set; }

        /// <summary>The first day (yyyy-MM-dd, local time) on which the window may appear again after "Remind me tomorrow". Empty when not postponed.</summary>
        [DataMember(Name = "remindOn")]
        public string RemindOn { get; set; }

        /// <summary>The day (yyyy-MM-dd, local time) of the last check; the next one is made on a later day.</summary>
        [DataMember(Name = "lastCheckDay")]
        public string LastCheckDay { get; set; }

        public UpdateSettings Clone() => (UpdateSettings)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            SkippedVersion = SkippedVersion ?? string.Empty;
            RemindOn = RemindOn ?? string.Empty;
            LastCheckDay = LastCheckDay ?? string.Empty;
        }

        private void SetDefaults()
        {
            CheckForUpdates = true;
            SkippedVersion = string.Empty;
            RemindOn = string.Empty;
            LastCheckDay = string.Empty;
        }
    }
}
