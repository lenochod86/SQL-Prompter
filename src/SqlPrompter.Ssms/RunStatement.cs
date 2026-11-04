using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Analysis;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Shift+F5: runs only the statement at the caret. The statement is selected, <c>Query.Execute</c> (which runs the selection) is called
    /// and the selection or the caret is put back. A selection the user made is run as it is, like F5 does.
    /// </summary>
    internal static class RunStatement
    {
        public static void Run(ITextView view)
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
                var range = StatementLocator.Find(snapshot.GetText(), caret.Position);
                if (range == null)
                {
                    SetStatus("SQL Prompter: no statement at the caret.");
                    return;
                }

                view.Selection.Select(new SnapshotSpan(snapshot, range.Start, range.Length), false);
                try
                {
                    dte.ExecuteCommand("Query.Execute");
                }
                finally
                {
                    view.Selection.Clear();
                    view.Caret.MoveTo(caret.TranslateTo(view.TextBuffer.CurrentSnapshot, PointTrackingMode.Positive));
                }

                Log.Write("Ran the statement at " + range.Start + " (" + range.Length + " characters).");
            }
            catch (Exception ex)
            {
                Log.Error("Running the statement at the caret failed", ex);
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
