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
