namespace SqlPrompter.Core.Coloring
{
    /// <summary>
    /// Decides whether the color of a tab is as we want it (#439). The shell sets the accent brush of a tab again whenever its caption changes (a running
    /// query shows "Executing..."), so the state of the window is compared with the wanted color each time the shell changes it.
    /// </summary>
    public static class TabColorGuard
    {
        /// <summary>True when nothing has to be set: the accent is the wanted color (compared as ARGB) and the tab is marked as colored.</summary>
        public static bool IsInPlace(int? currentArgb, bool currentIsColorized, int wantedArgb) =>
            currentIsColorized && currentArgb.HasValue && currentArgb.Value == wantedArgb;

        /// <summary>
        /// Whether a change of the accent brush or of the colorized flag that the shell made has to be answered at once: only for a window we color, and not
        /// while we are setting the values ourselves (so the handler does not trigger itself).
        /// </summary>
        public static bool MustReassert(bool weColorTheWindow, bool settingOurselves, int? currentArgb, bool currentIsColorized, int wantedArgb) =>
            weColorTheWindow && !settingOurselves && !IsInPlace(currentArgb, currentIsColorized, wantedArgb);
    }
}
