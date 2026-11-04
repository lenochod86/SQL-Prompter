using System;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Ssms
{
    /// <summary>Puts an expanded snippet into the editor in place of a span of text.</summary>
    internal static class SnippetInserter
    {
        /// <summary>Expands the snippet in place of the typed prefix of a completion session.</summary>
        /// <returns>True if the snippet was inserted; false leaves the buffer untouched so that the item is committed as plain text.</returns>
        public static bool TryInsert(IAsyncCompletionSession session, ITextBuffer buffer, Snippet snippet)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var span = session.ApplicableToSpan.GetSpan(buffer.CurrentSnapshot);
                Insert(session.TextView, span, snippet, selectedText: null);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Expanding the snippet failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Replaces <paramref name="span"/> with the expanded snippet. <paramref name="selectedText"/> is what <c>$SELECTEDTEXT$</c> stands for;
        /// null means the text of the editor selection, if any.
        /// </summary>
        public static void Insert(ITextView view, SnapshotSpan span, Snippet snippet, string selectedText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var buffer = view.TextBuffer;
            var snapshot = span.Snapshot;
            var line = snapshot.GetLineFromPosition(span.Start.Position);
            var lineText = line.GetText();
            var indentLength = 0;
            while (indentLength < lineText.Length && (lineText[indentLength] == ' ' || lineText[indentLength] == '\t'))
            {
                indentLength++;
            }

            var selection = view.Selection;
            var connection = SsmsConnection.GetActive();
            var context = new SnippetContext
            {
                SelectedText = selectedText ?? (selection.IsEmpty ? string.Empty : selection.SelectedSpans[0].GetText()),
                User = connection != null && !string.IsNullOrEmpty(connection.UserName) ? connection.UserName : Environment.UserName,
                DatabaseName = SqlBatch.FindUsedDatabase(snapshot, span.Start.Position) ?? connection?.Database ?? string.Empty,
                NewLine = string.IsNullOrEmpty(line.GetLineBreakText()) ? "\r\n" : line.GetLineBreakText(),
                Indent = lineText.Substring(0, Math.Min(indentLength, span.Start.Position - line.Start.Position)),
            };
            var expanded = SnippetExpander.Expand(snippet, context);

            var start = span.Start.Position;
            buffer.Replace(span, expanded.Text);
            var current = buffer.CurrentSnapshot;
            if (expanded.SelectionStart >= 0)
            {
                view.Selection.Select(new SnapshotSpan(current, start + expanded.SelectionStart, expanded.SelectionEnd - expanded.SelectionStart), false);
                view.Caret.MoveTo(new SnapshotPoint(current, start + expanded.SelectionEnd));
            }
            else
            {
                view.Selection.Clear();
                view.Caret.MoveTo(new SnapshotPoint(current, start + expanded.CursorOffset));
            }

            if (expanded.Fields.Count > 0 && view is IWpfTextView wpfView)
            {
                SnippetSession.Start(wpfView, start, expanded);
            }

            Log.Write("Snippet \"" + snippet.Prefix + "\" expanded (" + expanded.Fields.Count + " fields).");
        }
    }
}
