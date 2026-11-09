using System;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Hides the item <i>Set Tab Color</i> that SSMS puts into the context menu of a document tab (and the Window menu). It is a submenu of the shell with
    /// a fixed palette; the shell shows it only while at least one of its commands is visible, so a priority command target that answers the
    /// QueryStatus of those commands as invisible makes it disappear (verified in SSMS 22.11, see docs/architecture.md). The setting is read on every
    /// query, so switching the option takes effect at once. The SSMS option <i>Tab colorization method</i> stays untouched (None would stop our
    /// tab colors being drawn).
    /// </summary>
    internal sealed class HideSsmsTabColor : IOleCommandTarget
    {
        /// <summary>The command group of the document tab well; ids 2 to 18 are the entries of the palette (3 to 18 the colors, 2 the item to remove a color). Id 19 is <i>Restore Closed Tab</i> and must stay.</summary>
        private static readonly Guid TabWellGroup = new Guid("43f755c7-7916-454d-81a9-90d4914019dd");

        private const uint FirstId = 2;
        private const uint LastId = 18;

        private uint cookie;

        public static void Register()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var service = Package.GetGlobalService(typeof(SVsRegisterPriorityCommandTarget)) as IVsRegisterPriorityCommandTarget;
            if (service == null)
            {
                Log.Write("The priority command target service was not found; the SSMS item Set Tab Color stays.");
                return;
            }

            var target = new HideSsmsTabColor();
            var hr = service.RegisterPriorityCommandTarget(0, target, out target.cookie);
            if (hr != 0)
            {
                Log.Write("Registering the priority command target failed (" + hr + "); the SSMS item Set Tab Color stays.");
            }
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            try
            {
                if (cCmds == 1 && pguidCmdGroup == TabWellGroup && prgCmds[0].cmdID >= FirstId && prgCmds[0].cmdID <= LastId && SettingsService.Current.Coloring.HideSsmsTabColorMenu)
                {
                    prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_INVISIBLE);
                    return 0;
                }
            }
            catch
            {
                // A command query must never fail because of us.
            }

            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) =>
            (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
    }
}
