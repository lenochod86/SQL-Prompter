using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Completion
{
    /// <summary>The settings that influence what suggestions contain.</summary>
    public sealed class SuggestionOptions
    {
        /// <summary>Plain suggestions: no aliases, columns qualified only when ambiguous. The product takes its options from the settings.</summary>
        public static SuggestionOptions Default { get; } = new SuggestionOptions(
            KeywordCase.MatchTyped,
            new AliasSettings { AddAliases = false, QualifyColumnsWithAliases = false });

        public SuggestionOptions(KeywordCase keywordCase, AliasSettings aliases)
        {
            KeywordCase = keywordCase;
            Aliases = aliases ?? new AliasSettings();
        }

        public KeywordCase KeywordCase { get; }

        public AliasSettings Aliases { get; }
    }
}
