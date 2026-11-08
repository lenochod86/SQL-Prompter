using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Ssms
{
    /// <summary>Small shared helpers for the code-only snippet windows (theme colors of the shell).</summary>
    internal static class SnippetUi
    {
        public static void Theme(Control control)
        {
            control.SetResourceReference(Control.BackgroundProperty, VsBrushes.WindowKey);
            control.SetResourceReference(Control.ForegroundProperty, VsBrushes.WindowTextKey);
        }

        public static void Theme(Window window)
        {
            window.SetResourceReference(Window.BackgroundProperty, VsBrushes.WindowKey);
            window.SetResourceReference(Window.ForegroundProperty, VsBrushes.WindowTextKey);

            // The scroll bars of the lists and boxes in the window follow the theme of the shell too.
            if (window.TryFindResource(VsResourceKeys.ScrollBarStyleKey) is Style scrollBars)
            {
                window.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = scrollBars;
            }
        }

        public static TextBox Box(bool multiline, double height = double.NaN)
        {
            var box = new TextBox
            {
                Margin = new Thickness(0, 2, 0, 8),
                Padding = new Thickness(4),
                BorderThickness = new Thickness(1),
                FontFamily = multiline ? new FontFamily("Consolas") : SystemFonts.MessageFontFamily,
            };
            Theme(box);
            box.SetResourceReference(Control.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            if (multiline)
            {
                box.AcceptsReturn = true;
                box.AcceptsTab = true;
                box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                box.TextWrapping = TextWrapping.NoWrap;
            }

            if (!double.IsNaN(height))
            {
                box.Height = height;
            }

            return box;
        }

        public static TextBlock Label(string text)
        {
            var label = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold };
            label.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);
            return label;
        }

        public static Button Button(string text, Action click)
        {
            var button = new Button { Content = text, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0), MinWidth = 80 };
            button.Click += (s, e) => click();
            return button;
        }

        public static string Describe(Snippet snippet) =>
            string.IsNullOrWhiteSpace(snippet.Description) ? snippet.Prefix : snippet.Prefix + "  —  " + snippet.Description;
    }

    /// <summary>A filter box and a list of snippets to choose one from, e.g. to wrap the selection with.</summary>
    internal sealed class SnippetPickerWindow : Window
    {
        private readonly IReadOnlyList<Snippet> snippets;
        private readonly TextBox filter = SnippetUi.Box(false);
        private readonly ListBox list = new ListBox { MinHeight = 180 };
        private readonly TextBox preview = SnippetUi.Box(true, 120);

        public SnippetPickerWindow(string title, IReadOnlyList<Snippet> snippets)
        {
            this.snippets = snippets;
            Title = title;
            Width = 560;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SnippetUi.Theme(this);
            SnippetUi.Theme(list);
            preview.IsReadOnly = true;

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(SnippetUi.Label("Type to filter, Enter inserts, Esc cancels"));
            Grid.SetRow(filter, 1);
            Grid.SetRow(list, 2);
            Grid.SetRow(preview, 3);
            preview.Margin = new Thickness(0, 8, 0, 8);
            root.Children.Add(filter);
            root.Children.Add(list);
            root.Children.Add(preview);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(SnippetUi.Button("Insert", Choose));
            buttons.Children.Add(SnippetUi.Button("Cancel", Close));
            Grid.SetRow(buttons, 4);
            root.Children.Add(buttons);
            Content = root;

            filter.TextChanged += (s, e) => Fill();
            list.SelectionChanged += (s, e) => preview.Text = (list.SelectedItem as ListBoxItem)?.Tag is Snippet snippet ? snippet.Body : string.Empty;
            list.MouseDoubleClick += (s, e) => Choose();
            PreviewKeyDown += OnKeyDown;
            Loaded += (s, e) => filter.Focus();
            Fill();
        }

        /// <summary>The snippet the user chose, or null if the window was cancelled.</summary>
        public Snippet Chosen { get; private set; }

        private void Fill()
        {
            var text = filter.Text.Trim();
            list.Items.Clear();
            foreach (var snippet in snippets.Where(s => text.Length == 0 ||
                s.Prefix.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || s.Description.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                list.Items.Add(new ListBoxItem { Content = SnippetUi.Describe(snippet), Tag = snippet });
            }

            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }

        private void Choose()
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is Snippet snippet)
            {
                Chosen = snippet;
                DialogResult = true;
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

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Choose();
                    e.Handled = true;
                    break;
                case Key.Down:
                    Move(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    Move(-1);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    Move(10);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    Move(-10);
                    e.Handled = true;
                    break;
            }
        }
    }

    /// <summary>The list of snippets with an editor for the selected one: new, save, delete.</summary>
    internal sealed class SnippetManagerWindow : Window
    {
        private readonly SnippetStore store;
        private readonly ListBox list = new ListBox();
        private readonly TextBox prefix = SnippetUi.Box(false);
        private readonly TextBox description = SnippetUi.Box(false);
        private readonly SnippetBodyEditor body = NewBody();
        private readonly System.Collections.ObjectModel.ObservableCollection<PlaceholderRow> placeholderRows = new System.Collections.ObjectModel.ObservableCollection<PlaceholderRow>();
        private readonly DataGrid placeholders = NewPlaceholderGrid();
        private readonly TextBlock status = new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        private Snippet editing;

        /// <param name="newDraft">A snippet to start with as a new one (e.g. made of a selection); null lists the existing ones.</param>
        public SnippetManagerWindow(SnippetStore store, Snippet newDraft)
        {
            this.store = store;
            Title = "SQL Prompter snippets — " + store.Folder;
            Width = 900;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SnippetUi.Theme(this);
            SnippetUi.Theme(list);
            status.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);

            var root = new Grid { Margin = new Thickness(12) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(list);
            list.Margin = new Thickness(0, 0, 12, 0);
            list.SelectionChanged += (s, e) =>
            {
                if (list.SelectedItem is ListBoxItem item && item.Tag is Snippet snippet)
                {
                    Edit(snippet);
                }
            };

            var editor = new Grid();
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var stack = new StackPanel();
            stack.Children.Add(SnippetUi.Label("Prefix (what you type to expand it)"));
            stack.Children.Add(prefix);
            stack.Children.Add(SnippetUi.Label("Description"));
            stack.Children.Add(description);
            stack.Children.Add(SnippetUi.Label("Body  ($CURSOR$, $SELECTEDTEXT$, $USER$, $DBNAME$, $DATE(format)$, $field$)"));
            editor.Children.Add(stack);
            Grid.SetRow(body, 1);
            Grid.SetRowSpan(body, 4);
            body.Margin = new Thickness(0, 2, 0, 8);
            body.VerticalAlignment = VerticalAlignment.Stretch;
            // The body takes the free space; fields below it.
            editor.Children.Remove(stack);
            var top = new StackPanel();
            top.Children.Add(stack);
            Grid.SetRow(top, 0);
            editor.Children.Add(top);
            Grid.SetRow(body, 4);
            Grid.SetRowSpan(body, 1);
            editor.Children.Add(body);
            var fieldsPanel = new StackPanel();
            fieldsPanel.Children.Add(SnippetUi.Label("Custom fields (name and the default value shown when the snippet is inserted)"));
            placeholders.ItemsSource = placeholderRows;
            fieldsPanel.Children.Add(placeholders);
            Grid.SetRow(fieldsPanel, 5);
            editor.Children.Add(fieldsPanel);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(SnippetUi.Button("New", () => Edit(null)));
            buttons.Children.Add(SnippetUi.Button("Save", Save));
            buttons.Children.Add(SnippetUi.Button("Delete", Delete));
            buttons.Children.Add(SnippetUi.Button("Close", Close));
            var more = new StackPanel { Orientation = Orientation.Horizontal };
            more.Children.Add(SnippetUi.Button("Import...", () => { ThreadHelper.ThrowIfNotOnUIThread(); SqlPrompterPackage.ImportSnippets(); Reload(editing?.Id); }));
            more.Children.Add(SnippetUi.Button("Open folder", SqlPrompterPackage.OpenSnippetsFolder));
            more.Children.Add(SnippetUi.Button("Reload", () => { SnippetService.Reload(); Reload(editing?.Id); }));
            var bottom = new StackPanel();
            bottom.Children.Add(more);
            bottom.Children.Add(buttons);
            bottom.Children.Add(status);
            Grid.SetRow(bottom, 6);
            editor.Children.Add(bottom);
            Grid.SetColumn(editor, 1);
            root.Children.Add(editor);
            Content = root;

            Reload(null);
            if (newDraft != null)
            {
                Edit(newDraft, isNew: true);
            }
            else if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
            else
            {
                Edit(null);
            }
        }

        private void Reload(string selectId)
        {
            var result = store.Load();
            var shared = SettingsService.Current.Snippets.SharedFolderList().Select(f => (IEnumerable<Snippet>)new SnippetStore(Environment.ExpandEnvironmentVariables(f)).Load().Snippets).ToList();
            var all = SnippetMerge.Merge(result.Snippets, shared);
            list.Items.Clear();
            foreach (var snippet in all)
            {
                // A shared snippet is copied to the personal folder when it is saved here; the personal copy then wins.
                var item = new ListBoxItem { Content = snippet.IsShared ? snippet.Prefix + "  (shared)" : snippet.Prefix, Tag = snippet };
                list.Items.Add(item);
                if (snippet.Id == selectId)
                {
                    list.SelectedItem = item;
                }
            }

            status.Text = all.Count + " snippets" + (all.Any(s => s.IsShared) ? " (shared ones are copied to your folder when you save them)" : string.Empty) + (result.Errors.Count > 0 ? "; not readable: " + string.Join(", ", result.Errors.Select(e => e.Split(':')[0])) : string.Empty);
        }


        private sealed class PlaceholderRow
        {
            public string Name { get; set; }

            public string DefaultValue { get; set; }
        }

        private static SnippetBodyEditor NewBody()
        {
            var editor = new SnippetBodyEditor { Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(4), BorderThickness = new Thickness(1) };
            SnippetUi.Theme(editor);
            editor.SetResourceReference(Control.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            return editor;
        }

        private static DataGrid NewPlaceholderGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = true,
                CanUserDeleteRows = true,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Height = 110,
                Margin = new Thickness(0, 2, 0, 8),
                SelectionUnit = DataGridSelectionUnit.FullRow,
            };
            SnippetUi.Theme(grid);
            grid.SetResourceReference(DataGrid.RowBackgroundProperty, VsBrushes.WindowKey);
            grid.SetResourceReference(DataGrid.BackgroundProperty, VsBrushes.WindowKey);
            grid.SetResourceReference(DataGrid.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, VsBrushes.ToolWindowBorderKey);
            grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Default value", Binding = new System.Windows.Data.Binding("DefaultValue"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            return grid;
        }
        private bool isNew;

        private void Edit(Snippet snippet, bool isNew = false)
        {
            this.isNew = snippet == null || isNew;
            editing = snippet ?? Snippet.Create(string.Empty, string.Empty, string.Empty);
            prefix.Text = editing.Prefix;
            description.Text = editing.Description;
            body.PlainText = editing.Body;
            placeholderRows.Clear();
            foreach (var placeholder in editing.Placeholders ?? new List<SnippetPlaceholder>())
            {
                placeholderRows.Add(new PlaceholderRow { Name = placeholder.Name, DefaultValue = placeholder.DefaultValue });
            }
            if (this.isNew)
            {
                list.SelectedItem = null;
                prefix.Focus();
            }
        }

        private void Save()
        {
            var text = prefix.Text.Trim();
            if (text.Length == 0)
            {
                status.Text = "Give the snippet a prefix.";
                return;
            }

            var clash = store.Load().Snippets.FirstOrDefault(s => s.Id != editing.Id && string.Equals(s.Prefix, text, StringComparison.OrdinalIgnoreCase));
            if (clash != null)
            {
                status.Text = "Another snippet has the prefix \"" + text + "\".";
                return;
            }

            editing.Prefix = text;
            editing.Description = description.Text.Trim();
            editing.Body = body.PlainText;
            placeholders.CommitEdit(DataGridEditingUnit.Row, true);
            editing.Placeholders = placeholderRows
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => new SnippetPlaceholder { Name = r.Name.Trim(), DefaultValue = r.DefaultValue ?? string.Empty })
                .ToList();
            store.Save(editing);
            SnippetService.Reload();
            Reload(editing.Id);
            status.Text = "Saved \"" + text + "\".";
            isNew = false;
        }

        private void Delete()
        {
            if (isNew)
            {
                Edit(null);
                return;
            }

            if (MessageBox.Show(this, "Delete the snippet \"" + editing.Prefix + "\"?", "SQL Prompter", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            store.Delete(editing.Id);
            SnippetService.Reload();
            Reload(null);
            status.Text = "Deleted.";
            Edit(null);
        }
    }
}
