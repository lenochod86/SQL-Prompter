using System;
using Microsoft.VisualStudio.Text;
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

    /// <summary>
    /// The status bar of the list when SQL Prompter runs the list itself (<see cref="InvokedList"/>): the tabs Column Picker and All Suggestions, in the
    /// picker the Select All button and the table order check box, and a hint. The editor's own session has no footer.
    /// </summary>
    internal sealed class ListFooter
    {
        /// <summary>True while the column picker is shown, false for the normal list.</summary>
        public bool PickerMode { get; set; }

        /// <summary>The tab Column Picker is available (the caret is where columns are picked).</summary>
        public bool PickerAvailable { get; set; }

        public string Hint { get; set; }

        public bool TableOrder { get; set; }

        public Action ShowPicker { get; set; }

        public Action ShowAll { get; set; }

        public Action SelectAll { get; set; }

        public Action HideHint { get; set; }

        public Action<bool> TableOrderChanged { get; set; }
    }

    internal sealed class SqlCompletionPresenter : ICompletionPresenter
    {
        public const int VisibleRows = 12;

        private const double RowHeight = 22;
        private const double GripHeight = 14;
        private const double FooterHeight = 24;
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
        private readonly Grid definitionHeader = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 4, 2) };
        private readonly TextBlock definitionName = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        private DefinitionRef definitionReference;
        private SqlPrompter.Core.Model.ObjectSummary definitionSummary;
        private bool showingSummary;
        private readonly StackPanel definitionTabs = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 4, 4) };
        private readonly Grid summaryGrid = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 8, 6) };
        private Button scriptTab;
        private Button summaryTab;
        private string definitionScript;
        private CompletionItem definitionItem;
        private CompletionItem dbDescriptionItem;
        private string dbDescription;
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
        private readonly StackPanel footerPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4, 1, 20, 1), Visibility = Visibility.Collapsed };
        private ListFooter footer;

        /// <summary>The items shown with a check box (the column picker); null for a normal list.</summary>
        public HashSet<CompletionItem> CheckedItems { get; set; }

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
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = GripHeight });
            Grid.SetRow(footerPanel, 1);
            grid.Children.Add(footerPanel);
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
            definitionName.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            definitionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            definitionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            definitionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var copyButton = HeaderButton("Copy", "Copy the whole script", CopyDefinition);
            var pinButton = HeaderButton("Pin", "Keep the definition in a window of its own", PinDefinition);
            Grid.SetColumn(copyButton, 1);
            Grid.SetColumn(pinButton, 2);
            definitionHeader.Children.Add(definitionName);
            definitionHeader.Children.Add(copyButton);
            definitionHeader.Children.Add(pinButton);
            var detailPanel = new StackPanel();
            detailPanel.Children.Add(descriptionText);
            scriptTab = TabButton("Script", () => SelectTab(false));
            summaryTab = TabButton("Summary", () => SelectTab(true));
            definitionTabs.Children.Add(scriptTab);
            definitionTabs.Children.Add(summaryTab);
            detailPanel.Children.Add(definitionHeader);
            detailPanel.Children.Add(definitionTabs);
            detailPanel.Children.Add(definitionText);
            detailPanel.Children.Add(summaryGrid);
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
            CheckedItems = null;
            SetFooter(SessionFooter(session));
            Present(presentation.ItemList, presentation.ItemList, presentation.SelectedItemIndex, presentation.UseSoftSelection,
                presentation.DisplaySuggestionItem && presentation.SuggestionItem != null, presentation.SuggestionItem, presentation.SelectSuggestionItem, presentation.ApplicableToSpan);
        }

        /// <summary>
        /// Shows the list in the popup (opens it when it is closed). Used by the editor's completion session (through <see cref="Update"/>) and by the list
        /// that SQL Prompter runs itself (<see cref="InvokedList"/>); the popup does not care who drives it. <paramref name="listKey"/> identifies the
        /// list: while it stays the same object only the selection moves, a new one resets the scroll position and the width.
        /// </summary>
        public void Present(IEnumerable<CompletionItemWithHighlight> list, object listKey, int selectedIndex, bool useSoftSelection, bool displaySuggestionItem,
            CompletionItem suggestion, bool suggestionIsSelected, ITrackingSpan applicableToSpan)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            soft = useSoftSelection;
            if (!ReferenceEquals(lastItemList, listKey))
            {
                lastItemList = listKey;
                items = list.ToList();
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

            showSuggestion = displaySuggestionItem && suggestion != null;
            suggestionSelected = showSuggestion && suggestionIsSelected;
            selected = suggestionSelected ? -1 : selectedIndex;
            suggestionItem = suggestion;
            EnsureSelectedVisible();
            Render();
            Position(applicableToSpan);
            if (popup.IsOpen)
            {
                descriptionPopup.IsOpen = descriptionText.Text.Length > 0;
            }
        }

        /// <summary>The status bar of the editor's own list: where columns are picked, the tabs and the hint that lead to the column picker.</summary>
        private ListFooter SessionFooter(IAsyncCompletionSession session)
        {
            if (!session.Properties.TryGetProperty(SqlCompletionSource.PickerAvailableKey, out bool available) || !available)
            {
                return null;
            }

            var shownHint = SettingsService.Current.Behavior.ShowColumnPickerHint;
            return new ListFooter
            {
                PickerAvailable = true,
                Hint = shownHint ? "Ctrl+Left: column picker" : null,
                HideHint = shownHint ? new Action(() =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    var settings = SettingsService.Current.Clone();
                    settings.Behavior.ShowColumnPickerHint = false;
                    SettingsService.Update(settings);
                    SetFooter(SessionFooter(session));
                }) : null,
                ShowPicker = () =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (InvokedList.CanPick(view))
                    {
                        session.Dismiss();
                        InvokedList.Open(view, picker: true);
                    }
                },
                ShowAll = () => { },
            };
        }

        /// <summary>Sets what the status bar shows (null for none) and draws it.</summary>
        public void SetFooter(ListFooter value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            footer = value;
            footerPanel.Children.Clear();
            footerPanel.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible;
            if (value == null)
            {
                return;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            footerPanel.Children.Add(row);
            if (value.PickerAvailable)
            {
                row.Children.Add(FooterButton("Column Picker", value.PickerMode, () => value.ShowPicker?.Invoke()));
                row.Children.Add(FooterButton("All Suggestions", !value.PickerMode, () => value.ShowAll?.Invoke()));
            }

            if (value.PickerMode)
            {
                row.Children.Add(FooterButton("Select All", false, () => value.SelectAll?.Invoke()));
                var order = new CheckBox { Content = "List in table order", IsChecked = value.TableOrder, Focusable = false, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
                order.SetResourceReference(Control.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                order.Click += (s, e) => value.TableOrderChanged?.Invoke(order.IsChecked == true);
                row.Children.Add(order);
            }

            if (!string.IsNullOrEmpty(value.Hint))
            {
                var hintRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                var hint = new TextBlock { Text = value.Hint, FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
                hint.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                hintRow.Children.Add(hint);
                if (value.HideHint != null)
                {
                    hintRow.Children.Add(FooterButton("x", false, () => value.HideHint()));
                }

                footerPanel.Children.Add(hintRow);
            }
        }

        private static Button FooterButton(string text, bool active, Action click)
        {
            var button = new Button { Content = text, Focusable = false, FontSize = 11, Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(0, 0, 4, 0), FontWeight = active ? FontWeights.Bold : FontWeights.Normal };
            button.Click += (s, e) => click();
            return button;
        }

        /// <summary>True while the popup is shown.</summary>
        public bool IsOpen => popup.IsOpen;

        /// <summary>Opens the popup for a list given with <see cref="Present"/> (the editor's session opens it through <see cref="Open"/>).</summary>
        public void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ReadSizeSettings();
            popup.IsOpen = true;
            descriptionPopup.IsOpen = descriptionText.Text.Length > 0;
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
            if (dbDescription != null && selected >= 0 && selected < items.Count && ReferenceEquals(items[selected].CompletionItem, dbDescriptionItem))
            {
                descriptionText.Text += (descriptionText.Text.Length > 0 ? "\n\n" : string.Empty) + dbDescription;
            }

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

            definitionReference = reference;
            RequestDescription(item, reference);

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

        /// <summary>The descriptions of the database (MS_Description) are added under the description of the item when they arrive (#206).</summary>
        private void RequestDescription(CompletionItem item, DefinitionRef reference)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dbDescription = null;
            dbDescriptionItem = item;
            if (!SettingsService.Current.Behavior.ShowDbDescriptions)
            {
                return;
            }

            DescriptionPreview.Request(reference, text =>
            {
                if (string.IsNullOrEmpty(text) || !ReferenceEquals(definitionItem, item) || !ReferenceEquals(dbDescriptionItem, item))
                {
                    return;
                }

                dbDescription = text;
                descriptionText.Text += (descriptionText.Text.Length > 0 ? "\n\n" : string.Empty) + text;
                descriptionPopup.IsOpen = popup.IsOpen;
            });
        }

        private void ShowDefinition(string script)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            definitionScript = script;
            if (string.IsNullOrWhiteSpace(script))
            {
                definitionText.Inlines.Clear();
                definitionText.Visibility = Visibility.Collapsed;
                definitionHeader.Visibility = Visibility.Collapsed;
                definitionTabs.Visibility = Visibility.Collapsed;
                summaryGrid.Visibility = Visibility.Collapsed;
                definitionSummary = null;
                return;
            }

            var lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n');
            var shown = lines.Take(DefinitionLines).Select(l => l.Length > 110 ? l.Substring(0, 110) + "..." : l);
            var text = string.Join("\n", shown) + (lines.Length > DefinitionLines ? "\n..." : string.Empty);
            FillColored(definitionText, text);
            definitionName.Inlines.Clear();
            if (definitionReference != null)
            {
                if (!string.IsNullOrEmpty(definitionReference.Schema))
                {
                    definitionName.Inlines.Add(new System.Windows.Documents.Run(definitionReference.Schema + ".") { Foreground = DimBrush() });
                }

                definitionName.Inlines.Add(new System.Windows.Documents.Run(definitionReference.Name));
            }

            definitionSummary = BuildSummary();
            definitionTabs.Visibility = definitionSummary != null ? Visibility.Visible : Visibility.Collapsed;
            definitionHeader.Visibility = Visibility.Visible;
            SelectTab(false);
            descriptionPopup.IsOpen = popup.IsOpen;
        }

        /// <summary>The columns or parameters of the object from the metadata that is loaded (null when there are none).</summary>
        private SqlPrompter.Core.Model.ObjectSummary BuildSummary()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (definitionReference == null)
                {
                    return null;
                }

                var cache = MetadataService.Current?.CacheForActiveWindow(out _, definitionReference.Database);
                return SqlPrompter.Core.Model.ObjectSummary.For(cache?.Current, definitionReference.Schema, definitionReference.Name);
            }
            catch (Exception ex)
            {
                Log.Error("Building the summary failed", ex);
                return null;
            }
        }

        private void SelectTab(bool summary)
        {
            showingSummary = summary && definitionSummary != null;
            definitionText.Visibility = showingSummary ? Visibility.Collapsed : (string.IsNullOrWhiteSpace(definitionScript) ? Visibility.Collapsed : Visibility.Visible);
            summaryGrid.Visibility = showingSummary ? Visibility.Visible : Visibility.Collapsed;
            scriptTab.FontWeight = showingSummary ? FontWeights.Normal : FontWeights.Bold;
            summaryTab.FontWeight = showingSummary ? FontWeights.Bold : FontWeights.Normal;
            if (showingSummary)
            {
                FillSummary(summaryGrid, definitionSummary);
            }
        }

        /// <summary>The summary as a small table: a header row, then a row per column or parameter.</summary>
        private static void FillSummary(Grid grid, SqlPrompter.Core.Model.ObjectSummary summary)
        {
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            for (var c = 0; c < summary.Headers.Count; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto, MinWidth = c == 0 ? 160 : 0 });
            }

            for (var r = -1; r < summary.Rows.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (var c = 0; c < summary.Headers.Count; c++)
                {
                    var cell = new TextBlock
                    {
                        Text = r < 0 ? summary.Headers[c] : summary.Rows[r][c],
                        Margin = new Thickness(4, 2, 16, 2),
                        FontWeight = r < 0 ? FontWeights.SemiBold : FontWeights.Normal,
                        FontSize = 12,
                    };
                    cell.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                    Grid.SetRow(cell, r + 1);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                }
            }
        }

        private static Button TabButton(string text, Action click)
        {
            var button = new Button { Content = text, Focusable = false, Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(0, 0, 4, 0), FontSize = 11 };
            button.Click += (s, e) => click();
            return button;
        }

        private static Brush DimBrush()
        {
            var dark = BackgroundColor.R + BackgroundColor.G + BackgroundColor.B < 384;
            var brush = new SolidColorBrush(dark ? Color.FromRgb(0xB0, 0xB0, 0xB0) : Color.FromRgb(0x70, 0x70, 0x70));
            brush.Freeze();
            return brush;
        }

        /// <summary>The script colored as SQL (keywords, strings, comments, variables) for the light or the dark theme.</summary>
        private static void FillColored(TextBlock block, string text)
        {
            var dark = BackgroundColor.R + BackgroundColor.G + BackgroundColor.B < 384;
            var colors = new Color?[]
            {
                null,
                dark ? Color.FromRgb(0x56, 0x9C, 0xD6) : Color.FromRgb(0x00, 0x00, 0xFF),
                dark ? Color.FromRgb(0xD6, 0x9D, 0x85) : Color.FromRgb(0xA3, 0x15, 0x15),
                dark ? Color.FromRgb(0x57, 0xA6, 0x4A) : Color.FromRgb(0x00, 0x80, 0x00),
                dark ? Color.FromRgb(0x9C, 0xDC, 0xFE) : Color.FromRgb(0x1F, 0x37, 0x7F),
                null,
            };
            var kinds = SqlPrompter.Core.Snippets.SnippetHighlighter.Classify(text);
            block.Inlines.Clear();
            var i = 0;
            while (i < text.Length)
            {
                var kind = Math.Min(kinds[i], colors.Length - 1);
                var j = i;
                while (j < text.Length && Math.Min(kinds[j], colors.Length - 1) == kind)
                {
                    j++;
                }

                var run = new System.Windows.Documents.Run(text.Substring(i, j - i));
                if (colors[kind].HasValue)
                {
                    run.Foreground = new SolidColorBrush(colors[kind].Value);
                }

                block.Inlines.Add(run);
                i = j;
            }
        }

        private static Button HeaderButton(string text, string tip, Action click)
        {
            var button = new Button { Content = text, ToolTip = tip, Focusable = false, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0), FontSize = 11 };
            button.Click += (s, e) => click();
            return button;
        }

        private void CopyDefinition()
        {
            try
            {
                if (showingSummary && definitionSummary != null)
                {
                    Clipboard.SetText(definitionSummary.ToText());
                }
                else if (!string.IsNullOrEmpty(definitionScript))
                {
                    Clipboard.SetText(definitionScript);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Copying the definition failed", ex);
            }
        }

        /// <summary>Opens the definition in a window that stays when the list closes.</summary>
        private void PinDefinition()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(definitionScript))
            {
                return;
            }

            var title = definitionReference == null ? "Definition" : (string.IsNullOrEmpty(definitionReference.Schema) ? string.Empty : definitionReference.Schema + ".") + definitionReference.Name;
            DefinitionWindow.Open(title, definitionScript, definitionSummary, showingSummary);
        }

        private void Position(ITrackingSpan applicableToSpan)
        {
            try
            {
                var span = applicableToSpan.GetSpan(view.TextSnapshot);
                var line = view.GetTextViewLineContainingBufferPosition(span.Start);
                var bounds = line.GetCharacterBounds(span.Start);
                var height = (Math.Min(items.Count, visibleRows) + (showSuggestion ? 1 : 0)) * RowHeight + (footer == null ? GripHeight : FooterHeight + (string.IsNullOrEmpty(footer.Hint) ? 0 : 20)) + 2;
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
            private readonly SqlCompletionPresenter owner;
            private readonly TextBlock check = new TextBlock { Width = 16, Height = 16, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 14, Visibility = Visibility.Collapsed };

            public Row(SqlCompletionPresenter owner)
            {
                this.owner = owner;
                Root = new Grid { Height = RowHeight, Background = Brushes.Transparent, Focusable = false };
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                icon = new CrispImage { Width = 16, Height = 16, Margin = new Thickness(6, 0, 6, 0) };
                Grid.SetColumn(icon, 0);
                Root.Children.Add(icon);
                Grid.SetColumn(check, 0);
                Root.Children.Add(check);

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
                check.Visibility = Visibility.Collapsed;
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
                if (item.Properties.TryGetProperty(SqlCompletionSource.PickerHeaderKey, out string heading))
                {
                    // A heading of the column picker: the source of the columns that follow.
                    icon.Visibility = Visibility.Collapsed;
                    check.Visibility = Visibility.Collapsed;
                    name.Inlines.Clear();
                    name.Inlines.Add(new Run(heading) { FontWeight = FontWeights.Bold });
                    suffix.Text = item.Suffix;
                    SetTextColor(false);
                    Root.ClearValue(Panel.BackgroundProperty);
                    Root.Background = Brushes.Transparent;
                    return;
                }

                var checks = owner.CheckedItems != null;
                icon.Visibility = checks ? Visibility.Collapsed : Visibility.Visible;
                check.Visibility = checks ? Visibility.Visible : Visibility.Collapsed;
                check.Text = checks && owner.CheckedItems.Contains(item) ? "[x]" : "[ ]";
                check.FontSize = 11;
                check.Width = 22;
                if (!checks)
                {
                    icon.Moniker = new ImageMoniker { Guid = item.Icon.ImageId.Guid, Id = item.Icon.ImageId.Id };
                }

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
                check.SetResourceReference(TextBlock.ForegroundProperty, key);
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
