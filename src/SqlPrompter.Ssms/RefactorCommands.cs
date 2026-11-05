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

                ReplaceDifference(view, span.Start.Position, original, changed);
                Log.Write("Refactoring: " + what + ".");
                SetStatus("SQL Prompter: " + what + " done.");
            }
            catch (Exception ex)
            {
                Log.Error("Refactoring (" + what + ") failed", ex);
            }
        }

        /// <summary>Expands every select-list star of the selection (or of the whole script) with the metadata of the active window.</summary>
        public static void ExpandStars(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                var snapshot = view.TextBuffer.CurrentSnapshot;
                var selection = view.Selection.IsEmpty
                    ? new SnapshotSpan(snapshot, 0, snapshot.Length)
                    : view.Selection.StreamSelectionSpan.SnapshotSpan;
                var cache = MetadataService.Current?.CacheForActiveWindow(out _, SqlBatch.FindUsedDatabase(snapshot, selection.Start.Position));
                var metadata = cache?.Current;
                if (metadata == null)
                {
                    SetStatus("SQL Prompter: the metadata of the database is not loaded yet.");
                    return;
                }

                var original = snapshot.GetText();
                var options = SettingsService.Current.ToSuggestionOptions();
                var changed = SqlPrompter.Core.Completion.StarExpanderAll.Expand(
                    original, selection.Start.Position, selection.End.Position, metadata, options, out var expanded, out var skipped);
                if (changed == original)
                {
                    SetStatus(skipped > 0
                        ? "SQL Prompter: " + skipped + " * could not be expanded (unknown tables)."
                        : "SQL Prompter: no * to expand.");
                    return;
                }

                ReplaceDifference(view, 0, original, changed);
                SetStatus("SQL Prompter: expanded " + expanded + " * " + (skipped > 0 ? "(" + skipped + " could not be expanded)." : "."));
                Log.Write("Refactoring: expanded " + expanded + " stars, skipped " + skipped + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Expanding the stars failed", ex);
            }
        }

        /// <summary>Puts the schema in front of the table, view, function and procedure names of the selection (or the whole script).</summary>
        public static void QualifyNames(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                var snapshot = view.TextBuffer.CurrentSnapshot;
                var selection = view.Selection.IsEmpty
                    ? new SnapshotSpan(snapshot, 0, snapshot.Length)
                    : view.Selection.StreamSelectionSpan.SnapshotSpan;
                var metadata = MetadataService.Current?.CacheForActiveWindow(out _, SqlBatch.FindUsedDatabase(snapshot, selection.Start.Position))?.Current;
                if (metadata == null)
                {
                    SetStatus("SQL Prompter: the metadata of the database is not loaded yet.");
                    return;
                }

                var original = snapshot.GetText();
                var changed = SqlPrompter.Core.Editing.NameQualifier.Qualify(original, selection.Start.Position, selection.End.Position, metadata, out var count);
                if (changed == original)
                {
                    SetStatus("SQL Prompter: no names to qualify.");
                    return;
                }

                ReplaceDifference(view, 0, original, changed);
                SetStatus("SQL Prompter: qualified " + count + " names.");
                Log.Write("Refactoring: qualified " + count + " names.");
            }
            catch (Exception ex)
            {
                Log.Error("Qualifying names failed", ex);
            }
        }

        /// <summary>Describes the selection (or the whole script) in a new query window of the same connection, as a comment.</summary>
        public static void ShowSummary(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                var snapshot = view.TextBuffer.CurrentSnapshot;
                var text = view.Selection.IsEmpty ? snapshot.GetText() : view.Selection.StreamSelectionSpan.SnapshotSpan.GetText();
                var newLine = snapshot.LineCount > 1 ? snapshot.GetLineFromLineNumber(0).GetLineBreakText() : "\r\n";
                var summary = SqlPrompter.Core.Editing.ScriptSummary.Build(text, newLine.Length > 0 ? newLine : "\r\n");
                if (!SsmsConnection.OpenNewWindow(SsmsConnection.GetActive()?.Database, summary))
                {
                    System.Windows.Clipboard.SetText(summary);
                    SetStatus("SQL Prompter: the summary is on the clipboard (no connected window to open it in).");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Script summary failed", ex);
            }
        }

        /// <summary>Formats the whole script, or the selection, with the formatting style of the options.</summary>
        public static void FormatScript(ITextView view, bool wholeDocument)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                var snapshot = view.TextBuffer.CurrentSnapshot;
                if (!wholeDocument && view.Selection.IsEmpty)
                {
                    SetStatus("SQL Prompter: select the text to format first.");
                    return;
                }

                var span = wholeDocument || view.Selection.IsEmpty
                    ? new SnapshotSpan(snapshot, 0, snapshot.Length)
                    : view.Selection.StreamSelectionSpan.SnapshotSpan;
                var original = span.GetText();
                var newLine = snapshot.LineCount > 1 && snapshot.GetLineFromLineNumber(0).LineBreakLength > 0
                    ? snapshot.GetLineFromLineNumber(0).GetLineBreakText()
                    : "\r\n";
                var result = SqlPrompter.Core.Formatting.SqlFormatter.Format(original, SettingsService.Current.Formatting, newLine);

                // A trailing line break of the original stays; the formatter writes none.
                var changed = result.Text;
                var trailing = original.Substring(original.TrimEnd().Length);
                changed = changed.TrimEnd() + trailing;
                if (changed == original)
                {
                    SetStatus("SQL Prompter: already formatted.");
                    return;
                }

                ReplaceDifference(view, span.Start.Position, original, changed);
                var kept = result.Kept > 0 ? " (" + result.Kept + " statements kept as written: comments inside or not safe to change)" : string.Empty;
                SetStatus("SQL Prompter: formatted " + result.Formatted + " statements" + kept + ".");
                Log.Write("Formatted " + result.Formatted + " statements, kept " + result.Kept + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Formatting failed", ex);
            }
        }

        /// <summary>Replaces only the middle that differs between the two texts; <paramref name="offset"/> is where the original starts in the buffer.</summary>
        private static void ReplaceDifference(ITextView view, int offset, string original, string changed)
        {
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

            var replaced = new Span(offset + prefix, original.Length - prefix - suffix);
            view.TextBuffer.Replace(replaced, changed.Substring(prefix, changed.Length - prefix - suffix));
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
