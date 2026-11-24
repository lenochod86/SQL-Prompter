using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// One page of the settings window: a scrolling column of headings, checkboxes, dropdowns, numeric boxes and tables, each with a short help text.
    /// Controls read their value from the working copy through a getter and write back through a setter; <see cref="UpdateEnabled"/> greys out the
    /// ones that depend on another option.
    /// </summary>
    internal sealed class SettingsPage : Panel
    {
        private const int TextWidth = 600;
        private readonly FlowLayoutPanel flow;
        private readonly Panel scroller;
        private readonly List<Action> refreshers = new List<Action>();
        private readonly List<Action> commits = new List<Action>();
        private readonly List<Action> enablers = new List<Action>();
        private readonly List<Func<IEnumerable<string>>> validators = new List<Func<IEnumerable<string>>>();
        private bool loading;
        private FormatPreview previewBox;
        private Func<string> previewText;
        private Func<string> previewHeading;
        private Label previewTitle;
        private Func<string> previewNote;
        private Label previewNoteLabel;

        public SettingsPage(string title, string intro = null)
        {
            Dock = DockStyle.Fill;
            Title = title;
            // The flow sizes itself to its content (so the scroll range is exactly its height, bottom padding included) and the panel around it scrolls (#356).
            scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            flow = new FlowLayoutPanel
            {
                Location = Point.Empty,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(14, 10, 14, 16),
            };
            scroller.Controls.Add(flow);
            Controls.Add(scroller);
            flow.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font.FontFamily, Font.Size + 4, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) });
            if (intro != null)
            {
                Help(intro, 0);
            }
        }

        public string Title { get; }

        /// <summary>Loads every control from the working copy.</summary>
        public void Reload()
        {
            loading = true;
            foreach (var refresh in refreshers)
            {
                refresh();
            }

            loading = false;
            UpdateEnabled();
        }

        /// <summary>Copies the values that are not written on every change (tables, the coloring editor) into the working copy.</summary>
        public void Commit()
        {
            foreach (var commit in commits)
            {
                commit();
            }
        }

        public IEnumerable<string> Problems() => validators.SelectMany(v => v());

        public void UpdateEnabled()
        {
            foreach (var enabler in enablers)
            {
                enabler();
            }

            RefreshPreview();
        }

        /// <summary>A colored preview under the options; it is drawn again after every change (<paramref name="render"/> returns the text to show).</summary>
        public void Preview(string heading, Func<string> render, Control headerExtra = null, Func<string> title = null, Func<string> note = null)
        {
            previewText = render;
            previewNote = note;
            previewBox = new FormatPreview { Dock = DockStyle.Fill };
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 250, Padding = new Padding(14, 4, 14, 10) };
            panel.Controls.Add(previewBox);
            if (note != null)
            {
                // A line under the preview that says why options seem to have no effect (a short statement kept on one line); empty text hides it.
                previewNoteLabel = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 34, ForeColor = SystemColors.GrayText, Visible = false, Padding = new Padding(0, 4, 0, 0) };
                panel.Controls.Add(previewNoteLabel);
            }

            var header = new Panel { Dock = DockStyle.Top, Height = 26 };
            previewTitle = new Label { Text = heading, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
            previewHeading = title;
            header.Controls.Add(previewTitle);
            if (headerExtra != null)
            {
                headerExtra.Dock = DockStyle.Right;
                header.Controls.Add(headerExtra);
            }

            panel.Controls.Add(header);
            Controls.Add(panel);
            scroller.BringToFront();
        }

        private void RefreshPreview()
        {
            if (previewBox == null)
            {
                return;
            }

            try
            {
                previewBox.ShowText(previewText());
                if (previewNoteLabel != null)
                {
                    var noteText = previewNote() ?? string.Empty;
                    previewNoteLabel.Text = noteText;
                    previewNoteLabel.Visible = noteText.Length > 0;
                }

                if (previewHeading != null)
                {
                    previewTitle.Text = previewHeading();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Drawing the preview failed", ex);
            }
        }

        public void Heading(string text)
        {
            flow.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 16, 0, 0) });
        }

        /// <summary>A line of text that is read again from <paramref name="get"/> whenever the page is loaded or an option changes.</summary>
        public void Info(Func<string> get)
        {
            var label = new Label { AutoSize = true, MaximumSize = new Size(TextWidth, 0), Margin = new Padding(0, 10, 0, 0), Font = new Font(Font, FontStyle.Bold) };
            refreshers.Add(() => label.Text = get());
            enablers.Add(() => label.Text = get());
            flow.Controls.Add(label);
        }

        public void Help(string text, int indent = 0)
        {
            flow.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(TextWidth - indent * 22, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(indent * 22 + 20, 0, 0, 0),
            });
        }

        public void Check(string text, string help, Func<bool> get, Action<bool> set, Func<bool> enabledWhen = null, int indent = 0)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(indent * 22, 8, 0, 0) };
            refreshers.Add(() => box.Checked = get());
            box.CheckedChanged += (s, e) =>
            {
                if (!loading)
                {
                    set(box.Checked);
                    UpdateEnabled();
                }
            };
            if (enabledWhen != null)
            {
                enablers.Add(() => box.Enabled = enabledWhen());
            }

            flow.Controls.Add(box);
            if (!string.IsNullOrEmpty(help))
            {
                Help(help, indent);
            }
        }

        public void Choice<T>(string label, string help, IList<KeyValuePair<T, string>> options, Func<T> get, Action<T> set, Func<bool> enabledWhen = null)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
            foreach (var option in options)
            {
                combo.Items.Add(option.Value);
            }

            refreshers.Add(() =>
            {
                var current = get();
                var index = 0;
                for (var i = 0; i < options.Count; i++)
                {
                    if (EqualityComparer<T>.Default.Equals(options[i].Key, current))
                    {
                        index = i;
                    }
                }

                combo.SelectedIndex = index;
            });
            combo.SelectedIndexChanged += (s, e) =>
            {
                if (!loading && combo.SelectedIndex >= 0)
                {
                    set(options[combo.SelectedIndex].Key);
                    UpdateEnabled();
                }
            };
            AddRow(label, combo, null, help, enabledWhen, combo);
        }

        public void Number(string label, string help, int min, int max, Func<int> get, Action<int> set, string suffix = null, Func<bool> enabledWhen = null)
        {
            var box = new NumericUpDown { Minimum = min, Maximum = max, Width = 80 };
            refreshers.Add(() => box.Value = Math.Max(min, Math.Min(max, get())));
            box.ValueChanged += (s, e) =>
            {
                if (!loading)
                {
                    set((int)box.Value);
                    UpdateEnabled();
                }
            };
            AddRow(label, box, suffix, help, enabledWhen, box);
        }

        public void Folder(string label, string help, Func<string> get, Action<string> set, Action<string> open = null)
        {
            var text = new TextBox { Width = 380 };
            var browse = new Button { Text = "Browse...", AutoSize = true };
            refreshers.Add(() => text.Text = get() ?? string.Empty);
            text.TextChanged += (s, e) =>
            {
                if (!loading)
                {
                    set(text.Text.Trim());
                }
            };
            browse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog { SelectedPath = text.Text })
                {
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    {
                        text.Text = dialog.SelectedPath;
                    }
                }
            };
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            panel.Controls.Add(text);
            panel.Controls.Add(browse);
            if (open != null)
            {
                text.Width = 280;
                var openButton = new Button { Text = "Open folder", AutoSize = true };
                openButton.Click += (s, e) => open(text.Text);
                panel.Controls.Add(openButton);
            }
            AddRow(label, panel, null, help, null, null);
        }

        /// <summary>A grid of text columns for a list of rows; rows with an empty cell are dropped.</summary>
        public void Table(string heading, string help, string[] columns, Func<IEnumerable<string[]>> get, Action<List<string[]>> set)
        {
            Heading(heading);
            if (!string.IsNullOrEmpty(help))
            {
                Help(help);
            }

            var grid = new DataGridView
            {
                Width = TextWidth,
                Height = 170,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                Margin = new Padding(0, 4, 0, 0),
            };
            foreach (var column in columns)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column });
            }

            refreshers.Add(() =>
            {
                grid.Rows.Clear();
                foreach (var row in get())
                {
                    grid.Rows.Add(row.Cast<object>().ToArray());
                }
            });
            commits.Add(() =>
            {
                grid.EndEdit();
                var rows = new List<string[]>();
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }

                    var cells = columns.Select((c, i) => (Convert.ToString(row.Cells[i].Value) ?? string.Empty).Trim()).ToArray();
                    if (cells.All(c => c.Length > 0))
                    {
                        rows.Add(cells);
                    }
                }

                set(rows);
            });
            flow.Controls.Add(grid);
        }

        public void Button(string text, Action click, string help = null)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            button.Click += (s, e) => click();
            flow.Controls.Add(button);
            if (!string.IsNullOrEmpty(help))
            {
                Help(help);
            }
        }

        /// <summary>
        /// The rules of the code analysis grouped by category (#403): a filter box, Enable all / Disable all for the whole list and for each category, and a check box with
        /// the code and the title of every rule above its description.
        /// </summary>
        public void RuleCatalog(IReadOnlyList<SqlPrompter.Core.Analysis.AnalysisRule> rules, Func<SqlPrompter.Core.Analysis.AnalysisRule, bool> get, Action<SqlPrompter.Core.Analysis.AnalysisRule, bool> set, Func<bool> enabledWhen)
        {
            var rows = new List<KeyValuePair<SqlPrompter.Core.Analysis.AnalysisRule, Control[]>>();
            var headers = new List<KeyValuePair<SqlPrompter.Core.Analysis.RuleCategory, Control>>();

            void SetAll(IEnumerable<SqlPrompter.Core.Analysis.AnalysisRule> which, bool on)
            {
                foreach (var rule in which)
                {
                    set(rule, on);
                }

                Reload();
            }

            Control Buttons(Action<bool> all, string enableText = "Enable all", string disableText = "Disable all")
            {
                var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
                var enable = new Button { Text = enableText, AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
                var disable = new Button { Text = disableText, AutoSize = true, Margin = new Padding(0) };
                enable.Click += (s, e) => all(true);
                disable.Click += (s, e) => all(false);
                if (enabledWhen != null)
                {
                    enablers.Add(() => enable.Enabled = disable.Enabled = enabledWhen());
                }

                row.Controls.Add(enable);
                row.Controls.Add(disable);
                return row;
            }

            var filterRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            filterRow.Controls.Add(new Label { Text = "Filter:", AutoSize = false, Width = 50, Height = 24, TextAlign = ContentAlignment.MiddleLeft });
            var filter = new TextBox { Width = 320 };
            filterRow.Controls.Add(filter);
            flow.Controls.Add(filterRow);
            flow.Controls.Add(new Label { Text = "All rules:", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 0) });
            flow.Controls.Add(Buttons(on => SetAll(rules, on), "Enable all rules", "Disable all rules"));

            foreach (var category in rules.Select(r => r.Category).Distinct())
            {
                var inCategory = rules.Where(r => r.Category == category).ToList();
                var header = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 16, 0, 0) };
                header.Controls.Add(new Label { Text = SqlPrompter.Core.Analysis.AnalysisRules.CategoryName(category) + " (" + inCategory.Count + ")", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 14, 0) });
                var buttons = Buttons(on => SetAll(inCategory, on));
                buttons.Margin = new Padding(0);
                header.Controls.Add(buttons);
                flow.Controls.Add(header);
                headers.Add(new KeyValuePair<SqlPrompter.Core.Analysis.RuleCategory, Control>(category, header));
                foreach (var rule in inCategory)
                {
                    var captured = rule;
                    var box = new CheckBox { Text = rule.Code + "   " + rule.Title, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
                    refreshers.Add(() => box.Checked = get(captured));
                    box.CheckedChanged += (s, e) =>
                    {
                        if (!loading)
                        {
                            set(captured, box.Checked);
                            UpdateEnabled();
                        }
                    };
                    if (enabledWhen != null)
                    {
                        enablers.Add(() => box.Enabled = enabledWhen());
                    }

                    var help = new Label
                    {
                        Text = rule.Description + " (" + (rule.Severity == SqlPrompter.Core.Analysis.CodeIssueSeverity.Warning ? "warning" : "hint") + (rule.DefaultOn ? string.Empty : ", off by default") + ")",
                        AutoSize = true,
                        MaximumSize = new Size(TextWidth, 0),
                        ForeColor = SystemColors.GrayText,
                        Margin = new Padding(20, 0, 0, 0),
                    };
                    flow.Controls.Add(box);
                    flow.Controls.Add(help);
                    rows.Add(new KeyValuePair<SqlPrompter.Core.Analysis.AnalysisRule, Control[]>(rule, new Control[] { box, help }));
                }
            }

            filter.TextChanged += (s, e) =>
            {
                var words = filter.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                flow.SuspendLayout();
                foreach (var row in rows)
                {
                    var text = row.Key.Code + " " + row.Key.Title + " " + row.Key.Description;
                    var visible = words.All(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                    foreach (var control in row.Value)
                    {
                        control.Visible = visible;
                    }
                }

                foreach (var header in headers)
                {
                    header.Value.Visible = rows.Any(r => r.Key.Category == header.Key && r.Value[0].Visible);
                }

                flow.ResumeLayout(true);
            };
        }

        /// <summary>Puts a ready-made editor on the page below the heading.</summary>
        public void Host(Control editor, Action load, Action commit, Func<IEnumerable<string>> validate)
        {
            editor.Dock = DockStyle.None;
            editor.Width = TextWidth + 80;
            editor.Height = 520;
            flow.Controls.Add(editor);
            refreshers.Add(load);
            commits.Add(commit);
            validators.Add(validate);
        }

        private void AddRow(string label, Control control, string suffix, string help, Func<bool> enabledWhen, Control toDisable)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            row.Controls.Add(new Label { Text = label, AutoSize = false, Width = 190, Height = 24, TextAlign = ContentAlignment.MiddleLeft });
            row.Controls.Add(control);
            if (suffix != null)
            {
                row.Controls.Add(new Label { Text = suffix, AutoSize = true, Margin = new Padding(6, 5, 0, 0) });
            }

            if (enabledWhen != null && toDisable != null)
            {
                enablers.Add(() => toDisable.Enabled = enabledWhen());
            }

            flow.Controls.Add(row);
            if (!string.IsNullOrEmpty(help))
            {
                Help(help, 0);
            }
        }
    }
}
