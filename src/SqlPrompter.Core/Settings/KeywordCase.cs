namespace SqlPrompter.Core.Settings
{
    /// <summary>How keywords, built-in functions and data types are inserted.</summary>
    public enum KeywordCase
    {
        /// <summary>Upper case, lower case if the user started typing in lower case.</summary>
        MatchTyped,

        Upper,

        Lower,
    }
}
