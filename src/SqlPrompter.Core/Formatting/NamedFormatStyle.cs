using System.Runtime.Serialization;

namespace SqlPrompter.Core.Formatting
{
    /// <summary>A formatting style with a name; the settings keep several and one of them is active.</summary>
    [DataContract]
    public sealed class NamedFormatStyle
    {
        public NamedFormatStyle()
        {
            Name = string.Empty;
            Style = new FormatStyle();
        }

        public NamedFormatStyle(string name, FormatStyle style)
        {
            Name = name;
            Style = style;
        }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "style")]
        public FormatStyle Style { get; set; }

        public NamedFormatStyle Clone() => new NamedFormatStyle(Name, (Style ?? new FormatStyle()).Clone());

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Name = Name ?? string.Empty;
            Style = Style ?? new FormatStyle();
        }
    }
}
