using System;

namespace SqlPrompter.Core.History
{
    /// <summary>
    /// Follows a shutdown of SSMS that has started (the main window is being closed) but may still be cancelled, e.g. with Cancel in a "save
    /// changes?" dialog. While it is pending the closing of the windows is not the user's doing and the session file is left alone (it was written
    /// when the shutdown began). A modal dialog keeps the main window disabled; when the window has been enabled again for <see cref="CancelAfter"/>
    /// without the shutdown going on, it was cancelled.
    /// </summary>
    public sealed class TabShutdownGuard
    {
        private DateTime? enabledSince;

        public TabShutdownGuard()
        {
            CancelAfter = TimeSpan.FromSeconds(3);
        }

        /// <summary>How long the main window must be enabled without the shutdown going on before the shutdown counts as cancelled.</summary>
        public TimeSpan CancelAfter { get; set; }

        /// <summary>A shutdown has begun and is neither finished nor cancelled.</summary>
        public bool Pending { get; private set; }

        public void Begin()
        {
            Pending = true;
            enabledSince = null;
        }

        /// <summary>Called on a timer while pending; true once when the shutdown turns out to be cancelled (the guard is then no longer pending).</summary>
        public bool Poll(DateTime now, bool mainWindowEnabled)
        {
            if (!Pending)
            {
                return false;
            }

            if (!mainWindowEnabled)
            {
                enabledSince = null;
                return false;
            }

            if (enabledSince == null)
            {
                enabledSince = now;
            }

            if (now - enabledSince.Value < CancelAfter)
            {
                return false;
            }

            Pending = false;
            enabledSince = null;
            return true;
        }
    }
}
