using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Formatting;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// SQL Prompter > Formatting Styles: several named styles with one active, a tree of the option groups with a search box, the options of the
    /// group with proper controls, and a preview that formats a sample of the group (or the current query) with the style being edited.
    /// </summary>
    internal sealed class FormattingStylesWindow : Form
    {
        private readonly List<NamedFormatStyle> styles;
        private readonly ComboBox styleBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        private readonly Label activeLabel = new Label { AutoSize = true, Margin = new Padding(8, 7, 0, 0), ForeColor = SystemColors.GrayText };
        private readonly TextBox search = new TextBox { Dock = DockStyle.Top };
        private readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None, ShowLines = false, ShowRootLines = false };
        private readonly Panel host = new Panel { Dock = DockStyle.Fill };
        private readonly CheckBox currentQuery = new CheckBox { Text = "Preview the current query", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        private string activeName;
        private NamedFormatStyle current;
        private SettingsPage page;
        private string pageGroup;
        private bool loading;

        public FormattingStylesWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = SettingsService.Current;
            styles = settings.FormattingStyles.Select(s => s.Clone()).ToList();
            activeName = settings.ActiveStyle.Name;
            current = styles.First(s => s.Name == activeName);

            Text = "SQL Prompter Formatting Styles";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Size = new Size(1040, 800);
            MinimumSize = new Size(820, 600);

            // One row; every control is vertically centered in it, so the label, the combo box and the buttons share one baseline (#304).
            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 4, 8, 4), ColumnCount = 0, RowCount = 1, GrowStyle = TableLayoutPanelGrowStyle.AddColumns };
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            void AddTop(Control control)
            {
                control.Anchor = AnchorStyles.Left;
                control.Margin = new Padding(0, 0, 6, 0);
                top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                top.Controls.Add(control, top.ColumnStyles.Count - 1, 0);
            }

            AddTop(new Label { Text = "Style", AutoSize = true });
            AddTop(styleBox);
            AddTop(Button("New", NewStyle));
            AddTop(Button("Duplicate", Duplicate));
            AddTop(Button("Rename", Rename));
            AddTop(Button("Delete", Delete));
            AddTop(Button("Import...", Import));
            AddTop(Button("Set as active", () => { activeName = current.Name; ShowActive(); }));
            activeLabel.Margin = new Padding(6, 0, 0, 0);
            AddTop(activeLabel);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 8, 8, 0) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            var save = new Button { Text = "Save", Width = 90 };
            save.Click += (s, e) => Save();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            CancelButton = cancel;

            search.TextChanged += (s, e) => ShowPage();
            tree.AfterSelect += (s, e) => { if (!loading) { search.Text = string.Empty; ShowPage(); } };
            var left = new Panel { Dock = DockStyle.Left, Width = 230, Padding = new Padding(8, 4, 0, 4) };
            left.Controls.Add(tree);
            left.Controls.Add(search);
            left.Controls.Add(new Label { Text = "Search for options", Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText });

            foreach (var group in FormatOptionCatalog.Groups)
            {
                tree.Nodes.Add(new TreeNode(group.Name) { Tag = group.Name });
            }

            styleBox.SelectedIndexChanged += (s, e) =>
            {
                if (!loading && styleBox.SelectedIndex >= 0)
                {
                    current = styles[styleBox.SelectedIndex];
                    ShowActive();
                    ShowPage(true);
                }
            };
            currentQuery.CheckedChanged += (s, e) => page?.UpdateEnabled();

            Controls.Add(host);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = SystemColors.ControlDark });
            Controls.Add(left);
            Controls.Add(buttons);
            Controls.Add(top);
            RefreshStyles();
            tree.SelectedNode = tree.Nodes[0];
            WinFormsTheme.Apply(this);
        }

        public static void Open()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                using (var window = new FormattingStylesWindow())
                {
                    window.ShowDialog(new MainWindowOwner());
                }
            }
            catch (Exception ex)
            {
                Log.Error("Opening the formatting styles window failed", ex);
            }
        }

        /// <summary>The SSMS main window as the owner of a WinForms dialog, so that the dialog stays in front of it.</summary>
        private sealed class MainWindowOwner : IWin32Window
        {
            public IntPtr Handle => System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        }

        private static Button Button(string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (s, e) => click();
            return button;
        }

        private void RefreshStyles()
        {
            loading = true;
            styleBox.Items.Clear();
            foreach (var style in styles)
            {
                styleBox.Items.Add(style.Name);
            }

            styleBox.SelectedIndex = styles.IndexOf(current);
            loading = false;
            ShowActive();
        }

        private void ShowActive() => activeLabel.Text = string.Equals(current.Name, activeName, StringComparison.OrdinalIgnoreCase)
            ? "(the active style)"
            : "(not active; the active style is \"" + activeName + "\")";

        private void ShowPage(bool force = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var text = search.Text.Trim();
            var group = tree.SelectedNode?.Tag as string ?? FormatOptionCatalog.Groups[0].Name;
            var key = text.Length > 0 ? "?" + text : group;
            if (!force && page != null && key == pageGroup)
            {
                return;
            }

            pageGroup = key;
            var options = text.Length > 0 ? FormatOptionCatalog.Search(text) : FormatOptionCatalog.OptionsOf(group);
            var shownGroup = text.Length > 0 ? (options.Count > 0 ? options[0].Group : group) : group;
            var info = FormatOptionCatalog.GroupOf(shownGroup);
            var next = new SettingsPage(text.Length > 0 ? "Search: " + text : info.Name, text.Length > 0 && options.Count == 0 ? "No option matches." : info.Description);
            string lastGroup = null;
            foreach (var option in options)
            {
                if (text.Length > 0 && option.Group != lastGroup)
                {
                    next.Heading(option.Group);
                    lastGroup = option.Group;
                }

                AddOption(next, option);
            }

            next.Preview("Preview: " + info.Name + " sample", () => Render(info), currentQuery, () => currentQuery.Checked ? "Preview: your current query" : "Preview: " + info.Name + " sample", () => CollapsedNote);
            host.Controls.Clear();
            host.Controls.Add(next);
            page?.Dispose();
            page = next;
            page.Reload();
        }

        private void AddOption(SettingsPage target, FormatOption option)
        {
            switch (option.Kind)
            {
                case FormatOptionKind.Bool:
                    target.Check(option.Title, option.Help, () => (bool)option.Get(current.Style), v => option.Set(current.Style, v));
                    break;
                case FormatOptionKind.Number:
                    target.Number(option.Title, option.Help, option.Min, option.Max, () => (int)option.Get(current.Style), v => option.Set(current.Style, v), option.Unit);
                    break;
                default:
                    target.Choice(option.Title, option.Help, new[] { Pair(CaseStyle.Keep, "Keep as typed"), Pair(CaseStyle.Upper, "UPPER CASE"), Pair(CaseStyle.Lower, "lower case") },
                        () => (CaseStyle)option.Get(current.Style), v => option.Set(current.Style, v));
                    break;
            }
        }

        private static KeyValuePair<CaseStyle, string> Pair(CaseStyle key, string text) => new KeyValuePair<CaseStyle, string>(key, text);

        /// <summary>The sample of the group, or the selection or text of the active editor when the toggle is on, formatted with the style being edited.</summary>
        private string Render(FormatOptionGroup group)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var text = group.Sample;
            if (currentQuery.Checked)
            {
                var editor = EditorText();
                if (!string.IsNullOrWhiteSpace(editor))
                {
                    text = editor;
                }
            }

            var result = SqlFormatter.Format(text, current.Style);
            collapsedNote = result.Collapsed == 0
                ? null
                : (result.Collapsed == 1 ? "This statement is" : result.Collapsed + " statements are") + " kept on one line by Short statements (shorter than " + current.Style.CollapseStatementsShorterThan +
                  " characters), so the options that split a statement over lines show no effect on it. Lower the limit, or set it to 0, to see them.";
            return result.Text;
        }

        private string collapsedNote;

        /// <summary>The note under the preview when the style kept statements of the preview on one line (#441); null when nothing was.</summary>
        private string CollapsedNote => collapsedNote;

        private static string EditorText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var view = ActiveEditor.GetView();
                if (view == null)
                {
                    return null;
                }

                var text = view.Selection.IsEmpty ? view.TextBuffer.CurrentSnapshot.GetText() : view.Selection.StreamSelectionSpan.GetText();
                return text.Length > 20000 ? text.Substring(0, 20000) : text;
            }
            catch (Exception ex)
            {
                Log.Error("Reading the current query for the preview failed", ex);
                return null;
            }
        }

        private void NewStyle()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var name = TextPrompt.Ask(this, "New style", "Name of the new style", string.Empty, Taken);
            if (name != null)
            {
                current = new NamedFormatStyle(name, new FormatStyle());
                styles.Add(current);
                RefreshStyles();
                ShowPage(true);
            }
        }

        private void Duplicate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var name = TextPrompt.Ask(this, "Duplicate style", "Name of the copy", current.Name + " copy", Taken);
            if (name != null)
            {
                current = new NamedFormatStyle(name, current.Style.Clone());
                styles.Add(current);
                RefreshStyles();
                ShowPage(true);
            }
        }

        /// <summary>Reads a SQL Prompt style file into a new style of the list (#304); the window's own copy of the list changes, so Cancel drops it.</summary>
        private void Import()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var imported = StyleImportDialog.Run(this, styles);
            if (imported != null)
            {
                current = imported;
                activeName = imported.Name;
                RefreshStyles();
                ShowPage(true);
            }
        }

        private void Rename()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var oldName = current.Name;
            var name = TextPrompt.Ask(this, "Rename style", "New name", oldName, n => !string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase) && Taken(n));
            if (name != null)
            {
                if (string.Equals(activeName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    activeName = name;
                }

                current.Name = name;
                RefreshStyles();
            }
        }

        private void Delete()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (styles.Count <= 1)
            {
                MessageBox.Show(this, "There has to be at least one style.", "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this, "Delete the style \"" + current.Name + "\"?", "SQL Prompter", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            var wasActive = string.Equals(current.Name, activeName, StringComparison.OrdinalIgnoreCase);
            var index = styles.IndexOf(current);
            styles.Remove(current);
            current = styles[Math.Min(index, styles.Count - 1)];
            if (wasActive)
            {
                activeName = current.Name;
            }

            RefreshStyles();
            ShowPage(true);
        }

        private bool Taken(string name) => styles.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        private void Save()
        {
            var settings = SettingsService.Current.Clone();
            settings.FormattingStyles = styles.Select(s => s.Clone()).ToList();
            settings.ActiveFormattingStyle = activeName;
            settings.EnsureStyles();
            SettingsService.Update(settings);
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>A small dialog that asks for one line of text.</summary>
    internal static class TextPrompt
    {
        /// <summary>The trimmed text, or null when cancelled. <paramref name="taken"/> says that a name is in use.</summary>
        public static string Ask(IWin32Window owner, string title, string label, string initial, Func<string, bool> taken)
        {
            using (var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(380, 120),
            })
            {
                var box = new TextBox { Left = 12, Top = 36, Width = 356, Text = initial };
                var ok = new Button { Text = "OK", Left = 200, Top = 78, Width = 80 };
                var cancel = new Button { Text = "Cancel", Left = 290, Top = 78, Width = 80, DialogResult = DialogResult.Cancel };
                ok.Click += (s, e) =>
                {
                    var name = box.Text.Trim();
                    if (name.Length == 0)
                    {
                        MessageBox.Show(form, "Type a name.", "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else if (taken(name))
                    {
                        MessageBox.Show(form, "A style with this name exists already.", "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        form.DialogResult = DialogResult.OK;
                    }
                };
                form.Controls.AddRange(new Control[] { new Label { Text = label, Left = 12, Top = 14, AutoSize = true }, box, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                box.SelectAll();
                ThreadHelper.ThrowIfNotOnUIThread();
                WinFormsTheme.Apply(form);
                return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }
    }
}
