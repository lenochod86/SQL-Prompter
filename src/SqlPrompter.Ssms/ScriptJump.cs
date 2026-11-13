using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Navigation;

namespace SqlPrompter.Ssms
{
    /// <summary>Moves the caret to the start of the next or previous statement or batch of the script (#224); built from <see cref="ScriptOutline"/>.</summary>
    internal static class ScriptJump
    {
        public static void Run(IWpfTextView view, bool batches, bool forward)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            var snapshot = view.TextSnapshot;
            var items = ScriptOutline.Build(snapshot.GetText());
            var target = ScriptOutline.JumpTarget(items, view.Caret.Position.BufferPosition.Position, batches, forward);
            if (target < 0)
            {
                if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
                {
                    statusBar.SetText("No " + (forward ? "next " : "previous ") + (batches ? "batch" : "statement"));
                }

                return;
            }

            var point = new SnapshotPoint(snapshot, System.Math.Min(target, snapshot.Length));
            view.Selection.Clear();
            view.Caret.MoveTo(point);
            view.ViewScroller.EnsureSpanVisible(new SnapshotSpan(point, 0), EnsureSpanVisibleOptions.ShowStart);
        }
    }
}
