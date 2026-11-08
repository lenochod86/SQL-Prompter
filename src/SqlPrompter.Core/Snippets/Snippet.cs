using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace SqlPrompter.Core.Snippets
{
    /// <summary>A custom field of a snippet: <c>$name$</c> in the body, with a default value.</summary>
    [DataContract]
    public sealed class SnippetPlaceholder
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; }

        [DataMember(Name = "defaultValue", Order = 1)]
        public string DefaultValue { get; set; }
    }

    /// <summary>
    /// A snippet in the format of SQL Prompt's snippet folder: one JSON file with <c>id</c>, <c>prefix</c> (what is typed to
    /// expand it), <c>description</c>, <c>body</c> and the custom <c>placeholders</c>.
    /// </summary>
    [DataContract]
    public sealed class Snippet
    {
        public Snippet()
        {
            SetDefaults();
        }

        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "prefix", Order = 1)]
        public string Prefix { get; set; }

        [DataMember(Name = "description", Order = 2)]
        public string Description { get; set; }

        [DataMember(Name = "body", Order = 3)]
        public string Body { get; set; }

        /// <summary>Omitted from the file when empty, like SQL Prompt does.</summary>
        [DataMember(Name = "placeholders", Order = 4, EmitDefaultValue = false)]
        public List<SnippetPlaceholder> Placeholders { get; set; }

        /// <summary>The snippet does not open the suggestion list after it is inserted (#353); omitted from the file when it does (the default).</summary>
        [DataMember(Name = "noSuggestions", Order = 5, EmitDefaultValue = false)]
        public bool NoSuggestions { get; set; }

        /// <summary>The snippet comes from a shared folder (not saved in the file).</summary>
        public bool IsShared { get; set; }

        public static Snippet Create(string prefix, string description, string body, IEnumerable<SnippetPlaceholder> placeholders = null) => new Snippet
        {
            Id = Guid.NewGuid().ToString(),
            Prefix = prefix,
            Description = description ?? string.Empty,
            Body = body ?? string.Empty,
            Placeholders = (placeholders ?? Enumerable.Empty<SnippetPlaceholder>()).ToList(),
        };

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString() : Id;
            Description = Description ?? string.Empty;
            Body = Body ?? string.Empty;
            Placeholders = (Placeholders ?? new List<SnippetPlaceholder>())
                .Where(p => p != null && !string.IsNullOrEmpty(p.Name))
                .Select(p => new SnippetPlaceholder { Name = p.Name, DefaultValue = p.DefaultValue ?? string.Empty })
                .ToList();
        }

        private void SetDefaults()
        {
            Id = string.Empty;
            Description = string.Empty;
            Body = string.Empty;
            Placeholders = new List<SnippetPlaceholder>();
        }
    }
}
