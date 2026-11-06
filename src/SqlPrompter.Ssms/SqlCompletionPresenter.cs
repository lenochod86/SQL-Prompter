using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Our own completion list (research #61). Each row shows the kind icon, the name with the typed text in bold,
    /// a detail (column type) after it and the suffix (alias, schema, "SQL Prompter") on the right.
    /// Only <see cref="SqlCompletionPresenter.VisibleRows"/> rows exist, whatever the number of items, so thousands of items stay cheap.
    /// </summary>
    [Export(typeof(ICompletionPresenterProvider))]
    [Name("SqlPrompter.Presenter")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = "default")]
    internal sealed class SqlCompletionPresenterProvider : ICompletionPresenterProvider
    {
        public CompletionPresenterOptions Options { get; } = new CompletionPresenterOptions(SqlCompletionPresenter.VisibleRows);

        public ICompletionPresenter GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionPresenter((IWpfTextView)textView));
    }

    internal sealed class SqlCompletionPresenter : ICompletionPresenter
    {
        public const int VisibleRows = 12;

        private const double RowHeight = 22;
        private const double GripHeight = 14;
        private const int MinRows = 4;
        private const int MaxRows = 40;
        private const double MinWidth = 300;
        private const double MaxWidth = 1600;

        /// <summary>Icons are drawn for this background so that they get the colors of the current theme.</summary>
        private static Color BackgroundColor
        {
            get
            {
                var c = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
                return Color.FromArgb(c.A, c.R, c.G, c.B);
            }
        }
        private const double MinListWidth = 420;
        private const double MaxListWidth = 1000;
        private const double DescriptionWidth = 380;
        private const double DefinitionWidth = 640;
        private const int DefinitionLines = 28;

        private readonly IWpfTextView view;
        private readonly Popup popup;
        private readonly Popup descriptionPopup;
        private readonly TextBlock descriptionText;
        private readonly TextBlock definitionText;
        private CompletionItem definitionItem;
        private DispatcherTimer definitionTimer;
        private readonly StackPanel rowPanel = new StackPanel();
        private readonly ScrollBar scrollBar;
        private readonly List<Row> rows = new List<Row>();
        private object lastItemList;
        private IReadOnlyList<CompletionItemWithHighlight> items = new CompletionItemWithHighlight[0];
        private int offset;
        private int selected = -1;
        private bool soft;
        private bool updatingScrollBar;
        private Row suggestionRow;
        private bool showSuggestion;
        private bool suggestionSelected;
        private CompletionItem suggestionItem;
        private double listWidth = MinListWidth;
        private int visibleRows = VisibleRows;
        private double widthSetting;
        private double dragRows;
        private double dragWidth;
        private readonly Grid listGrid;

        public SqlCompletionPresenter(IWpfTextView view)
        {
            this.view = view;
            suggestionRow = new Row(this) { IsSuggestion = true };
            suggestionRow.Root.Visibility = Visibility.Collapsed;
            rowPanel.Children.Add(suggestionRow.Root);
            for (var i = 0; i < MaxRows; i++)
            {
                var row = new Row(this);
                rows.Add(row);
                rowPanel.Children.Add(row.Root);
            }

            ReadSizeSettings();
            scrollBar = new ScrollBar { Orientation = Orientation.Vertical, Width = 14, Visibility = Visibility.Collapsed, Focusable = false };
            scrollBar.ValueChanged += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (!updatingScrollBar)
                {
                    offset = (int)Math.Round(scrollBar.Value);
                    Render();
                }
            };

            var grid = new Grid { Width = listWidth, Focusable = false };
            listGrid = grid;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GripHeight) });
            Grid.SetColumn(rowPanel, 0);
            Grid.SetColumn(scrollBar, 1);
            grid.Children.Add(rowPanel);
            grid.Children.Add(scrollBar);
            var grip = CreateGrip();
            Grid.SetRow(grip, 1);
            Grid.SetColumnSpan(grip, 2);
            grid.Children.Add(grip);
            grid.PreviewMouseWheel += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Scroll(e.Delta > 0 ? -3 : 3);
                e.Handled = true;
            };

            popup = CreatePopup(grid);
            PopupWindows.Attach(popup, () => view.VisualElement);
            descriptionText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = DescriptionWidth, Margin = new Thickness(8, 6, 8, 6), HorizontalAlignment = HorizontalAlignment.Left };
            descriptionText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            definitionText = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(8, 0, 8, 6),
                MaxWidth = DefinitionWidth,
                Visibility = Visibility.Collapsed,
            };
            definitionText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            var detailPanel = new StackPanel();
            detailPanel.Children.Add(descriptionText);
            detailPanel.Children.Add(definitionText);
            descriptionPopup = CreatePopup(detailPanel);
            PopupWindows.Attach(descriptionPopup, () => view.VisualElement);
        }

#pragma warning disable CS0067 // never raised yet: no filter buttons
        public event EventHandler<CompletionFilterChangedEventArgs> FiltersChanged;

        public event EventHandler<CompletionClosedEventArgs> CompletionClosed;
#pragma warning restore CS0067

        public event EventHandler<CompletionItemSelectedEventArgs> CompletionItemSelected;

        public event EventHandler<CompletionItemEventArgs> CommitRequested;

        public void Open(IAsyncCompletionSession session, CompletionPresentationViewModel presentation)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ReadSizeSettings();
            Update(session, presentation);
            popup.IsOpen = true;
            descriptionPopup.IsOpen = descriptionText.Text.Length > 0;
        }

        public void Update(IAsyncCompletionSession session, CompletionPresentationViewModel presentation)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            soft = presentation.UseSoftSelection;
            if (!ReferenceEquals(lastItemList, presentation.ItemList))
            {
                lastItemList = presentation.ItemList;
                items = presentation.ItemList.ToList();
                listWidth = widthSetting > 0 ? widthSetting : MeasureWidth(items);
                listGrid.Width = listWidth;
                offset = 0;
                scrollBar.Visibility = items.Count > visibleRows ? Visibility.Visible : Visibility.Collapsed;
                updatingScrollBar = true;
                scrollBar.Minimum = 0;
                scrollBar.Maximum = Math.Max(0, items.Count - visibleRows);
                scrollBar.ViewportSize = visibleRows;
                scrollBar.LargeChange = visibleRows;
                scrollBar.SmallChange = 1;
                updatingScrollBar = false;
            }

            showSuggestion = presentation.DisplaySuggestionItem && presentation.SuggestionItem != null;
            suggestionSelected = showSuggestion && presentation.SelectSuggestionItem;
            selected = suggestionSelected ? -1 : presentation.SelectedItemIndex;
            suggestionItem = presentation.SuggestionItem;
            EnsureSelectedVisible();
            Render();
            Position(presentation);
            if (popup.IsOpen)
            {
                descriptionPopup.IsOpen = descriptionText.Text.Length > 0;
            }
        }

        public void Close()
        {
            definitionTimer?.Stop();
            definitionItem = null;
            popup.IsOpen = false;
            descriptionPopup.IsOpen = false;
        }

        public void Dispose() => Close();

        private static Popup CreatePopup(UIElement content)
        {
            var border = new Border { BorderThickness = new Thickness(1), Child = content, Focusable = false };
            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            return new Popup { Child = border, AllowsTransparency = false, StaysOpen = true, Focusable = false, Placement = PlacementMode.Relative };
        }

        /// <summary>Width that fits the longest row (estimated from the text lengths), within limits.</summary>
        private static double MeasureWidth(IReadOnlyList<CompletionItemWithHighlight> list)
        {
            double widest = 0;
            foreach (var entry in list)
            {
                var item = entry.CompletionItem;
                item.Properties.TryGetProperty(SqlCompletionSource.DetailKey, out string detail);
                var width = 7.2 * (item.DisplayText.Length + (detail?.Length ?? 0) + 1) + 24 + 6.2 * (item.Suffix?.Length ?? 0);
                widest = Math.Max(widest, width);
            }

            return Math.Max(MinListWidth, Math.Min(MaxListWidth, widest + 70));
        }

        /// <summary>Reads the size the user chose (rows, width; 0 = follows the items) so that a change in the settings window applies to the next list.</summary>
        private void ReadSizeSettings()
        {
            var behavior = SettingsService.Current.Behavior;
            visibleRows = Math.Max(MinRows, Math.Min(MaxRows, behavior.ListRows));
            widthSetting = behavior.ListWidth > 0 ? Math.Max(MinWidth, Math.Min(MaxWidth, behavior.ListWidth)) : 0;
            lastItemList = null;
        }

        /// <summary>The grip in the bottom-right corner: dragging changes the number of rows and the width; the size is kept in the settings.</summary>
        private FrameworkElement CreateGrip()
        {
            var thumb = new System.Windows.Controls.Primitives.Thumb
            {
                Cursor = System.Windows.Input.Cursors.SizeNWSE,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                Width = 16,
                Height = GripHeight,
                ToolTip = "Drag to resize the list",
                Template = GripTemplate(),
            };
            thumb.DragStarted += (s, e) =>
            {
                dragRows = visibleRows;
                dragWidth = listWidth;
            };
            thumb.DragDelta += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                dragRows += e.VerticalChange / RowHeight;
                dragWidth += e.HorizontalChange;
                visibleRows = Math.Max(MinRows, Math.Min(MaxRows, (int)Math.Round(dragRows)));
                widthSetting = Math.Max(MinWidth, Math.Min(MaxWidth, dragWidth));
                listWidth = widthSetting;
                listGrid.Width = listWidth;
                ApplyRows();
            };
            thumb.DragCompleted += (s, e) =>
            {
                var settings = SettingsService.Current.Clone();
                settings.Behavior.ListRows = visibleRows;
                settings.Behavior.ListWidth = (int)Math.Round(widthSetting);
                SettingsService.Update(settings);
                // The editor keeps the focus: the thumb is not focusable and the popup is not activated.
            };
            return thumb;
        }

        private static ControlTemplate GripTemplate()
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var triangle = new FrameworkElementFactory(typeof(System.Windows.Shapes.Polygon));
            triangle.SetValue(System.Windows.Shapes.Polygon.PointsProperty, new PointCollection { new Point(14, 3), new Point(14, 13), new Point(4, 13) });
            triangle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, VsBrushes.ToolWindowBorderKey);
            root.AppendChild(triangle);
            return new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = root };
        }

        /// <summary>The number of rows changed while the list is open: scrollbar, offset and the rows shown follow.</summary>
        private void ApplyRows()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            scrollBar.Visibility = items.Count > visibleRows ? Visibility.Visible : Visibility.Collapsed;
            updatingScrollBar = true;
            scrollBar.Maximum = Math.Max(0, items.Count - visibleRows);
            scrollBar.ViewportSize = visibleRows;
            scrollBar.LargeChange = visibleRows;
            updatingScrollBar = false;
            EnsureSelectedVisible();
            Render();
            descriptionPopup.HorizontalOffset = popup.HorizontalOffset + listWidth + 4;
        }
        private void Scroll(int delta)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            offset = Math.Max(0, Math.Min(Math.Max(0, items.Count - visibleRows), offset + delta));
            Render();
        }

        private void EnsureSelectedVisible()
        {
            if (selected >= 0)
            {
                if (selected < offset)
                {
                    offset = selected;
                }
                else if (selected >= offset + visibleRows)
                {
                    offset = selected - visibleRows + 1;
                }
            }

            offset = Math.Max(0, Math.Min(Math.Max(0, items.Count - visibleRows), offset));
        }

        private void Render()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (showSuggestion)
            {
                suggestionRow.BindSuggestion(new CompletionItemWithHighlight(suggestionItem), suggestionSelected);
            }
            else
            {
                suggestionRow.Root.Visibility = Visibility.Collapsed;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var index = offset + i;
                if (i < visibleRows && index < items.Count)
                {
                    rows[i].Bind(items[index], index == selected, soft);
                }
                else
                {
                    rows[i].Root.Visibility = Visibility.Collapsed;
                }
            }

            updatingScrollBar = true;
            scrollBar.Value = offset;
            updatingScrollBar = false;

            string description = null;
            if (selected >= 0 && selected < items.Count)
            {
                items[selected].CompletionItem.Properties.TryGetProperty(SqlCompletionSource.DescriptionPropertyKey, out description);
            }

            descriptionText.Text = description ?? string.Empty;
            UpdateDefinition(selected >= 0 && selected < items.Count ? items[selected].CompletionItem : null);
        }

        /// <summary>
        /// Shows the script of the selected object under its description: at once when it was loaded lately, otherwise after a short
        /// pause (so that moving through the list does not ask the database for every item) and in the background.
        /// </summary>
        private void UpdateDefinition(CompletionItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ReferenceEquals(item, definitionItem))
            {
                return;
            }

            definitionItem = item;
            definitionTimer?.Stop();
            ShowDefinition(null);
            if (item == null || !item.Properties.TryGetProperty(SqlCompletionSource.DefinitionKey, out DefinitionRef reference))
            {
                return;
            }

            if (DefinitionPreview.TryGetCached(reference, out var cached))
            {
                ShowDefinition(cached);
                return;
            }

            definitionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            definitionTimer.Tick += (s, e) =>
            {
                definitionTimer.Stop();
                if (ReferenceEquals(definitionItem, item))
                {
                    DefinitionPreview.Request(reference, script =>
                    {
                        if (ReferenceEquals(definitionItem, item))
                        {
                            ShowDefinition(script);
                        }
                    });
                }
            };
            definitionTimer.Start();
        }

        private void ShowDefinition(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                definitionText.Text = string.Empty;
                definitionText.Visibility = Visibility.Collapsed;
                return;
            }

            var lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n');
            var shown = lines.Take(DefinitionLines).Select(l => l.Length > 110 ? l.Substring(0, 110) + "..." : l);
            definitionText.Text = string.Join("\n", shown) + (lines.Length > DefinitionLines ? "\n..." : string.Empty);
            definitionText.Visibility = Visibility.Visible;
            descriptionPopup.IsOpen = popup.IsOpen;
        }

        private void Position(CompletionPresentationViewModel presentation)
        {
            try
            {
                var span = presentation.ApplicableToSpan.GetSpan(view.TextSnapshot);
                var line = view.GetTextViewLineContainingBufferPosition(span.Start);
                var bounds = line.GetCharacterBounds(span.Start);
                var height = (Math.Min(items.Count, visibleRows) + (showSuggestion ? 1 : 0)) * RowHeight + GripHeight + 2;
                var left = bounds.Left - view.ViewportLeft;
                var below = bounds.Bottom - view.ViewportTop + 2;
                var above = bounds.Top - view.ViewportTop - height - 2;

                // Above the caret when the list does not fit below and there is room above.
                var top = below + height > view.ViewportHeight && above >= 0 ? above : below;
                popup.PlacementTarget = view.VisualElement;
                popup.HorizontalOffset = left;
                popup.VerticalOffset = top;
                descriptionPopup.PlacementTarget = view.VisualElement;
                descriptionPopup.HorizontalOffset = left + listWidth + 4;
                descriptionPopup.VerticalOffset = top;
            }
            catch (Exception ex)
            {
                Log.Error("Positioning the presenter failed", ex);
            }
        }

        private void OnRowClicked(Row row, int clickCount)
        {
            var item = row.Item?.CompletionItem;
            var isSuggestion = row.IsSuggestion;
            if (item == null)
            {
                return;
            }

            if (clickCount >= 2)
            {
                CommitRequested?.Invoke(this, new CompletionItemEventArgs(item));
            }
            else
            {
                CompletionItemSelected?.Invoke(this, new CompletionItemSelectedEventArgs(item, suggestionItemSelected: isSuggestion));
            }
        }

        /// <summary>One reusable row of the list.</summary>
        private sealed class Row
        {
            private readonly CrispImage icon;
            private readonly TextBlock name;
            private readonly CrispImage keyIcon;
            private readonly InlineUIContainer keyContainer;
            private readonly TextBlock suffix;

            public Row(SqlCompletionPresenter owner)
            {
                Root = new Grid { Height = RowHeight, Background = Brushes.Transparent, Focusable = false };
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                icon = new CrispImage { Width = 16, Height = 16, Margin = new Thickness(6, 0, 6, 0) };
                Grid.SetColumn(icon, 0);
                Root.Children.Add(icon);

                name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                name.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                Grid.SetColumn(name, 1);
                Root.Children.Add(name);

                keyIcon = new CrispImage { Width = 16, Height = 16, Margin = new Thickness(4, 0, 0, 0) };
                keyContainer = new InlineUIContainer(keyIcon) { BaselineAlignment = BaselineAlignment.Center };
                ImageThemingUtilities.SetImageBackgroundColor(icon, BackgroundColor);
                ImageThemingUtilities.SetImageBackgroundColor(keyIcon, BackgroundColor);

                suffix = new TextBlock { Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, FontSize = 11 };
                suffix.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                Grid.SetColumn(suffix, 3);
                Root.Children.Add(suffix);

                Root.MouseLeftButtonDown += (s, e) =>
                {
                    owner.OnRowClicked(this, e.ClickCount);
                    e.Handled = true;
                };
            }

            public Grid Root { get; }

            public bool IsSuggestion { get; set; }

            public CompletionItemWithHighlight? Item { get; private set; }

            /// <summary>The "suggestion item": what the user typed, shown as the first row; Enter keeps it instead of inserting a suggestion.</summary>
            public void BindSuggestion(CompletionItemWithHighlight entry, bool isSelected)
            {
                Item = entry;
                Root.Visibility = Visibility.Visible;
                icon.Visibility = Visibility.Hidden;
                name.Inlines.Clear();
                name.Inlines.Add(new Run(entry.CompletionItem.DisplayText) { FontStyle = FontStyles.Italic });
                suffix.Text = string.Empty;
                SetTextColor(isSelected);
                if (isSelected)
                {
                    Root.SetResourceReference(Panel.BackgroundProperty, VsBrushes.HighlightKey);
                }
                else
                {
                    Root.ClearValue(Panel.BackgroundProperty);
                    Root.Background = Brushes.Transparent;
                }
            }

            public void Bind(CompletionItemWithHighlight entry, bool isSelected, bool isSoft)
            {
                Item = entry;
                var item = entry.CompletionItem;
                Root.Visibility = Visibility.Visible;
                icon.Visibility = Visibility.Visible;
                icon.Moniker = new ImageMoniker { Guid = item.Icon.ImageId.Guid, Id = item.Icon.ImageId.Id };

                name.Inlines.Clear();
                AddHighlighted(name, item.DisplayText, entry.HighlightedSpans);
                if (item.Properties.TryGetProperty(SqlCompletionSource.DetailKey, out string detail))
                {
                    var run = new Run(" " + detail) { FontSize = 11 };
                    run.SetResourceReference(TextElement.ForegroundProperty, isSelected && !isSoft ? VsBrushes.HighlightTextKey : VsBrushes.ControlLinkTextKey);
                    name.Inlines.Add(run);
                }

                if (item.Properties.TryGetProperty(SqlCompletionSource.KeyKey, out SqlPrompter.Core.Completion.KeyKind key))
                {
                    keyIcon.Moniker = key == SqlPrompter.Core.Completion.KeyKind.Primary ? KnownMonikers.Key : KnownMonikers.ForeignKey;
                    name.Inlines.Add(keyContainer);
                }
                suffix.Text = item.Suffix;
                SetTextColor(isSelected && !isSoft);
                if (isSelected)
                {
                    Root.SetResourceReference(Panel.BackgroundProperty, isSoft ? VsBrushes.ToolWindowBorderKey : VsBrushes.HighlightKey);
                }
                else
                {
                    Root.ClearValue(Panel.BackgroundProperty);
                    Root.Background = Brushes.Transparent;
                }
            }

            /// <summary>On the highlighted (hard selected) row the text takes the highlight text color; rows are reused, so it is always set.</summary>
            private void SetTextColor(bool highlighted)
            {
                var key = highlighted ? VsBrushes.HighlightTextKey : VsBrushes.ToolWindowTextKey;
                name.SetResourceReference(TextBlock.ForegroundProperty, key);
                suffix.SetResourceReference(TextBlock.ForegroundProperty, key);
            }

            private static void AddHighlighted(TextBlock block, string text, System.Collections.Immutable.ImmutableArray<Microsoft.VisualStudio.Text.Span> spans)
            {
                var position = 0;
                foreach (var span in spans.OrderBy(s => s.Start))
                {
                    if (span.Start < position || span.End > text.Length)
                    {
                        continue;
                    }

                    if (span.Start > position)
                    {
                        block.Inlines.Add(new Run(text.Substring(position, span.Start - position)));
                    }

                    block.Inlines.Add(new Run(text.Substring(span.Start, span.Length)) { FontWeight = FontWeights.Bold });
                    position = span.End;
                }

                if (position < text.Length)
                {
                    block.Inlines.Add(new Run(text.Substring(position)));
                }
            }
        }
    }
}
