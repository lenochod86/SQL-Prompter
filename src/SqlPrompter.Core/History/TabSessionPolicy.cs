namespace SqlPrompter.Core.History
{
    /// <summary>
    /// When the list of open query tabs may be written to the session file. A change the user makes (a tab opened, closed, saved) is written
    /// at once, even when it leaves no tab; the timer that keeps the text of unsaved queries up to date writes a list that has become empty only
    /// after it stayed empty for a second tick, because SSMS may be closing the windows; once SSMS has begun to shut down nothing is written, so
    /// that the closing of the windows is not taken for the user closing them.
    /// </summary>
    public static class TabSessionPolicy
    {
        /// <param name="tabCount">The number of query tabs that are open now.</param>
        /// <param name="sawTabs">Tabs were open or restored in this run (a start that restored nothing must not wipe the saved tabs).</param>
        /// <param name="shuttingDown">SSMS has begun to shut down.</param>
        /// <param name="userChange">The write is the result of an event of the user (opening, closing or saving a tab), not of the timer.</param>
        /// <param name="emptyTicks">How many timer ticks in a row found no tab (counting this one).</param>
        public static bool ShouldSave(int tabCount, bool sawTabs, bool shuttingDown, bool userChange, int emptyTicks)
        {
            if (shuttingDown || !sawTabs)
            {
                return false;
            }

            return tabCount > 0 || userChange || emptyTicks >= 2;
        }
    }
}
