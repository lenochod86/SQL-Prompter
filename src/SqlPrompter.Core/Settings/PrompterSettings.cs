using System;
using System.Runtime.Serialization;
using SqlPrompter.Core.Completion;

namespace SqlPrompter.Core.Settings
{
    /// <summary>
    /// All user settings. Every feature adds its own group here with defaults; a missing member in the JSON file keeps its default.
    /// </summary>
    [DataContract]
    public sealed class PrompterSettings
    {
        public const int CurrentVersion = 1;

        public PrompterSettings()
        {
            SetDefaults();
        }

        [DataMember(Name = "version")]
        public int Version { get; set; }

        [DataMember(Name = "behavior")]
        public BehaviorSettings Behavior { get; set; }

        public PrompterSettings Clone() => new PrompterSettings { Version = Version, Behavior = Behavior.Clone() };

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            // "behavior": null in the file.
            Behavior = Behavior ?? new BehaviorSettings();
        }

        private void SetDefaults()
        {
            Version = CurrentVersion;
            Behavior = new BehaviorSettings();
        }
    }

    /// <summary>Suggestions, commit keys and casing.</summary>
    [DataContract]
    public sealed class BehaviorSettings
    {
        public BehaviorSettings()
        {
            SetDefaults();
        }

        /// <summary>When off, the extension does not take part in completion and the built-in list shows.</summary>
        [DataMember(Name = "suggestionsEnabled")]
        public bool SuggestionsEnabled { get; set; }

        // Enter and Tab always insert the selected suggestion.
        [DataMember(Name = "commitOnSpace")]
        public bool CommitOnSpace { get; set; }

        [DataMember(Name = "commitOnDot")]
        public bool CommitOnDot { get; set; }

        [DataMember(Name = "commitOnComma")]
        public bool CommitOnComma { get; set; }

        [DataMember(Name = "commitOnParentheses")]
        public bool CommitOnParentheses { get; set; }

        [DataMember(Name = "commitOnSemicolon")]
        public bool CommitOnSemicolon { get; set; }

        [DataMember(Name = "commitOnClosingBracket")]
        public bool CommitOnClosingBracket { get; set; }

        public KeywordCase KeywordCase { get; set; }

        /// <summary>The JSON serializer writes enums as numbers; the file holds the readable name instead.</summary>
        [DataMember(Name = "keywordCase")]
        private string KeywordCaseText
        {
            get => KeywordCase.ToString();
            set => KeywordCase = Enum.TryParse(value, true, out KeywordCase parsed) ? parsed : KeywordCase.MatchTyped;
        }

        public CommitKeys ToCommitKeys() =>
            new CommitKeys(CommitOnSpace, CommitOnDot, CommitOnComma, CommitOnParentheses, CommitOnSemicolon, CommitOnClosingBracket);

        public BehaviorSettings Clone() => (BehaviorSettings)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults()
        {
            // The defaults follow the commit keys of the SQL Prompt settings: only Space (besides Enter and Tab) inserts.
            SuggestionsEnabled = true;
            CommitOnSpace = true;
            CommitOnDot = false;
            CommitOnComma = false;
            CommitOnParentheses = false;
            CommitOnSemicolon = false;
            CommitOnClosingBracket = false;
            KeywordCase = KeywordCase.MatchTyped;
        }
    }
}
