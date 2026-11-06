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

            var grid = new GridView();
            grid.Columns.Add(Column("Pin", "Pin", 30));
            grid.Columns.Add(Column("Run", "Run", 140));
            grid.Columns.Add(Column("Tab", "Tab", 130));
            grid.Columns.Add(Column("Closed", "Closed", 140));
            grid.Columns.Add(Column("Database", "Database", 120));
            grid.Columns.Add(Column("Server", "Server", 120));
            list.View = grid;
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
            root.Children.Add(SnippetUi.Label("Type words to filter (text, tab, server, database). Enter or a double click opens the query in a new tab; Esc closes."));
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

        private static GridViewColumn Column(string header, string binding, double width) =>
            new GridViewColumn { Header = header, Width = width, DisplayMemberBinding = new System.Windows.Data.Binding(binding) };

        private void Fill()
        {
            var everything = HistoryService.Entries;
            var found = HistoryStore.Filter(everything, filter.Text);
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

            public string Pin => Entry.Pinned ? "*" : string.Empty;

            public string Run => Entry.RanAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            public string Closed => Entry.ClosedAtUtc.HasValue ? Entry.ClosedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;

            public string Database => Entry.Database;

            public string Server => Entry.Server;

            public string Tab => Entry.Tab;
        }
    }
}
