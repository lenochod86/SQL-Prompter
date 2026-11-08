namespace SqlPrompter.Core.Settings
{
    /// <summary>How keywords, built-in functions and data types are inserted.</summary>
    public enum KeywordCase
    {
        /// <summary>Upper case, lower case if the user started typing in lower case.</summary>
        MatchTyped,

        Upper,

        Lower,

        /// <summary>The case of the active formatting style (keywords, built-in functions and data types each have their own); where the style leaves the case as written the typed text decides (#354).</summary>
        FollowStyle,
    }
}