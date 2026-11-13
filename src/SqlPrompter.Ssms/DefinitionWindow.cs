using System;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// A definition pinned away from the completion list: the script (colored as SQL) and, for a table, view, procedure or function, the summary
    /// (columns or parameters) in a window that stays until it is closed. The text and the cells can be selected with the mouse, so a single
    /// name or type can be copied; the Copy button copies the whole script or summary.
    /// </summary>
    internal sealed class DefinitionWindow : Window
    {
        private readonly string script;
        private readonly ObjectSummary summary;
        private readonly TabControl tabs = new TabControl { Margin = new Thickness(0, 0, 0, 8) };

        private DefinitionWindow(string title, string script, ObjectSummary summary, bool startOnSummary, Size? size)
        {
            this.script = script;
            this.summary = summary;
            Title = title;
            Width = Math.Max(720, size?.Width ?? 0);
            Height = Math.Max(520, size?.Height ?? 0);
            ShowInTaskbar = false;
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            SnippetUi.Theme(this);
            SnippetUi.Theme(tabs);

            var editor = new SnippetBodyEditor { IsReadOnly = true, BorderThickness = new Thickness(0) };
            SnippetUi.Theme(editor);
            editor.PlainText = script;
            tabs.Items.Add(new TabItem { Header = "Script", Content = editor });
            if (summary != null)
            {
                tabs.Items.Add(new TabItem { Header = "Summary", Content = SummaryGrid(summary) });
                tabs.SelectedIndex = startOnSummary ? 1 : 0;
            }

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = SnippetUi.Label(title);
            header.FontSize = 14;
            header.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(tabs, 1);
            root.Children.Add(header);
            root.Children.Add(tabs);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(SnippetUi.Button("Copy", Copy));
            buttons.Children.Add(SnippetUi.Button("Close", Close));
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
            Content = root;
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
            SourceInitialized += (s, e) => System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
        }

        public static void Open(string title, string script, ObjectSummary summary, bool startOnSummary, Size? size = null)
        {
            var window = new DefinitionWindow(title, script, summary, startOnSummary, size);
            ActiveEditor.SetOwner(window);
            window.Show();
        }

        private static DataGrid SummaryGrid(ObjectSummary summary)
        {
            var table = new DataTable();
            for (var c = 0; c < summary.Headers.Count; c++)
            {
                table.Columns.Add("c" + c, typeof(string)).Caption = summary.Headers[c];
            }

            foreach (var row in summary.Rows)
            {
                table.Rows.Add(row.Cast<object>().ToArray());
            }

            var grid = new DataGrid
            {
                IsReadOnly = true,
                AutoGenerateColumns = false,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                SelectionMode = DataGridSelectionMode.Extended,
                ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                BorderThickness = new Thickness(0),
                ItemsSource = table.DefaultView,
            };
            SnippetUi.Theme(grid);
            for (var c = 0; c < summary.Headers.Count; c++)
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = summary.Headers[c],
                    Binding = new System.Windows.Data.Binding("[" + c + "]"),
                    Width = c == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                });
            }

            // The Copy command of the grid (Ctrl+C, and the context menu) copies the selected cells.
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "Copy", Command = System.Windows.Input.ApplicationCommands.Copy, CommandTarget = grid });
            grid.ContextMenu = menu;
            return grid;
        }

        private void Copy()
        {
            try
            {
                var onSummary = summary != null && tabs.SelectedIndex == 1;
                Clipboard.SetText(onSummary ? summary.ToText() : script);
            }
            catch (Exception ex)
            {
                Log.Error("Copying the definition failed", ex);
            }
        }
    }
}
