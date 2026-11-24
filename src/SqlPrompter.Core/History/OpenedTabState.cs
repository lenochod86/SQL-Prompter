namespace SqlPrompter.Core.History
{
    /// <summary>What kind of text a new query tab is opened with (#512).</summary>
    public enum OpenedTabKind
    {
        /// <summary>The script of an object (F12, quick open, find in code, dependencies): the user only looks at it.</summary>
        Definition,

        /// <summary>A generated text the user did not write (a preview, an insert script of grid rows, a summary of a script).</summary>
        Generated,

        /// <summary>The text of a history entry or a restored tab: the user's own work.</summary>
        UserText,
    }

    public static class OpenedTabState
    {
        /// <summary>Whether a tab opened with this kind of text starts unmodified: no mark in the caption and no question when it is closed unchanged.</summary>
        public static bool StartsUnmodified(OpenedTabKind kind) => kind == OpenedTabKind.Definition || kind == OpenedTabKind.Generated;
    }
}