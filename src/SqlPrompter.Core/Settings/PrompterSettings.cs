using System;
using System.Runtime.Serialization;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Formatting;

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

        [DataMember(Name = "aliases")]
        public AliasSettings Aliases { get; set; }

        [DataMember(Name = "joins")]
        public JoinSettings Joins { get; set; }

        [DataMember(Name = "inserts")]
        public InsertSettings Inserts { get; set; }

        [DataMember(Name = "snippets")]
        public SnippetSettings Snippets { get; set; }

        [DataMember(Name = "coloring")]
        public ColorSettings Coloring { get; set; }

        [DataMember(Name = "analysis")]
        public AnalysisSettings Analysis { get; set; }

        [DataMember(Name = "formatting")]
        public FormatStyle Formatting { get; set; }

        public PrompterSettings Clone() => new PrompterSettings { Version = Version, Behavior = Behavior.Clone(), Aliases = Aliases.Clone(), Joins = Joins.Clone(), Inserts = Inserts.Clone(), Snippets = Snippets.Clone(), Coloring = Coloring.Clone(), Analysis = Analysis.Clone(), Formatting = Formatting.Clone() };

        public SqlPrompter.Core.Completion.SuggestionOptions ToSuggestionOptions() =>
            new SqlPrompter.Core.Completion.SuggestionOptions(Behavior.KeywordCase, Aliases, null, Joins, null, Inserts);

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            // "behavior": null in the file.
            Behavior = Behavior ?? new BehaviorSettings();
            Aliases = Aliases ?? new AliasSettings();
            Joins = Joins ?? new JoinSettings();
            Inserts = Inserts ?? new InsertSettings();
            Snippets = Snippets ?? new SnippetSettings();
            Coloring = Coloring ?? new ColorSettings();
            Analysis = Analysis ?? new AnalysisSettings();
            Formatting = Formatting ?? new FormatStyle();
        }

        private void SetDefaults()
        {
            Version = CurrentVersion;
            Behavior = new BehaviorSettings();
            Aliases = new AliasSettings();
            Joins = new JoinSettings();
            Inserts = new InsertSettings();
            Snippets = new SnippetSettings();
            Coloring = new ColorSettings();
            Analysis = new AnalysisSettings();
            Formatting = new FormatStyle();
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

        /// <summary>Adds the text "SQL Prompter" to the right side of every item so that our list is recognizable at a glance.</summary>
        [DataMember(Name = "markItems")]
        public bool MarkItems { get; set; }

        /// <summary>Items chosen more often come first in their group; the counts stay on this machine.</summary>
        [DataMember(Name = "rankByUsage")]
        public bool RankByUsage { get; set; }

        /// <summary>Typing <c>'</c> or <c>[</c> also inserts the closing character.</summary>
        [DataMember(Name = "autoClose")]
        public bool AutoClose { get; set; }

        /// <summary>The list opens by itself after a comma (and the space after it) where the next item is a name or an expression.</summary>
        [DataMember(Name = "suggestAfterComma")]
        public bool SuggestAfterComma { get; set; }

        /// <summary>The other places of the name under the caret are highlighted.</summary>
        [DataMember(Name = "highlightMatches")]
        public bool HighlightMatches { get; set; }

        /// <summary>Every executed query is written to the history on this machine.</summary>
        [DataMember(Name = "keepHistory")]
        public bool KeepHistory { get; set; }

        /// <summary>History older than this many days is removed when SSMS starts; 0 keeps it forever.</summary>
        [DataMember(Name = "historyDays")]
        public int HistoryDays { get; set; }

        /// <summary>The open query tabs, text and connection, are kept and come back when SSMS starts (no passwords).</summary>
        [DataMember(Name = "restoreTabs")]
        public bool RestoreTabs { get; set; }

        /// <summary>Running a DELETE without WHERE asks for confirmation first.</summary>
        [DataMember(Name = "warnDeleteWithoutWhere")]
        public bool WarnDeleteWithoutWhere { get; set; }

        /// <summary>Running an UPDATE without WHERE asks for confirmation first.</summary>
        [DataMember(Name = "warnUpdateWithoutWhere")]
        public bool WarnUpdateWithoutWhere { get; set; }

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
            MarkItems = true;
            RankByUsage = true;
            AutoClose = true;
            SuggestAfterComma = true;
            HighlightMatches = true;
            KeepHistory = true;
            HistoryDays = 0;
            RestoreTabs = true;
            WarnDeleteWithoutWhere = true;
            WarnUpdateWithoutWhere = true;
        }
    }
}
