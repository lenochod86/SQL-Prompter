using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Preview as SELECT and Run and roll back (#210): the UPDATE or DELETE of the selection (or at the caret) is turned into a script by
    /// <see cref="StatementPreview"/> and opened in a new query window on the same connection. Nothing is executed and the original is not touched.
    /// </summary>
    internal static class PreviewStatement
    {
        public static void AsSelect(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Run(view, StatementPreview.ToSelect, "SELECT preview");
        }

        public static void DryRun(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Run(view, StatementPreview.ToDryRun, "dry run");
        }

        private static void Run(ITextView view, Func<string, string, PreviewResult> convert, string what)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                var snapshot = view.TextBuffer.CurrentSnapshot;
                string statement;
                if (!view.Selection.IsEmpty)
                {
                    statement = view.Selection.StreamSelectionSpan.SnapshotSpan.GetText();
                }
                else
                {
                    var range = StatementLocator.Find(snapshot.GetText(), view.Caret.Position.BufferPosition.Position);
                    if (range == null)
                    {
                        SetStatus("SQL Prompter: no statement at the caret.");
                        return;
                    }

                    statement = snapshot.GetText(range.Start, range.Length);
                }

                var result = convert(statement, Environment.NewLine);
                if (!result.Succeeded)
                {
                    SetStatus("SQL Prompter: " + result.Reason);
                    return;
                }

                var connection = SsmsConnection.GetActive();
                var database = SqlBatch.FindUsedDatabase(snapshot, view.Caret.Position.BufferPosition.Position) ?? connection?.Database;
                if (connection == null || !SsmsConnection.OpenNewWindow(database, result.Text))
                {
                    SetStatus("SQL Prompter: the new window could not be opened (is the window connected?).");
                    return;
                }

                Log.Write("Preview: " + what + " of a statement of " + statement.Length + " characters opened in a new window.");
                SetStatus("SQL Prompter: " + what + " opened in a new window; nothing was executed.");
            }
            catch (Exception ex)
            {
                Log.Error("Previewing the statement failed", ex);
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
