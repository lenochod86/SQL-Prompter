namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// Decides whether Ctrl was tapped: pressed and released on its own. Besides the key events of the editor, the caller passes a counter of
    /// all other input seen on the thread (keys, mouse buttons and wheel): chords that SSMS routes to commands (Ctrl+A, Ctrl+Space …) never
    /// reach the key events of the editor, but they do change the counter.
    /// </summary>
    public sealed class CtrlTapDetector
    {
        private bool armed;
        private long inputAtDown;

        /// <summary>Ctrl went down. Only the first press (not the key repeat of a held Ctrl) with no other modifier starts a tap.</summary>
        public void CtrlDown(bool isRepeat, bool onlyCtrlDown, long otherInput)
        {
            armed = !isRepeat && onlyCtrlDown;
            if (armed)
            {
                inputAtDown = otherInput;
            }
        }

        /// <summary>Any other key was seen by the editor, or the focus or the mouse was taken: no tap.</summary>
        public void Interrupted() => armed = false;

        /// <summary>Ctrl went up: true when this ends a tap of Ctrl on its own.</summary>
        public bool CtrlUp(bool noModifierDown, long otherInput)
        {
            var tapped = armed && noModifierDown && otherInput == inputAtDown;
            armed = false;
            return tapped;
        }
    }
}
