using System;
using System.Linq;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Completion;

namespace SqlPrompter.Ssms
{
    /// <summary>Reads what the editor looks like at a position, for the text SQL Prompter inserts over several lines.</summary>
    internal static class EditorLayouts
    {
        /// <summary>The line the insertion starts on, its indentation and the width of the editor. Must run on the UI thread.</summary>
        public static EditorLayout Read(ITextView textView, ITextSnapshot snapshot, int position)
        {
            var layout = new EditorLayout { Style = SettingsService.Current.Formatting };
            try
            {
                var line = snapshot.GetLineFromPosition(Math.Min(position, snapshot.Length));
                var text = line.GetText();
                layout.LinePrefix = text.Substring(0, Math.Min(text.Length, position - line.Start.Position));
                layout.LineIndent = new string(text.TakeWhile(c => c == ' ' || c == '\t').ToArray());
                var firstBreak = snapshot.GetLineFromLineNumber(0).GetLineBreakText();
                layout.NewLine = line.LineBreakLength > 0 ? line.GetLineBreakText() : firstBreak.Length > 0 ? firstBreak : "\r\n";
                var options = textView.Options;
                var spaces = options.GetOptionValue(DefaultOptions.ConvertTabsToSpacesOptionId);
                layout.IndentUnit = spaces ? new string(' ', Math.Max(1, options.GetOptionValue(DefaultOptions.IndentSizeOptionId))) : "\t";
                var columnWidth = textView is IWpfTextView wpf && wpf.FormattedLineSource != null ? wpf.FormattedLineSource.ColumnWidth : 0;
                if (columnWidth > 0 && textView.ViewportWidth > 0)
                {
                    layout.LineWidth = Math.Max(40, (int)(textView.ViewportWidth / columnWidth) - 2);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reading the editor layout failed", ex);
            }

            return layout;
        }
    }
}
