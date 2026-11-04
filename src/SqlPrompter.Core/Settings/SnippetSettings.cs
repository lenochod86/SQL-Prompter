using System.Runtime.Serialization;

namespace SqlPrompter.Core.Settings
{
    /// <summary>Where the snippets are.</summary>
    [DataContract]
    public sealed class SnippetSettings
    {
        public SnippetSettings()
        {
            Folder = string.Empty;
        }

        /// <summary>Folder with the snippet files (SQL Prompt's JSON format); empty means the default folder next to the settings file.</summary>
        [DataMember(Name = "folder")]
        public string Folder { get; set; }

        public SnippetSettings Clone() => (SnippetSettings)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => Folder = string.Empty;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) => Folder = Folder ?? string.Empty;
    }
}
