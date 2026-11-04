using System.Diagnostics;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SqlPrompter.Ssms
{
    /// <summary>Finds the text view of the active editor window for the menu commands.</summary>
    internal static class ActiveEditor
    {
        /// <summary>The active text view, or null if no editor has the focus.</summary>
        public static IWpfTextView GetView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(Package.GetGlobalService(typeof(SVsTextManager)) is IVsTextManager manager) ||
                manager.GetActiveView(1, null, out var vsView) != VSConstants.S_OK || vsView == null)
            {
                return null;
            }

            var model = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            return model?.GetService<IVsEditorAdaptersFactoryService>()?.GetWpfTextView(vsView);
        }

        /// <summary>Makes a window opened by the package a child of the SSMS main window.</summary>
        public static void SetOwner(System.Windows.Window window)
        {
            new System.Windows.Interop.WindowInteropHelper(window).Owner = Process.GetCurrentProcess().MainWindowHandle;
        }
    }
}
