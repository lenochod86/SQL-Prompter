using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The body of a snippet: a rich text box that colors the text as SQL (keywords, strings, comments, variables) and the fields of the
    /// snippet (<c>$CURSOR$</c>, <c>$name$</c>). The text is plain; every line is a paragraph, and after each change the lines are colored again.
    /// The colors suit a light or a dark window. Undo is off, because coloring replaces the content of the box.
    /// </summary>
    internal sealed class SnippetBodyEditor : RichTextBox
    {
        private bool coloring;

        public SnippetBodyEditor()
        {
            Loaded += (s, e) => ApplyEditorBackground();
            AcceptsReturn = true;
            AcceptsTab = true;
            IsUndoEnabled = false;
            FontFamily = new FontFamily("Consolas");
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Document.PageWidth = 10000;
            Document.Blocks.Clear();
            Document.Blocks.Add(new Paragraph());
            TextChanged += (s, e) =>
            {
                if (!coloring)
                {
                    Recolor(CaretIndex());
                }
            };

            // The colors of the words are brushes of their own, so a change of the SSMS theme while the editor is open has to color the text again
            // (otherwise the light colors of one theme stay on the background of the other until SSMS is restarted).
            Loaded += (s, e) => Microsoft.VisualStudio.PlatformUI.VSColorTheme.ThemeChanged += OnThemeChanged;
            Unloaded += (s, e) => Microsoft.VisualStudio.PlatformUI.VSColorTheme.ThemeChanged -= OnThemeChanged;
        }

        /// <summary>The background of the preview is the background of the SSMS editor, so the colors of the words stand on what they stand on there.</summary>
        private void ApplyEditorBackground()
        {
            var color = EditorColors.Background();
            Background = new SolidColorBrush(color);
            Foreground = new SolidColorBrush(EditorColors.ByKind()[0]);
        }

        private void OnThemeChanged(Microsoft.VisualStudio.PlatformUI.ThemeChangedEventArgs args)
        {
            // The resources of the window (the background and the text color) follow the theme a moment later; the text is colored after that.
#pragma warning disable VSTHRD001, VSTHRD110 // the dispatcher of the control itself: the colors are applied after the resources of the window have changed
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsLoaded)
                {
                    ApplyEditorBackground();
                    Recolor(-1);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
#pragma warning restore VSTHRD001, VSTHRD110
        }

        /// <summary>The plain text, lines separated by CR LF.</summary>
        public string PlainText
        {
            get
            {
                var text = new TextRange(Document.ContentStart, Document.ContentEnd).Text;
                return text.EndsWith("\r\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 2) : text;
            }

            set => Recolor(-1, value ?? string.Empty);
        }

        private int CaretIndex() => new TextRange(Document.ContentStart, CaretPosition).Text.Length;

        private void Recolor(int caret, string text = null)
        {
            coloring = true;
            try
            {
                text = text ?? PlainText;
                if (IsReadOnly)
                {
                    // A preview shows the tabs at the tab stops of the editor; the text of an editor being edited keeps them (#450).
                    text = SqlPrompter.Core.Editing.TabExpander.Expand(text, EditorTabSize.Current());
                }

                var kinds = SnippetHighlighter.Classify(text);
                var brushes = Palette();
                var document = new FlowDocument { PageWidth = 10000, FontFamily = FontFamily, FontSize = FontSize, Foreground = brushes[0] };
                var start = 0;
                foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    var paragraph = new Paragraph { Margin = new Thickness(0) };
                    var i = 0;
                    while (i < line.Length)
                    {
                        var kind = kinds[start + i];
                        var j = i;
                        while (j < line.Length && kinds[start + j] == kind)
                        {
                            j++;
                        }

                        var run = new Run(line.Substring(i, j - i)) { Foreground = brushes[kind] };
                        if (kind == 5)
                        {
                            run.FontWeight = FontWeights.SemiBold;
                        }

                        paragraph.Inlines.Add(run);
                        i = j;
                    }

                    document.Blocks.Add(paragraph);
                    start += line.Length;
                    if (start < text.Length)
                    {
                        start += text[start] == '\r' && start + 1 < text.Length && text[start + 1] == '\n' ? 2 : 1;
                    }
                }

                Document = document;
                if (caret >= 0)
                {
                    CaretPosition = PositionAt(caret) ?? Document.ContentEnd;
                }
            }
            finally
            {
                coloring = false;
            }
        }

        private TextPointer PositionAt(int index)
        {
            foreach (var paragraph in Document.Blocks.OfType<Paragraph>())
            {
                var length = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Length;
                if (index <= length)
                {
                    foreach (var run in paragraph.Inlines.OfType<Run>())
                    {
                        if (index <= run.Text.Length)
                        {
                            return run.ContentStart.GetPositionAtOffset(index);
                        }

                        index -= run.Text.Length;
                    }

                    return paragraph.ContentEnd;
                }

                index -= length + 2;
            }

            return null;
        }

#pragma warning disable VSTHRD010 // the editor is a WPF control: it is only used on the UI thread
        /// <summary>The colors of the SQL editor of SSMS by kind of <see cref="SnippetHighlighter"/>.</summary>
        private Brush[] Palette()
        {
            return EditorColors.ByKind().Select(c => (Brush)new SolidColorBrush(c)).ToArray();
        }
#pragma warning restore VSTHRD010
    }
}
