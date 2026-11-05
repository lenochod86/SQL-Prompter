using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Refactoring commands of the menu. A Core function turns the selection (or the whole script when nothing is selected) into the changed
    /// text; only the part that really differs is replaced, so one Ctrl+Z undoes the command and the caret stays where it was.
    /// </summary>
    internal static class RefactorCommands
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
                var snapshot = view.TextBuffer.CurrentSnapshot;
                var span = view.Selection.IsEmpty
                    ? new SnapshotSpan(snapshot, 0, snapshot.Length)
                    : view.Selection.StreamSelectionSpan.SnapshotSpan;
                var original = span.GetText();
                var changed = transform(original);
                if (changed == original)
                {
                    SetStatus("SQL Prompter: nothing to change (" + what + ").");
                    return;
                }

                var prefix = 0;
                var limit = Math.Min(original.Length, changed.Length);
                while (prefix < limit && original[prefix] == changed[prefix])
                {
                    prefix++;
                }

                var suffix = 0;
                while (suffix < limit - prefix && original[original.Length - 1 - suffix] == changed[changed.Length - 1 - suffix])
                {
                    suffix++;
                }

                var replaced = new Span(span.Start.Position + prefix, original.Length - prefix - suffix);
                view.TextBuffer.Replace(replaced, changed.Substring(prefix, changed.Length - prefix - suffix));
                Log.Write("Refactoring: " + what + ".");
                SetStatus("SQL Prompter: " + what + " done.");
            }
            catch (Exception ex)
            {
                Log.Error("Refactoring (" + what + ") failed", ex);
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
