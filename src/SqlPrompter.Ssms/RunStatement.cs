using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Analysis;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Shift+F5: runs only the statement at the caret; Ctrl+Shift+F5: runs everything from the start of the script to the end of the statement at the caret
    /// (#420). The span is selected, <c>Query.Execute</c> (which runs the selection) is called and the selection or the caret is put back. A selection the user
    /// made is run as it is, like F5 does. The executed text is marked for a moment (<see cref="RunHighlighter"/>).
    /// </summary>
    internal static class RunStatement
    {
        public static void Run(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Run(view, StatementLocator.Find, "the statement at the caret");
        }

        public static void RunUpToCaret(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Run(view, StatementLocator.FindUpTo, "the statements up to the caret");
        }

        private static void Run(ITextView view, Func<string, int, StatementRange> locate, string what)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (!view.Selection.IsEmpty)
                {
                    dte.ExecuteCommand("Query.Execute");
                    return;
                }

                var snapshot = view.TextBuffer.CurrentSnapshot;
                var caret = view.Caret.Position.BufferPosition;
                var range = locate(snapshot.GetText(), caret.Position);
                if (range == null)
                {
                    SetStatus("SQL Prompter: no statement " + (what.Contains("up to") ? "before" : "at") + " the caret.");
                    return;
                }

                var span = new SnapshotSpan(snapshot, range.Start, range.Length);
                view.Selection.Select(span, false);
                try
                {
                    dte.ExecuteCommand("Query.Execute");
                }
                finally
                {
                    view.Selection.Clear();
                    view.Caret.MoveTo(caret.TranslateTo(view.TextBuffer.CurrentSnapshot, PointTrackingMode.Positive));
                }

                if (view is IWpfTextView wpfView)
                {
                    RunHighlighter.Show(wpfView, span);
                }

                Log.Write("Ran " + what + " at " + range.Start + " (" + range.Length + " characters).");
            }
            catch (Exception ex)
            {
                Log.Error("Running " + what + " failed", ex);
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
