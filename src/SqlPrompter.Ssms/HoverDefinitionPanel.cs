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
        private const double MinScriptWidth = 320;
        private const double MinScriptHeight = 120;
        private const int MaxLines = 600;

        // The size the user dragged the script area to, kept for the next hover windows of the session (#421).
        private static Size? chosenSize;

        private readonly DefinitionRef reference;
        private readonly ObjectSummary summary;
        private readonly TextBlock status = new TextBlock { Text = "Loading the definition...", Margin = new Thickness(0, 6, 0, 0), FontStyle = FontStyles.Italic };
        private string script;
        private ScrollViewer scroller;

        /// <summary>
        /// The panel on a background of its own: the popup of the editor's tooltip is white in a dark theme too, and the colors of the script (and the
        /// text color of the theme) have to stand on the background of the theme.
        /// </summary>
        public static FrameworkElement Create(DefinitionRef reference, ObjectSummary summary)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var border = new Border { Child = new HoverDefinitionPanel(reference, summary), Padding = new Thickness(8, 4, 8, 6), Margin = new Thickness(0, 6, 0, 0) };
            border.Background = new SolidColorBrush(EditorColors.Background());
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
            scroller = new ScrollViewer
            {
                Content = block,
                MaxWidth = ScriptWidth,
                MaxHeight = ScriptHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            if (chosenSize is Size size)
            {
                ApplySize(size.Width, size.Height);
            }

            Children.Add(scroller);
            Children.Add(CreateGrip());
        }

        /// <summary>The script area gets an explicit size (the limits of the default are lifted), within the minimum and the screen.</summary>
        private void ApplySize(double width, double height)
        {
            var area = SystemParameters.WorkArea;
            width = Math.Max(MinScriptWidth, Math.Min(width, area.Width - 120));
            height = Math.Max(MinScriptHeight, Math.Min(height, area.Height - 260));
            scroller.MaxWidth = double.PositiveInfinity;
            scroller.MaxHeight = double.PositiveInfinity;
            scroller.Width = width;
            scroller.Height = height;
            chosenSize = new Size(width, height);
        }

        /// <summary>
        /// The resize grip below the script, right-aligned. A thumb captures the mouse while it is dragged, so the popup of the tooltip does not close
        /// when the mouse leaves its original area.
        /// </summary>
        private FrameworkElement CreateGrip()
        {
            var grip = new System.Windows.Controls.Primitives.Thumb
            {
                Width = 14,
                Height = 14,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = System.Windows.Input.Cursors.SizeNWSE,
                ToolTip = "Drag to resize",
                Template = GripTemplate(),
            };
            grip.DragDelta += (s, e) =>
            {
                ApplySize(scroller.ActualWidth + e.HorizontalChange, scroller.ActualHeight + e.VerticalChange);
                e.Handled = true;
            };
            return grip;
        }

        private static ControlTemplate GripTemplate()
        {
            // Three diagonal lines in the text color of the theme (a hard-coded color would not follow the theme).
            var canvas = new FrameworkElementFactory(typeof(Canvas));
            canvas.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            for (var i = 0; i < 3; i++)
            {
                var line = new FrameworkElementFactory(typeof(System.Windows.Shapes.Line));
                line.SetValue(System.Windows.Shapes.Line.X1Property, 13.0 - (i * 4));
                line.SetValue(System.Windows.Shapes.Line.Y1Property, 13.0);
                line.SetValue(System.Windows.Shapes.Line.X2Property, 13.0);
                line.SetValue(System.Windows.Shapes.Line.Y2Property, 13.0 - (i * 4));
                line.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.0);
                line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, EnvironmentColors.ToolWindowTextBrushKey);
                canvas.AppendChild(line);
            }

            return new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = canvas };
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
                DefinitionWindow.Open((string.IsNullOrEmpty(reference.Schema) ? string.Empty : reference.Schema + ".") + reference.Name, script, summary, false,
                    chosenSize is Size size ? new Size(size.Width + 60, size.Height + 170) : (Size?)null);
            }
        }
    }
}
