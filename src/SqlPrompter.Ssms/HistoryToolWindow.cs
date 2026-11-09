using System;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The query history as a tab of the editor area (#385): the content of <see cref="HistoryWindow"/> hosted in a tool window that SSMS shows in the
    /// document well (<c>VsDockStyle.MDI</c>), so it can be docked, floated and pinned like the other tabs. It is not restored at the start of SSMS
    /// (<c>Transient</c>). One instance: opening the history again activates it and reads the entries again.
    /// </summary>
    [Guid("6c1a1c0e-5a0e-4f5e-9d3e-0b7f8a1f3a10")]
    public sealed class HistoryToolWindowPane : ToolWindowPane
    {
        public HistoryToolWindowPane()
            : base(null)
        {
            Caption = "SQL History";
            ThreadHelper.ThrowIfNotOnUIThread();
            Reload();
        }

        private void CloseTab()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            (Frame as IVsWindowFrame)?.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
        }

        private HistoryWindow window;

        /// <summary>The content was built a moment ago by the constructor: the first opening does not read the entries again.</summary>
        internal bool JustCreated { get; set; } = true;

        /// <summary>Reads the entries again into the content that is shown (the history is opened again).</summary>
        internal void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            window?.Refresh();
        }

        private void Reload()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            window = new HistoryWindow
            {
                Picked = (entry, makeSnippet) => SqlPrompterPackage.OpenHistoryEntry(entry, makeSnippet),
                CloseRequested = CloseTab,
            };
            var content = (UIElement)window.Content;
            window.Content = null;
            Content = content;
        }
    }
}
