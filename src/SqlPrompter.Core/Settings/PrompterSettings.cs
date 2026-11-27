using System;
using System.Collections.Generic;
using System.Linq;
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
            EnsureStyles();
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

        [DataMember(Name = "updates")]
        public UpdateSettings Updates { get; set; }

        /// <summary>The active formatting style (what Format Document and the inserted lists use). It is always a copy of the active named style.</summary>
        [DataMember(Name = "formatting")]
        public FormatStyle Formatting { get; set; }

        /// <summary>The named formatting styles; there is always at least one.</summary>
        [DataMember(Name = "formattingStyles")]
        public List<NamedFormatStyle> FormattingStyles { get; set; }

        /// <summary>The name of the active entry of <see cref="FormattingStyles"/>.</summary>
        [DataMember(Name = "activeFormattingStyle")]
        public string ActiveFormattingStyle { get; set; }

        public PrompterSettings Clone() => new PrompterSettings { Version = Version, Behavior = Behavior.Clone(), Aliases = Aliases.Clone(), Joins = Joins.Clone(), Inserts = Inserts.Clone(), Snippets = Snippets.Clone(), Coloring = Coloring.Clone(), Analysis = Analysis.Clone(), Updates = Updates.Clone(), Formatting = Formatting.Clone(), FormattingStyles = (FormattingStyles ?? new List<NamedFormatStyle>()).Select(s => s.Clone()).ToList(), ActiveFormattingStyle = ActiveFormattingStyle };

        public SqlPrompter.Core.Completion.SuggestionOptions ToSuggestionOptions() =>
            new SqlPrompter.Core.Completion.SuggestionOptions(Behavior.KeywordCase, Aliases, null, Joins, null, Inserts, new SqlPrompter.Core.Completion.EditorLayout { Style = Formatting });

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        /// <summary>Code that assigns <see cref="Formatting"/> directly (an import) is honored: the active style is written from it.</summary>
        [OnSerializing]
        private void OnSerializing(StreamingContext context)
        {
            if (Formatting != null && FormattingStyles != null && FormattingStyles.Count > 0)
            {
                ActiveStyle.Style = Formatting.Clone();
            }
        }

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
            Updates = Updates ?? new UpdateSettings();
            Formatting = Formatting ?? new FormatStyle();
            EnsureStyles();
        }

        /// <summary>The name given to the style of a settings file from before there were several.</summary>
        public const string DefaultStyleName = "Default";

        /// <summary>The entry of <see cref="FormattingStyles"/> that is active.</summary>
        public NamedFormatStyle ActiveStyle => FormattingStyles.FirstOrDefault(s => string.Equals(s.Name, ActiveFormattingStyle, StringComparison.OrdinalIgnoreCase)) ?? FormattingStyles[0];

        /// <summary>
        /// Makes the list of styles valid: at least one style (a file from before the styles gets its single style as "Default"), unique non-empty names,
        /// an active name that exists; then <see cref="Formatting"/> is the active style.
        /// </summary>
        public void EnsureStyles()
        {
            var styles = (FormattingStyles ?? new List<NamedFormatStyle>()).Where(s => s != null).ToList();
            if (styles.Count == 0)
            {
                styles.Add(new NamedFormatStyle(DefaultStyleName, (Formatting ?? new FormatStyle()).Clone()));
                ActiveFormattingStyle = DefaultStyleName;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var style in styles)
            {
                var name = string.IsNullOrWhiteSpace(style.Name) ? "Style" : style.Name.Trim();
                var unique = name;
                for (var i = 2; !seen.Add(unique); i++)
                {
                    unique = name + " " + i;
                }

                style.Name = unique;
            }

            FormattingStyles = styles;
            if (!styles.Any(s => string.Equals(s.Name, ActiveFormattingStyle, StringComparison.OrdinalIgnoreCase)))
            {
                ActiveFormattingStyle = styles[0].Name;
            }

            Formatting = ActiveStyle.Style.Clone();
        }

        /// <summary>Replaces the values of the active style (an import of a SQL Prompt style) and keeps <see cref="Formatting"/> in step.</summary>
        public void SetActiveStyleValues(FormatStyle style)
        {
            ActiveStyle.Style = style.Clone();
            Formatting = style.Clone();
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
            Updates = new UpdateSettings();
            Formatting = new FormatStyle();
            // Empty on purpose: a file from before the styles has only 'formatting', which EnsureStyles turns into the first style.
            FormattingStyles = new List<NamedFormatStyle>();
            ActiveFormattingStyle = null;
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

        /// <summary>The typed text may also pick the humps or words of a name (BKT for BurzaKlientTyp) or its characters in order; without it only a prefix or a substring matches.</summary>
        [DataMember(Name = "looseMatching")]
        public bool LooseMatching { get; set; }

        /// <summary>Items chosen more often come first in their group; the counts stay on this machine.</summary>
        [DataMember(Name = "rankByUsage")]
        public bool RankByUsage { get; set; }

        /// <summary>Typing <c>'</c> also inserts the closing quote (#440).</summary>
        [DataMember(Name = "autoCloseQuote")]
        public bool AutoCloseQuote { get; set; }

        /// <summary>Typing <c>(</c> also inserts the closing parenthesis.</summary>
        [DataMember(Name = "autoCloseParenthesis")]
        public bool AutoCloseParenthesis { get; set; }

        /// <summary>Typing <c>[</c> also inserts the closing bracket.</summary>
        [DataMember(Name = "autoCloseBracket")]
        public bool AutoCloseBracket { get; set; }

        /// <summary>Typing <c>/*</c> also inserts <c> */</c>.</summary>
        [DataMember(Name = "autoCloseBlockComment")]
        public bool AutoCloseBlockComment { get; set; }

        /// <summary>
        /// The single switch of the settings files from before there was one per pair: it is read once (on = the quote, the parenthesis and the bracket on, off = all
        /// three off; the block comment pair did not exist and keeps its default) and never written again. A value of a pair in the same file wins (it is read later).
        /// </summary>
        [DataMember(Name = "autoClose", EmitDefaultValue = false)]
        private bool? LegacyAutoClose
        {
            get => null;
            set
            {
                if (value.HasValue)
                {
                    AutoCloseQuote = value.Value;
                    AutoCloseParenthesis = value.Value;
                    AutoCloseBracket = value.Value;
                }
            }
        }

        /// <summary>The pairs that are closed automatically.</summary>
        public SqlPrompter.Core.Editing.AutoClosePairs AutoClosePairs =>
            (AutoCloseQuote ? SqlPrompter.Core.Editing.AutoClosePairs.Quote : 0) |
            (AutoCloseParenthesis ? SqlPrompter.Core.Editing.AutoClosePairs.Parenthesis : 0) |
            (AutoCloseBracket ? SqlPrompter.Core.Editing.AutoClosePairs.Bracket : 0) |
            (AutoCloseBlockComment ? SqlPrompter.Core.Editing.AutoClosePairs.BlockComment : 0);

        /// <summary>The list opens by itself after a comma (and the space after it) where the next item is a name or an expression.</summary>
        [DataMember(Name = "suggestAfterComma")]
        public bool SuggestAfterComma { get; set; }

        /// <summary>The description area shows the descriptions that the database keeps (extended property MS_Description) for the selected object and its columns.</summary>
        [DataMember(Name = "showDbDescriptions")]
        public bool ShowDbDescriptions { get; set; }

        /// <summary>A tooltip over a name says what it is (alias, table, column, variable, function, procedure).</summary>
        [DataMember(Name = "showHover")]
        public bool ShowHover { get; set; }

        /// <summary>A note under the caret says that Tab expands a star in a select list.</summary>
        [DataMember(Name = "showStarHint")]
        public bool ShowStarHint { get; set; }

        /// <summary>Pressing and releasing Ctrl on its own opens the quick actions popup.</summary>
        [DataMember(Name = "quickActionsOnCtrl")]
        public bool QuickActionsOnCtrl { get; set; }

        /// <summary>Rows of the suggestion list (set by dragging its corner).</summary>
        [DataMember(Name = "listRows")]
        public int ListRows { get; set; }

        /// <summary>Width of the suggestion list in pixels; 0 follows the longest item.</summary>
        [DataMember(Name = "listWidth")]
        public int ListWidth { get; set; }

        /// <summary>The column picker lists the columns in the order of the table (false: alphabetically); it is remembered.</summary>
        [DataMember(Name = "pickerTableOrder")]
        public bool PickerTableOrder { get; set; }

        /// <summary>
        /// Experimental: the list that opens while typing is run by SQL Prompter itself (the same list as Ctrl+Space) instead of the editor's
        /// completion session (#187).
        /// </summary>
        [DataMember(Name = "ownTypingList")]
        public bool OwnTypingList { get; set; }

        /// <summary>The list says that Ctrl+Left switches to the column picker (the hint can be closed).</summary>
        [DataMember(Name = "showColumnPickerHint")]
        public bool ShowColumnPickerHint { get; set; }

        /// <summary>The other places of the name under the caret are highlighted.</summary>
        [DataMember(Name = "highlightMatches")]
        public bool HighlightMatches { get; set; }

        /// <summary>The keywords that belong together with the one at the caret (BEGIN and END, TRY and CATCH, ...) are highlighted.</summary>
        [DataMember(Name = "highlightBlockKeywords")]
        public bool HighlightBlockKeywords { get; set; }

        /// <summary>The column of an INSERT and the value at the same position are highlighted for one another (#554).</summary>
        [DataMember(Name = "highlightInsertPairs")]
        public bool HighlightInsertPairs { get; set; }

        /// <summary>Every executed query is written to the history on this machine.</summary>
        [DataMember(Name = "keepHistory")]
        public bool KeepHistory { get; set; }

        /// <summary>The query history opens as a tab in the editor area (true) or as a separate window (false, #385).</summary>
        [DataMember(Name = "historyAsTab")]
        public bool HistoryAsTab { get; set; }

        /// <summary>History older than this many days is removed when SSMS starts; 0 keeps it forever.</summary>
        [DataMember(Name = "historyDays")]
        public int HistoryDays { get; set; }

        /// <summary>The history follows the query tabs (which are open, which were closed) and keeps versions of their text (#302); needs <see cref="KeepHistory"/>.</summary>
        [DataMember(Name = "trackTabs")]
        public bool TrackTabs { get; set; }

        /// <summary>How often the open tabs are checked for a change of their text, in seconds (a version is stored at most this often per tab).</summary>
        [DataMember(Name = "tabCheckSeconds")]
        public int TabCheckSeconds { get; set; }

        /// <summary>How many versions of the text are kept per tab (older ones are thinned out).</summary>
        [DataMember(Name = "tabVersionsMax")]
        public int TabVersionsMax { get; set; }

        /// <summary>A restored or reopened tab that used a SQL login is connected again: with the live connection of the same login, or after one Connect dialog per server and login (#322).</summary>
        [DataMember(Name = "reconnectSqlTabs")]
        public bool ReconnectSqlTabs { get; set; }

        /// <summary>The Connect dialog that SSMS opens for a restored tab without a password has Remember Password ticked (#333); off by default, SQL Prompter never forces SSMS to remember a password.</summary>
        [DataMember(Name = "prefillRememberPassword")]
        public bool PrefillRememberPassword { get; set; }

        /// <summary>How many closed query tabs can be reopened with Reopen Closed Query Tab (#226).</summary>
        [DataMember(Name = "closedTabsKept")]
        public int ClosedTabsKept { get; set; }

        /// <summary>A closed query tab can be reopened for this many hours; 0 keeps it until SSMS ends.</summary>
        [DataMember(Name = "closedTabsHours")]
        public int ClosedTabsHours { get; set; }

        /// <summary>The open query tabs, text and connection, are kept and come back when SSMS starts (no passwords).</summary>
        [DataMember(Name = "restoreTabs")]
        public bool RestoreTabs { get; set; }

        /// <summary>Running a DELETE without WHERE asks for confirmation first.</summary>
        [DataMember(Name = "warnDeleteWithoutWhere")]
        public bool WarnDeleteWithoutWhere { get; set; }

        /// <summary>Running an UPDATE without WHERE asks for confirmation first.</summary>
        [DataMember(Name = "warnUpdateWithoutWhere")]
        public bool WarnUpdateWithoutWhere { get; set; }

        /// <summary>Running a script that begins a transaction and never ends it asks for confirmation first (#209).</summary>
        [DataMember(Name = "warnOpenTransaction")]
        public bool WarnOpenTransaction { get; set; }

        public KeywordCase KeywordCase { get; set; }

        /// <summary>The JSON serializer writes enums as numbers; the file holds the readable name instead.</summary>
        [DataMember(Name = "keywordCase")]
        private string KeywordCaseText
        {
            get => KeywordCase.ToString();
            set => KeywordCase = Enum.TryParse(value, true, out KeywordCase parsed) ? parsed : KeywordCase.FollowStyle;
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
            KeywordCase = KeywordCase.FollowStyle;
            LooseMatching = true;
            RankByUsage = true;
            AutoCloseQuote = true;
            AutoCloseParenthesis = true;
            AutoCloseBracket = true;
            AutoCloseBlockComment = true;
            SuggestAfterComma = true;
            HighlightMatches = true;
            HighlightBlockKeywords = true;
            HighlightInsertPairs = true;
            PickerTableOrder = false;
            ShowColumnPickerHint = true;
            OwnTypingList = false;
            ListRows = 12;
            ListWidth = 0;
            QuickActionsOnCtrl = true;
            ShowStarHint = true;
            ShowDbDescriptions = true;
            ShowHover = true;
            KeepHistory = true;
            HistoryAsTab = true;
            HistoryDays = 0;
            TrackTabs = true;
            TabCheckSeconds = 5;
            TabVersionsMax = 50;
            ClosedTabsKept = 20;
            ClosedTabsHours = 24;
            RestoreTabs = true;
            ReconnectSqlTabs = true;
            PrefillRememberPassword = false;
            WarnDeleteWithoutWhere = true;
            WarnUpdateWithoutWhere = true;
            WarnOpenTransaction = true;
        }
    }
}
