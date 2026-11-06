using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SqlPrompter.Core.Coloring;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The editor of the coloring settings: one grid of rules (server, database, environment, and the color the environment gives). The environment
    /// is picked from a dropdown that also offers "New environment..." (name and color in a small dialog); a click on the color of a row changes the
    /// color of its environment for every rule that uses it. Environments no rule uses are dropped when the settings are read back.
    /// </summary>
    internal sealed class ColoringEditor : UserControl
    {
        private const string NewEnvironmentItem = "New environment...";
        private readonly CheckBox enabled = new CheckBox { Text = "Color query windows (tab, frame and status bar) by their server and database", AutoSize = true };
        private readonly DataGridView rules = NewGrid();
        private readonly Label problems = new Label { AutoSize = false, ForeColor = Color.Firebrick, Dock = DockStyle.Fill };
        private readonly DataGridViewComboBoxColumn environmentColumn = new DataGridViewComboBoxColumn { HeaderText = "Environment", Name = "Environment", FlatStyle = FlatStyle.Flat, FillWeight = 30 };
        private readonly List<ColorEnvironment> environments = new List<ColorEnvironment>();
        private readonly Dictionary<DataGridViewRow, string> previous = new Dictionary<DataGridViewRow, string>();
        private bool loading;

        public ColoringEditor()
        {
            Dock = DockStyle.Fill;
            MinimumSize = new Size(520, 340);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(4) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

            layout.Controls.Add(enabled, 0, 0);
            layout.Controls.Add(Heading("Rules: server and database with * and ? wildcards (an empty database means every database). The environment gives the color; " +
                "pick one or choose \"New environment...\", and click the color of a row to change the color of that environment. The most specific rule wins, " +
                "whatever the order: a rule with a database, then one without wildcards, then the longer pattern."), 0, 1);

            rules.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Server", Name = "Server", FillWeight = 32 });
            rules.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Database", Name = "Database", FillWeight = 32 });
            rules.Columns.Add(environmentColumn);
            rules.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Color (click to change)", Name = "Color", ReadOnly = true, FillWeight = 22 });
            rules.DataError += (s, e) => e.ThrowException = false;
            rules.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (rules.IsCurrentCellDirty && rules.CurrentCell is DataGridViewComboBoxCell)
                {
                    rules.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            rules.CellValueChanged += RuleCellValueChanged;
            rules.CellClick += RuleCellClick;
            rules.UserDeletedRow += (s, e) => Revalidate();
            layout.Controls.Add(rules, 0, 2);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            buttons.Controls.Add(Button("Add rule", AddRule));
            buttons.Controls.Add(Button("Remove", RemoveSelected));
            layout.Controls.Add(buttons, 0, 3);
            layout.Controls.Add(problems, 0, 4);
            Controls.Add(layout);

            // The page is hosted in the legacy Options dialog of SSMS 22: stale paint of a child (a bar over the grids) was seen after the first
            // layout, so everything is double buffered and repainted when the page is shown, resized or scaled (#137).
            foreach (var control in new Control[] { this, layout, rules })
            {
                EnableDoubleBuffer(control);
            }

            VisibleChanged += (s, e) => Repaint();
            SizeChanged += (s, e) => Repaint();
            DpiChangedAfterParent += (s, e) => Repaint();
            HandleCreated += (s, e) => BeginInvoke(new Action(Repaint));

            enabled.CheckedChanged += (s, e) => Revalidate();
        }

        public IReadOnlyList<string> CurrentProblems => ColorSettingsValidator.Validate(ToSettings());

        public void ShowSettings(ColorSettings settings)
        {
            loading = true;
            enabled.Checked = settings.Enabled;
            environments.Clear();
            environments.AddRange(settings.Environments.Select(e => new ColorEnvironment { Name = e.Name, Color = e.Color }));
            RefreshChoices(settings.Rules.Select(r => r.Environment));
            rules.Rows.Clear();
            previous.Clear();
            foreach (var rule in settings.Rules)
            {
                var index = rules.Rows.Add(rule.Server, rule.Database, rule.Environment);
                previous[rules.Rows[index]] = rule.Environment;
            }

            loading = false;
            PaintAllColors();
            Revalidate();
        }

        public ColorSettings ToSettings()
        {
            var settings = new ColorSettings { Enabled = enabled.Checked };
            foreach (var environment in environments)
            {
                settings.Environments.Add(new ColorEnvironment
                {
                    Name = (environment.Name ?? string.Empty).Trim(),
                    Color = ColorSettingsValidator.Normalize(environment.Color) ?? environment.Color ?? string.Empty,
                });
            }

            foreach (DataGridViewRow row in rules.Rows)
            {
                if (!row.IsNewRow)
                {
                    settings.Rules.Add(new ColorRule
                    {
                        Server = (Convert.ToString(row.Cells[0].Value) ?? string.Empty).Trim(),
                        Database = (Convert.ToString(row.Cells[1].Value) ?? string.Empty).Trim(),
                        Environment = (Convert.ToString(row.Cells[2].Value) ?? string.Empty).Trim(),
                    });
                }
            }

            return ColorSettingsValidator.WithoutUnusedEnvironments(settings);
        }

        private void AddRule()
        {
            var index = rules.Rows.Add(string.Empty, string.Empty, environments.Count > 0 ? environments[0].Name : string.Empty);
            previous[rules.Rows[index]] = Convert.ToString(rules.Rows[index].Cells[2].Value);
            PaintColor(rules.Rows[index]);
            rules.CurrentCell = rules.Rows[index].Cells[0];
            rules.BeginEdit(true);
        }

        private void RemoveSelected()
        {
            foreach (var row in rules.SelectedRows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList())
            {
                previous.Remove(row);
                rules.Rows.Remove(row);
            }

            Revalidate();
        }

        private void RuleCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || e.RowIndex < 0)
            {
                return;
            }

            var row = rules.Rows[e.RowIndex];
            if (e.ColumnIndex == 2)
            {
                if (string.Equals(Convert.ToString(row.Cells[2].Value), NewEnvironmentItem, StringComparison.Ordinal))
                {
                    // The dropdown is still the editing control of the cell; the dialog and the new value must wait until the edit has ended,
                    // otherwise the control writes its old text back over the new value.
                    BeginInvoke(new Action(() =>
                    {
                        rules.EndEdit();
                        CreateEnvironmentFor(row);
                        PaintColor(row);
                        Revalidate();
                    }));
                    return;
                }
                else
                {
                    previous[row] = Convert.ToString(row.Cells[2].Value);
                }

                PaintColor(row);
            }

            Revalidate();
        }

        /// <summary>"New environment..." was chosen in a rule: ask for a name and a color, add the environment and select it; Cancel goes back to the previous choice.</summary>
        private void CreateEnvironmentFor(DataGridViewRow row)
        {
            previous.TryGetValue(row, out var before);
            using (var dialog = new NewEnvironmentDialog(environments.Select(e => e.Name).ToList()))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    environments.Add(new ColorEnvironment { Name = dialog.EnvironmentName, Color = dialog.EnvironmentColor });
                    RefreshChoices();
                    row.Cells[2].Value = dialog.EnvironmentName;
                    previous[row] = dialog.EnvironmentName;
                }
                else
                {
                    row.Cells[2].Value = before ?? string.Empty;
                }
            }
        }

        private void RuleCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 3 || rules.Rows[e.RowIndex].IsNewRow)
            {
                return;
            }

            var name = (Convert.ToString(rules[2, e.RowIndex].Value) ?? string.Empty).Trim();
            var environment = environments.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (environment == null)
            {
                return;
            }

            using (var dialog = new ColorDialog { FullOpen = true, AnyColor = true })
            {
                if (EnvironmentMatcher.TryParseColor(environment.Color ?? string.Empty, out var r, out var g, out var b))
                {
                    dialog.Color = Color.FromArgb(r, g, b);
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    environment.Color = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");
                    PaintAllColors();
                    Revalidate();
                }
            }
        }

        /// <summary>The dropdown offers the environments and "New environment..."; a name a rule still uses stays in the list so that the cell is not an error (the validation reports it).</summary>
        private void RefreshChoices(IEnumerable<string> extra = null)
        {
            var used = rules.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => Convert.ToString(r.Cells[2].Value));
            var all = environments.Select(e => e.Name).Concat(extra ?? Enumerable.Empty<string>()).Concat(used)
                .Where(n => !string.IsNullOrWhiteSpace(n) && n != NewEnvironmentItem).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            environmentColumn.Items.Clear();
            foreach (var name in all)
            {
                environmentColumn.Items.Add(name);
            }

            environmentColumn.Items.Add(NewEnvironmentItem);
        }

        private void PaintAllColors()
        {
            foreach (DataGridViewRow row in rules.Rows)
            {
                if (!row.IsNewRow)
                {
                    PaintColor(row);
                }
            }
        }

        private void PaintColor(DataGridViewRow row)
        {
            var cell = row.Cells[3];
            var name = (Convert.ToString(row.Cells[2].Value) ?? string.Empty).Trim();
            var environment = environments.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            cell.Value = environment?.Color ?? string.Empty;
            if (environment == null || !EnvironmentMatcher.TryParseColor(environment.Color ?? string.Empty, out var r, out var g, out var b))
            {
                cell.Style.BackColor = Color.Empty;
                cell.Style.ForeColor = Color.Empty;
                cell.Style.SelectionBackColor = Color.Empty;
                cell.Style.SelectionForeColor = Color.Empty;
                return;
            }

            var color = Color.FromArgb(r, g, b);
            cell.Style.BackColor = color;
            cell.Style.SelectionBackColor = color;
            var light = (r * 299 + g * 587 + b * 114) / 1000 > 140;
            cell.Style.ForeColor = light ? Color.Black : Color.White;
            cell.Style.SelectionForeColor = cell.Style.ForeColor;
        }

        private void Revalidate()
        {
            if (loading)
            {
                return;
            }

            var found = CurrentProblems;
            problems.Text = found.Count == 0 ? string.Empty : "Fix before applying:\r\n- " + string.Join("\r\n- ", found.Take(4)) + (found.Count > 4 ? "\r\n- ..." : string.Empty);
        }

        private static void EnableDoubleBuffer(Control control) =>
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(control, true, null);

        private void Repaint()
        {
            PerformLayout();
            Invalidate(true);
            Update();
        }

        private static DataGridView NewGrid() => new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = SystemColors.Window,
            MinimumSize = new Size(300, 90),
        };

        private static Label Heading(string text) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 8, 0, 2) };

        private static System.Windows.Forms.Button Button(string text, Action click)
        {
            var button = new System.Windows.Forms.Button { Text = text, AutoSize = true };
            button.Click += (s, e) => click();
            return button;
        }
    }

    /// <summary>A name and a color for a new environment.</summary>
    internal sealed class NewEnvironmentDialog : Form
    {
        private readonly TextBox name = new TextBox { Width = 260 };
        private readonly System.Windows.Forms.Button colorButton = new System.Windows.Forms.Button { Width = 80, Height = 24 };
        private readonly List<string> existing;
        private string color = "#4C9AFF";

        public NewEnvironmentDialog(List<string> existing)
        {
            this.existing = existing;
            Text = "New environment";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(380, 130);

            var nameLabel = new Label { Text = "Name", Left = 12, Top = 18, Width = 60 };
            name.Left = 80;
            name.Top = 14;
            var colorLabel = new Label { Text = "Color", Left = 12, Top = 54, Width = 60 };
            colorButton.Left = 80;
            colorButton.Top = 50;
            colorButton.FlatStyle = FlatStyle.Flat;
            colorButton.Click += (s, e) => PickColor();
            var ok = new System.Windows.Forms.Button { Text = "OK", Left = 200, Top = 92, Width = 80 };
            var cancel = new System.Windows.Forms.Button { Text = "Cancel", Left = 290, Top = 92, Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => Accept();
            Controls.AddRange(new Control[] { nameLabel, name, colorLabel, colorButton, ok, cancel });
            CancelButton = cancel;
            AcceptButton = ok;
            ShowColor();
        }

        public string EnvironmentName => name.Text.Trim();

        public string EnvironmentColor => color;

        private void Accept()
        {
            if (EnvironmentName.Length == 0 || EnvironmentName == "New environment...")
            {
                MessageBox.Show(this, "Give the environment a name.", "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (existing.Any(e => string.Equals(e, EnvironmentName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "An environment with this name exists already; pick it from the list instead.", "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void PickColor()
        {
            using (var dialog = new ColorDialog { FullOpen = true, AnyColor = true })
            {
                if (EnvironmentMatcher.TryParseColor(color, out var r, out var g, out var b))
                {
                    dialog.Color = Color.FromArgb(r, g, b);
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    color = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");
                    ShowColor();
                }
            }
        }

        private void ShowColor()
        {
            if (EnvironmentMatcher.TryParseColor(color, out var r, out var g, out var b))
            {
                colorButton.BackColor = Color.FromArgb(r, g, b);
            }

            colorButton.Text = color;
            colorButton.ForeColor = (colorButton.BackColor.R * 299 + colorButton.BackColor.G * 587 + colorButton.BackColor.B * 114) / 1000 > 140 ? Color.Black : Color.White;
        }
    }
}
