using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The selection actions of the menu (add commas, add quotes and commas): the whole lines touched by the selection are replaced by the
    /// result of a Core function and stay selected, so the action can be undone with one Ctrl+Z and repeated.
    /// </summary>
    internal static class LineCommands
    {
        public static void Run(ITextView view, Func<string, string> transform, string what)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                if (view.Selection.IsEmpty)
                {
                    SetStatus("SQL Prompter: select the lines to add " + what + " to.");
                    return;
                }

                var snapshot = view.TextBuffer.CurrentSnapshot;
                var selection = view.Selection.StreamSelectionSpan.SnapshotSpan;
                var first = snapshot.GetLineFromPosition(selection.Start);
                var last = snapshot.GetLineFromPosition(selection.End);

                // A selection that ends at the very start of a line (as after selecting whole lines with the mouse) does not include that line.
                if (last.LineNumber > first.LineNumber && selection.End == last.Start)
                {
                    last = snapshot.GetLineFromLineNumber(last.LineNumber - 1);
                }

                var span = new SnapshotSpan(first.Start, last.End);
                var original = span.GetText();
                var changed = transform(original);
                if (changed == original)
                {
                    return;
                }

                var edited = view.TextBuffer.Replace(span, changed);
                view.Selection.Select(new SnapshotSpan(edited, span.Start.Position, changed.Length), false);
                view.Caret.MoveTo(new SnapshotPoint(edited, span.Start.Position + changed.Length));
                Log.Write("Added " + what + " to " + (last.LineNumber - first.LineNumber + 1) + " lines.");
            }
            catch (Exception ex)
            {
                Log.Error("Adding " + what + " failed", ex);
            }
        }

        private static void SetStatus(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText(text);
            }
        }
    }
}
