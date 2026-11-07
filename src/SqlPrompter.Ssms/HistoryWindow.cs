using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SqlPrompter.Core.History;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The query history like the snippet manager: a filter box, the list of runs (newest first) and the text of the chosen run.
    /// Enter or a double click chooses the run; the caller opens it in a new tab on the same server and database.
    /// </summary>
    internal sealed class HistoryWindow : Window
    {
        private const int MaxShown = 2000;

        private readonly TextBox filter = SnippetUi.Box(false);
        private readonly ListView list = new ListView();
        private readonly SnippetBodyEditor preview = new SnippetBodyEditor { Margin = new Thickness(0), Padding = new Thickness(4), BorderThickness = new Thickness(1) };
        private readonly TextBlock status = new TextBlock { Margin = new Thickness(0, 6, 0, 0) };
        private HistoryView view = HistoryView.All;

        public HistoryWindow()
        {
            Title = "SQL Prompter query history";
            Width = 1100;
            Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SnippetUi.Theme(this);
            SnippetUi.Theme(list);
            SnippetUi.Theme(preview);
            preview.SetResourceReference(Control.BorderBrushProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBorderKey);
            preview.IsReadOnly = true;
            preview.VerticalAlignment = VerticalAlignment.Stretch;
            status.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.WindowTextKey);

            // Two lines per run (#286): tab name and time of the run on the left, server and database on the right.
            list.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(RowTemplate);
            list.ItemContainerStyle = (Style)System.Windows.Markup.XamlReader.Parse(RowStyle);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.SelectionChanged += (s, e) => ShowPreview();
            list.MouseDoubleClick += (s, e) => Choose();
            list.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Choose();
                    e.Handled = true;
                }
            };

            filter.TextChanged += (s, e) => Fill();
            filter.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Choose();
                    e.Handled = true;
                }
                else if (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.PageDown || e.Key == Key.PageUp)
                {
                    Move(e.Key == Key.Down ? 1 : e.Key == Key.Up ? -1 : e.Key == Key.PageDown ? 10 : -10);
                    e.Handled = true;
                }
            };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new DockPanel { LastChildFill = true };
            var views = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(views, Dock.Right);
            views.Children.Add(ViewButton("\uE72C", "All runs", HistoryView.All, true));
            views.Children.Add(ViewButton("\uE718", "Pinned runs", HistoryView.Pinned, false));
            views.Children.Add(ViewButton("\uE838", "Open tabs", HistoryView.Open, false));
            views.Children.Add(ViewButton("\uE8B7", "Closed tabs", HistoryView.Closed, false));
            heading.Children.Add(views);
            var hint = SnippetUi.Label("Type words to filter (text, tab, server, database). Enter or a double click opens the query in a new tab; Esc closes.");
            hint.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(hint);
            root.Children.Add(heading);
            Grid.SetRow(filter, 1);
            root.Children.Add(filter);

            var middle = new Grid();
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star), MinWidth = 150 });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
            middle.Children.Add(list);
            var splitter = new GridSplitter
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = Cursors.SizeWE,
            };
            Grid.SetColumn(splitter, 1);
            middle.Children.Add(splitter);
            Grid.SetColumn(preview, 2);
            middle.Children.Add(preview);
            Grid.SetRow(middle, 2);
            root.Children.Add(middle);

            var bottom = new StackPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(SnippetUi.Button("Open In New Tab", Choose));
            buttons.Children.Add(SnippetUi.Button("Pin / Unpin", TogglePin));
            buttons.Children.Add(SnippetUi.Button("Make A Snippet", () =>
            {
                if (list.SelectedItem is Row picked)
                {
                    Chosen = picked.Entry;
                    MakeSnippet = true;
                    Close();
                }
            }));
            buttons.Children.Add(SnippetUi.Button("Delete", Delete));
            buttons.Children.Add(SnippetUi.Button("Close", Close));
            bottom.Children.Add(buttons);
            bottom.Children.Add(status);
            Grid.SetRow(bottom, 3);
            root.Children.Add(bottom);
            Content = root;

            Fill();
            Loaded += (s, e) => filter.Focus();
        }

        /// <summary>The run the user chose, or null.</summary>
        public HistoryEntry Chosen { get; private set; }

        /// <summary>The run is to become a snippet instead of being opened in a tab.</summary>
        public bool MakeSnippet { get; private set; }

        private void TogglePin()
        {
            if (!(list.SelectedItem is Row row))
            {
                return;
            }

            HistoryService.SetPinned(new[] { row.Entry.Id }, !row.Entry.Pinned);
            var id = row.Entry.Id;
            Fill();
            foreach (Row item in list.Items)
            {
                if (item.Entry.Id == id)
                {
                    list.SelectedItem = item;
                    break;
                }
            }
        }

        private const string ViewButtonTemplate =
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>" +
            "<Border x:Name='frame' Width='32' Height='32' Margin='2,0,0,0' BorderThickness='2' CornerRadius='3' BorderBrush='Transparent' Background='Transparent'>" +
            "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='Background' Value='#22808080'/></Trigger>" +
            "<Trigger Property='IsChecked' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='#3794FF'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate>";

        /// <summary>One of the toggle buttons that choose the view of the history; the one that is checked has a blue frame (#284).</summary>
        private RadioButton ViewButton(string glyph, string tooltip, HistoryView target, bool isChecked)
        {
            var icon = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16 };
            icon.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.WindowTextKey);
            var button = new RadioButton
            {
                GroupName = "HistoryViews",
                Content = icon,
                ToolTip = tooltip,
                IsChecked = isChecked,
                Focusable = false,
                Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(ViewButtonTemplate),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
            button.Checked += (s, e) =>
            {
                view = target;
                Fill();
            };
            return button;
        }

        private const string RowStyle =
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListViewItem'>" +
            "<Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='2'/></Style>";

        private const string RowTemplate =
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<Grid Margin='4,3,4,3' ToolTip='{Binding Tooltip}'>" +
            "<Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto' MaxWidth='260'/></Grid.ColumnDefinitions>" +
            "<StackPanel Grid.Column='0'>" +
            "<StackPanel Orientation='Horizontal'>" +
            "<TextBlock Text='&#xE718;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' Foreground='#E5484D' FontSize='12' Margin='0,3,6,0' Visibility='{Binding PinVisibility}'/>" +
            "<TextBlock Text='{Binding TabName}' FontSize='14' TextTrimming='CharacterEllipsis'/>" +
            "</StackPanel>" +
            "<TextBlock Text='{Binding RunText}' FontSize='11' Opacity='0.65'/>" +
            "</StackPanel>" +
            "<StackPanel Grid.Column='1' Margin='12,0,0,0' HorizontalAlignment='Right'>" +
            "<TextBlock Text='{Binding Server}' FontSize='14' HorizontalAlignment='Right' TextTrimming='CharacterEllipsis'/>" +
            "<TextBlock Text='{Binding Database}' FontSize='11' Opacity='0.65' HorizontalAlignment='Right' TextTrimming='CharacterEllipsis'/>" +
            "</StackPanel>" +
            "</Grid></DataTemplate>";

        private void Fill()
        {
            var everything = HistoryService.Entries;
            var found = HistoryStore.Filter(everything, filter.Text, view);
            list.Items.Clear();
            foreach (var entry in found.Take(MaxShown))
            {
                list.Items.Add(new Row(entry));
            }

            status.Text = found.Count + " of " + everything.Count + " runs" + (found.Count > MaxShown ? " (the newest " + MaxShown + " are shown)" : string.Empty);
            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
            else
            {
                preview.PlainText = string.Empty;
            }
        }

        private void Move(int step)
        {
            if (list.Items.Count == 0)
            {
                return;
            }

            list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + step));
            list.ScrollIntoView(list.SelectedItem);
        }

        private void ShowPreview() => preview.PlainText = (list.SelectedItem as Row)?.Entry.Text ?? string.Empty;

        private void Choose()
        {
            if (list.SelectedItem is Row row)
            {
                Chosen = row.Entry;
                Close();
            }
        }

        private void Delete()
        {
            if (!(list.SelectedItem is Row row))
            {
                return;
            }

            if (MessageBox.Show(this, "Delete this run from the history?", "SQL Prompter", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            HistoryService.Delete(new[] { row.Entry.Id });
            Fill();
        }

        private sealed class Row
        {
            public Row(HistoryEntry entry)
            {
                Entry = entry;
            }

            public HistoryEntry Entry { get; }

            public string TabName => Entry.Tab.Length > 0 ? Entry.Tab : "(no tab name)";

            /// <summary>The time of the run, to the minute.</summary>
            public string RunText => Entry.RanAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            public string Server => Entry.Server;

            public string Database => Entry.Database;

            public Visibility PinVisibility => Entry.Pinned ? Visibility.Visible : Visibility.Collapsed;

            /// <summary>The full texts of the row, including whether the tab is still open or when it was closed.</summary>
            public string Tooltip =>
                TabName + "\nRun: " + Entry.RanAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
                (Entry.ClosedAtUtc.HasValue ? "Tab closed: " + Entry.ClosedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "Tab is still open") +
                "\nServer: " + Entry.Server + "\nDatabase: " + Entry.Database + (Entry.Pinned ? "\nPinned" : string.Empty);
        }
    }
}
