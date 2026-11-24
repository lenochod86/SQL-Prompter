namespace SqlPrompter.Core.Completion
{
    /// <summary>Who commits when Enter is pressed while a suggestion list is open (#517).</summary>
    public static class EnterCommitRule
    {
        /// <summary>
        /// Whether the editor's own completion session commits its selected item on Enter. Never while the list SQL Prompter runs itself is open: that list has the
        /// selection the user sees (the arrow keys move it), the editor's session may be a stale one from the typing before (a snippet prefix) with another
        /// selected item, and committing it inserts the wrong item.
        /// </summary>
        public static bool EditorSessionShouldCommit(bool ownListOpen, bool sessionDismissed, bool hardSelection, bool suggestionItemSelected, bool hasSelectedItem)
        {
            return !ownListOpen && !sessionDismissed && hardSelection && !suggestionItemSelected && hasSelectedItem;
        }
    }
}
