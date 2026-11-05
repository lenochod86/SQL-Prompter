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
    /// The editor of the coloring settings in Tools → Options: environments as a grid of names and colors (a click on the color opens the color
    /// dialog), rules as a grid of server, database and a dropdown of the environments, and a line that says what is wrong before the page is applied.
    /// </summary>
    internal sealed class ColoringEditor : UserControl
    {
        private readonly CheckBox enabled = new CheckBox { Text = "Color query windows (tab, frame and status bar) by their server and database", AutoSize = true };
        private readonly DataGridView environments = NewGrid();
        private readonly DataGridView rules = NewGrid();
        private readonly Label problems = new Label { AutoSize = false, ForeColor = Color.Firebrick, Dock = DockStyle.Fill };
        private readonly DataGridViewComboBoxColumn environmentColumn = new DataGridViewComboBoxColumn { HeaderText = "Environment", Name = "Environment", FlatStyle = FlatStyle.Flat, FillWeight = 30 };
        private string renamedFrom;
        private bool loading;

        public ColoringEditor()
        {
            Dock = DockStyle.Fill;
            MinimumSize = new Size(520, 420);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(4) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

            layout.Controls.Add(enabled, 0, 0);
            layout.Controls.Add(Heading("Environments (a name and the color its windows get; click the color to change it)"), 0, 1);

            environments.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", Name = "Name", FillWeight = 70 });
            environments.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Color", Name = "Color", ReadOnly = true, FillWeight = 30 });
            environments.CellClick += EnvironmentCellClick;
            environments.CellBeginEdit += (s, e) => renamedFrom = e.ColumnIndex == 0 ? Convert.ToString(environments[e.ColumnIndex, e.RowIndex].Value) : null;
            environments.CellEndEdit += EnvironmentCellEndEdit;
            environments.RowsAdded += (s, e) => { if (!loading) { SetDefaultColor(e.RowIndex); } };
            layout.Controls.Add(environments, 0, 2);
            layout.Controls.Add(Buttons(
                Button("Add environment", () => AddEnvironment()),
                Button("Remove", () => RemoveSelected(environments))), 0, 3);

            layout.Controls.Add(Heading("Rules (server and database with * and ? wildcards; an empty database means every database; a rule with a database wins over a rule for the whole server, otherwise the first match wins)"), 0, 4);
            rules.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Server", Name = "Server", FillWeight = 35 });
            rules.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Database", Name = "Database", FillWeight = 35 });
            rules.Columns.Add(environmentColumn);
            rules.DataError += (s, e) => e.ThrowException = false;
            rules.CellEndEdit += (s, e) => Revalidate();
            rules.UserDeletedRow += (s, e) => Revalidate();
            layout.Controls.Add(rules, 0, 5);
            layout.Controls.Add(Buttons(
                Button("Add rule", () => rules.Rows.Add(string.Empty, string.Empty, FirstEnvironment())),
                Button("Remove", () => RemoveSelected(rules)),
                Button("Move up", () => MoveRule(-1)),
                Button("Move down", () => MoveRule(1))), 0, 6);
            layout.Controls.Add(problems, 0, 7);
            Controls.Add(layout);

            enabled.CheckedChanged += (s, e) => Revalidate();
            environments.UserDeletedRow += (s, e) => { RefreshEnvironmentChoices(); Revalidate(); };
        }

        public IReadOnlyList<string> CurrentProblems => ColorSettingsValidator.Validate(ToSettings());

        public void ShowSettings(ColorSettings settings)
        {
            loading = true;
            enabled.Checked = settings.Enabled;
            environments.Rows.Clear();
            foreach (var environment in settings.Environments)
            {
                var row = environments.Rows.Add(environment.Name, environment.Color);
                PaintColor(environments.Rows[row].Cells[1], environment.Color);
            }

            rules.Rows.Clear();
            RefreshEnvironmentChoices(settings.Rules.Select(r => r.Environment));
            foreach (var rule in settings.Rules)
            {
                rules.Rows.Add(rule.Server, rule.Database, rule.Environment);
            }

            loading = false;
            Revalidate();
        }

        public ColorSettings ToSettings()
        {
            var settings = new ColorSettings { Enabled = enabled.Checked };
            foreach (DataGridViewRow row in environments.Rows)
            {
                if (!row.IsNewRow)
                {
                    settings.Environments.Add(new ColorEnvironment
                    {
                        Name = Convert.ToString(row.Cells[0].Value) ?? string.Empty,
                        Color = ColorSettingsValidator.Normalize(Convert.ToString(row.Cells[1].Value)) ?? Convert.ToString(row.Cells[1].Value) ?? string.Empty,
                    });
                }
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

            foreach (var environment in settings.Environments)
            {
                environment.Name = environment.Name.Trim();
            }

            return settings;
        }

        private void AddEnvironment()
        {
            var row = environments.Rows.Add(string.Empty, "#4C9AFF");
            PaintColor(environments.Rows[row].Cells[1], "#4C9AFF");
            environments.CurrentCell = environments.Rows[row].Cells[0];
            environments.BeginEdit(true);
            RefreshEnvironmentChoices();
        }

        private void SetDefaultColor(int rowIndex)
        {
            if (rowIndex < environments.Rows.Count && environments.Rows[rowIndex].Cells[1].Value == null && !environments.Rows[rowIndex].IsNewRow)
            {
                environments.Rows[rowIndex].Cells[1].Value = "#4C9AFF";
                PaintColor(environments.Rows[rowIndex].Cells[1], "#4C9AFF");
            }
        }

        private void EnvironmentCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1 || environments.Rows[e.RowIndex].IsNewRow)
            {
                return;
            }

            var cell = environments[e.ColumnIndex, e.RowIndex];
            using (var dialog = new ColorDialog { FullOpen = true, AnyColor = true })
            {
                if (EnvironmentMatcher.TryParseColor(Convert.ToString(cell.Value) ?? string.Empty, out var r, out var g, out var b))
                {
                    dialog.Color = Color.FromArgb(r, g, b);
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    var hex = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");
                    cell.Value = hex;
                    PaintColor(cell, hex);
                    Revalidate();
                }
            }
        }

        private void EnvironmentCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 0 && renamedFrom != null)
            {
                var renamedTo = (Convert.ToString(environments[e.ColumnIndex, e.RowIndex].Value) ?? string.Empty).Trim();
                if (renamedFrom.Length > 0 && renamedTo.Length > 0 && renamedFrom != renamedTo)
                {
                    // Rules that pointed to the old name follow the rename.
                    RefreshEnvironmentChoices(new[] { renamedTo });
                    foreach (DataGridViewRow row in rules.Rows)
                    {
                        if (!row.IsNewRow && string.Equals(Convert.ToString(row.Cells[2].Value), renamedFrom, StringComparison.OrdinalIgnoreCase))
                        {
                            row.Cells[2].Value = renamedTo;
                        }
                    }
                }
            }

            renamedFrom = null;
            RefreshEnvironmentChoices();
            Revalidate();
        }

        /// <summary>The dropdown offers the environments; a name a rule still uses stays in the list so that the cell is not an error (the validation reports it).</summary>
        private void RefreshEnvironmentChoices(IEnumerable<string> extra = null)
        {
            var names = environments.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => (Convert.ToString(r.Cells[0].Value) ?? string.Empty).Trim())
                .Where(n => n.Length > 0)
                .ToList();
            var used = rules.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => Convert.ToString(r.Cells[2].Value));
            var all = names.Concat((extra ?? Enumerable.Empty<string>()).Concat(used)).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            environmentColumn.Items.Clear();
            foreach (var name in all)
            {
                environmentColumn.Items.Add(name);
            }
        }

        private string FirstEnvironment() => environmentColumn.Items.Count > 0 ? Convert.ToString(environmentColumn.Items[0]) : string.Empty;

        private void Revalidate()
        {
            if (loading)
            {
                return;
            }

            var found = CurrentProblems;
            problems.Text = found.Count == 0 ? string.Empty : "Fix before applying:\r\n- " + string.Join("\r\n- ", found.Take(4)) + (found.Count > 4 ? "\r\n- ..." : string.Empty);
        }

        private void RemoveSelected(DataGridView grid)
        {
            foreach (var row in grid.SelectedRows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList())
            {
                grid.Rows.Remove(row);
            }

            if (grid == environments)
            {
                RefreshEnvironmentChoices();
            }

            Revalidate();
        }

        private void MoveRule(int direction)
        {
            if (rules.CurrentRow == null || rules.CurrentRow.IsNewRow)
            {
                return;
            }

            var index = rules.CurrentRow.Index;
            var target = index + direction;
            if (target < 0 || target >= rules.Rows.Count || rules.Rows[target].IsNewRow)
            {
                return;
            }

            var values = Enumerable.Range(0, 3).Select(i => rules[i, index].Value).ToArray();
            for (var i = 0; i < 3; i++)
            {
                rules[i, index].Value = rules[i, target].Value;
                rules[i, target].Value = values[i];
            }

            rules.CurrentCell = rules[0, target];
            Revalidate();
        }

        private static void PaintColor(DataGridViewCell cell, string hex)
        {
            if (!EnvironmentMatcher.TryParseColor(hex ?? string.Empty, out var r, out var g, out var b))
            {
                cell.Style.BackColor = Color.Empty;
                cell.Style.ForeColor = Color.Empty;
                return;
            }

            var color = Color.FromArgb(r, g, b);
            cell.Style.BackColor = color;
            cell.Style.SelectionBackColor = color;
            var light = (r * 299 + g * 587 + b * 114) / 1000 > 140;
            cell.Style.ForeColor = light ? Color.Black : Color.White;
            cell.Style.SelectionForeColor = cell.Style.ForeColor;
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

        private static Label Heading(string text) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 8, 0, 2) };

        private static System.Windows.Forms.Button Button(string text, Action click)
        {
            var button = new System.Windows.Forms.Button { Text = text, AutoSize = true };
            button.Click += (s, e) => click();
            return button;
        }

        private static FlowLayoutPanel Buttons(params System.Windows.Forms.Button[] buttons)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            panel.Controls.AddRange(buttons);
            return panel;
        }
    }
}
