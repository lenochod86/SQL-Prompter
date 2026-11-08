using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The definition below the tooltip of an object name (#350): the script in the colors of the suggestion list, scrollable and selectable, with the
    /// Copy and Pin buttons. It shows "Loading..." at once and the script when the background load has finished (the UI thread never waits for the
    /// database); a name without a script (a synonym, encrypted text, no permission) says so. The Quick Info popup of the editor keeps open while
    /// the mouse is over it, so the mouse can enter it to scroll and copy.
    /// </summary>
    internal sealed class HoverDefinitionPanel : StackPanel
    {
        private const double ScriptWidth = 640;
        private const double ScriptHeight = 380;
        private const int MaxLines = 600;

        private readonly DefinitionRef reference;
        private readonly ObjectSummary summary;
        private readonly TextBlock status = new TextBlock { Text = "Loading the definition...", Margin = new Thickness(0, 6, 0, 0), FontStyle = FontStyles.Italic };
        private string script;

        /// <summary>
        /// The panel on a background of its own: the popup of the editor's tooltip is white in a dark theme too, and the colors of the script (and the
        /// text color of the theme) have to stand on the background of the theme.
        /// </summary>
        public static FrameworkElement Create(DefinitionRef reference, ObjectSummary summary)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var border = new Border { Child = new HoverDefinitionPanel(reference, summary), Padding = new Thickness(8, 4, 8, 6), Margin = new Thickness(0, 6, 0, 0) };
            border.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            return border;
        }

        private HoverDefinitionPanel(DefinitionRef reference, ObjectSummary summary)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            this.reference = reference;
            this.summary = summary;

            status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Children.Add(status);
            if (DefinitionPreview.TryGetCached(reference, out var cached) && cached != null)
            {
                Show(cached);
            }
            else
            {
                DefinitionPreview.Request(reference, Show);
            }
        }

        private void Show(string loaded)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            script = loaded;
            if (string.IsNullOrWhiteSpace(loaded))
            {
                status.Text = "No definition available.";
                return;
            }

            Children.Clear();
            var header = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            name.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            if (!string.IsNullOrEmpty(reference.Schema))
            {
                name.Inlines.Add(new System.Windows.Documents.Run(reference.Schema + ".") { Foreground = SqlCompletionPresenter.DimBrush() });
            }

            name.Inlines.Add(new System.Windows.Documents.Run(reference.Name));
            var copy = SqlCompletionPresenter.HeaderButton("Copy", "Copy the whole script", Copy);
            var pin = SqlCompletionPresenter.HeaderButton("Pin", "Keep the definition in a window of its own", Pin);
            Grid.SetColumn(copy, 1);
            Grid.SetColumn(pin, 2);
            header.Children.Add(name);
            header.Children.Add(copy);
            header.Children.Add(pin);
            Children.Add(header);

            var lines = loaded.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n');
            var text = string.Join("\n", lines.Take(MaxLines)) + (lines.Length > MaxLines ? "\n..." : string.Empty);
            var block = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 8, 0) };
            block.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            SqlCompletionPresenter.FillColored(block, text);
            Children.Add(new ScrollViewer
            {
                Content = block,
                MaxWidth = ScriptWidth,
                MaxHeight = ScriptHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
        }

        private void Copy()
        {
            try
            {
                if (!string.IsNullOrEmpty(script))
                {
                    Clipboard.SetText(script);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Copying the definition failed", ex);
            }
        }

        private void Pin()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!string.IsNullOrEmpty(script))
            {
                DefinitionWindow.Open((string.IsNullOrEmpty(reference.Schema) ? string.Empty : reference.Schema + ".") + reference.Name, script, summary, false);
            }
        }
    }
}
