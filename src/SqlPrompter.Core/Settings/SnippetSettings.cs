using System.Linq;
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
            SharedFolders = string.Empty;
        }

        /// <summary>Shared snippet folders, separated by semicolons (a repository, a network share); read in addition to the personal folder, which wins on the same prefix.</summary>
        [DataMember(Name = "sharedFolders")]
        public string SharedFolders { get; set; }

        /// <summary>The shared folders as a list, without empty entries.</summary>
        public System.Collections.Generic.IReadOnlyList<string> SharedFolderList() =>
            (SharedFolders ?? string.Empty).Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).Where(f => f.Length > 0).ToList();

        /// <summary>Folder with the snippet files (SQL Prompt's JSON format); empty means the default folder next to the settings file.</summary>
        [DataMember(Name = "folder")]
        public string Folder { get; set; }

        public SnippetSettings Clone() => (SnippetSettings)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Folder = string.Empty;
            SharedFolders = string.Empty;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Folder = Folder ?? string.Empty;
            SharedFolders = SharedFolders ?? string.Empty;
        }
    }
}
