using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SqlPrompter.Core.Formatting;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The settings window (SQL Prompter > Settings): groups in a list on the left, the options of the chosen group on the right. It edits a working copy of
    /// the settings; OK and Apply write it to the settings file, Cancel drops it.
    /// </summary>
    internal sealed class SettingsWindow : Form
    {
        private readonly ListBox groups = new ListBox { Dock = DockStyle.Left, Width = 190, IntegralHeight = false, BorderStyle = BorderStyle.None };
        private readonly Panel host = new Panel { Dock = DockStyle.Fill };
        private readonly List<SettingsPage> pages = new List<SettingsPage>();
        private PrompterSettings work = SettingsService.Current.Clone();

        public SettingsWindow()
        {
            Text = "SQL Prompter Settings";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;
            Size = new Size(960, 720);
            MinimumSize = new Size(760, 520);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 8, 8, 0) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            var apply = new Button { Text = "Apply", Width = 90 };
            var ok = new Button { Text = "OK", Width = 90 };
            apply.Click += (s, e) => Apply();
            ok.Click += (s, e) =>
            {
                if (Apply())
                {
                    DialogResult = DialogResult.OK;
                }
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(apply);
            buttons.Controls.Add(ok);
            AcceptButton = null;
            CancelButton = cancel;

            BuildPages();
            foreach (var page in pages)
            {
                groups.Items.Add(page.Title);
            }

            groups.SelectedIndexChanged += (s, e) => ShowGroup(groups.SelectedIndex);
            Controls.Add(host);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = SystemColors.ControlDark });
            Controls.Add(groups);
            Controls.Add(buttons);
            ReloadPages();
            groups.SelectedIndex = 0;
        }

        private void ShowGroup(int index)
        {
            if (index < 0)
            {
                return;
            }

            host.Controls.Clear();
            host.Controls.Add(pages[index]);
        }

        private void ReloadPages()
        {
            foreach (var page in pages)
            {
                page.Reload();
            }
        }

        /// <summary>Writes the working copy to the settings file. Returns false (and says why) when a page is not valid.</summary>
        private bool Apply()
        {
            foreach (var page in pages)
            {
                page.Commit();
                var problems = page.Problems().ToList();
                if (problems.Count > 0)
                {
                    groups.SelectedIndex = pages.IndexOf(page);
                    MessageBox.Show(this, "The settings of \"" + page.Title + "\" are not valid:\n- " + string.Join("\n- ", problems), "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            SettingsService.Update(work);
            return true;
        }

        /// <summary>After an import the settings file has changed under the window; show the new values (unsaved edits are dropped).</summary>
        private void Imported()
        {
            work = SettingsService.Current.Clone();
            ReloadPages();
        }

        private static KeyValuePair<T, string> O<T>(T key, string text) => new KeyValuePair<T, string>(key, text);

        private SettingsPage Add(string title, string intro = null)
        {
            var page = new SettingsPage(title, intro);
            pages.Add(page);
            return page;
        }

        private void BuildPages()
        {
            var caseStyles = new[] { O(CaseStyle.Keep, "Keep as typed"), O(CaseStyle.Upper, "UPPER CASE"), O(CaseStyle.Lower, "lower case") };

            var completion = Add("Completion", "When the suggestion list appears and which keys insert the selected item.");
            completion.Check("Enable suggestions", "When off, SQL Prompter stays out of the way and the built-in SSMS list is shown.",
                () => work.Behavior.SuggestionsEnabled, v => work.Behavior.SuggestionsEnabled = v);
            completion.Check("Show suggestions after a comma", "The list opens after a comma and the space that follows it (next column, next item), except in VALUES lists, lists of literals and DECLARE.",
                () => work.Behavior.SuggestAfterComma, v => work.Behavior.SuggestAfterComma = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Show a hint for expanding *", "While the caret is right behind a * in a select list that Tab can expand, a small note under it says \"Tab: expand columns\".",
                () => work.Behavior.ShowStarHint, v => work.Behavior.ShowStarHint = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Mark items with \"SQL Prompter\"", "Shows \"SQL Prompter\" on the right of every item so that it is obvious the list is ours and not the built-in one.",
                () => work.Behavior.MarkItems, v => work.Behavior.MarkItems = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Rank by usage", "Items you choose more often come first in their group. The counts are kept on this computer only (%APPDATA%\\SqlPrompter\\usage.json); reset them with SQL Prompter > Reset Usage Statistics.",
                () => work.Behavior.RankByUsage, v => work.Behavior.RankByUsage = v, () => work.Behavior.SuggestionsEnabled);
            completion.Choice("Keyword case", "How keywords are inserted. \"Match what I typed\" inserts upper case, or lower case if you started typing in lower case.",
                new[] { O(KeywordCase.MatchTyped, "Match what I typed"), O(KeywordCase.Upper, "UPPER CASE"), O(KeywordCase.Lower, "lower case") },
                () => work.Behavior.KeywordCase, v => work.Behavior.KeywordCase = v, () => work.Behavior.SuggestionsEnabled);
            completion.Heading("Insert the selected suggestion on");
            completion.Help("Enter and Tab always insert the selected suggestion. These keys insert it too, and are then typed as usual.");
            Func<bool> on = () => work.Behavior.SuggestionsEnabled;
            completion.Check("Space", null, () => work.Behavior.CommitOnSpace, v => work.Behavior.CommitOnSpace = v, on);
            completion.Check("Dot", null, () => work.Behavior.CommitOnDot, v => work.Behavior.CommitOnDot = v, on);
            completion.Check("Comma", null, () => work.Behavior.CommitOnComma, v => work.Behavior.CommitOnComma = v, on);
            completion.Check("Parentheses", null, () => work.Behavior.CommitOnParentheses, v => work.Behavior.CommitOnParentheses = v, on);
            completion.Check("Semicolon", null, () => work.Behavior.CommitOnSemicolon, v => work.Behavior.CommitOnSemicolon = v, on);
            completion.Check("Closing bracket ]", null, () => work.Behavior.CommitOnClosingBracket, v => work.Behavior.CommitOnClosingBracket = v, on);

            var aliases = Add("Aliases and qualification", "How tables and columns are inserted.");
            aliases.Heading("Aliases");
            aliases.Check("Add aliases to tables", "Inserts an alias with a table or view chosen after FROM or JOIN (OrderItems → oi).",
                () => work.Aliases.AddAliases, v => work.Aliases.AddAliases = v);
            aliases.Check("Include AS", "Inserts \"AS oi\" instead of \"oi\".", () => work.Aliases.IncludeAs, v => work.Aliases.IncludeAs = v, () => work.Aliases.AddAliases, 1);
            aliases.Check("Capitalise generated aliases", null, () => work.Aliases.CapitaliseGeneratedAliases, v => work.Aliases.CapitaliseGeneratedAliases = v, () => work.Aliases.AddAliases, 1);
            aliases.Heading("Qualification");
            aliases.Check("Qualify objects with schema", "Inserts tables and views as schema.name.", () => work.Aliases.QualifyObjectNames, v => work.Aliases.QualifyObjectNames = v);
            aliases.Check("Qualify columns with alias", "Inserts columns as alias.column. Columns that exist in several tables of the query are always qualified.",
                () => work.Aliases.QualifyColumnsWithAliases, v => work.Aliases.QualifyColumnsWithAliases = v);
            aliases.Table("Custom aliases", "Custom aliases take precedence over generated ones, e.g. OrderItems → oit.", new[] { "Table or view", "Alias" },
                () => work.Aliases.CustomAliases.Select(r => new[] { r.Name, r.Alias }),
                rows => work.Aliases.CustomAliases = rows.Select(r => new AliasRule { Name = r[0], Alias = r[1] }).ToList());
            aliases.Table("Ignored prefixes", "Prefixes dropped from a table name before the alias is generated (e.g. tbl_).", new[] { "Prefix" },
                () => work.Aliases.IgnoredPrefixes.Select(p => new[] { p }),
                rows => work.Aliases.IgnoredPrefixes = rows.Select(r => r[0]).ToList());

            var inserts = Add("Inserts and joins", "What INSERT statements, EXEC calls and JOIN conditions look like when inserted from the list.");
            inserts.Heading("INSERT");
            inserts.Choice("Column list", "Identity, computed and rowversion columns are left out.",
                new[]
                {
                    O(InsertColumnsMode.Automatic, "Insert the table with its column list"),
                    O(InsertColumnsMode.Item, "Offer an extra item \"table (columns)\""),
                    O(InsertColumnsMode.AfterParenthesis, "Offer the list after typing ( behind the table"),
                },
                () => work.Inserts.ColumnsMode, v => work.Inserts.ColumnsMode = v);
            inserts.Choice("Layout of the lists", "Used for column lists, VALUES and EXEC parameters.",
                new[] { O(ListLayout.OneLine, "All on one line"), O(ListLayout.EachOnItsOwnLine, "Each on its own line"), O(ListLayout.FitWidth, "As many on a line as fit the editor") },
                () => work.Inserts.Layout, v => work.Inserts.Layout = v);
            inserts.Choice("After the column list", "VALUES gets a placeholder for each column ('' for text, N'' for Unicode text, 0 for numbers, NULL for nullable columns).",
                new[] { O(InsertValuesMode.None, "A new line only"), O(InsertValuesMode.Values, "VALUES with a placeholder per column") },
                () => work.Inserts.ValuesMode, v => work.Inserts.ValuesMode = v);
            inserts.Heading("JOIN conditions");
            inserts.Check("New table on the left", "o.IdUser = u.IdUser instead of u.IdUser = o.IdUser (o is the table being joined).",
                () => work.Joins.NewTableOnLeft, v => work.Joins.NewTableOnLeft = v);
            inserts.Check("Foreign keys", "Offer the condition of a foreign key (the columns it references).",
                () => work.Joins.ForeignKeyConditions, v => work.Joins.ForeignKeyConditions = v);
            inserts.Check("Single columns of multi-column keys", "Besides the whole condition of a foreign key over several columns, offer each column pair on its own.",
                () => work.Joins.IncludePartialKeyConditions, v => work.Joins.IncludePartialKeyConditions = v, () => work.Joins.ForeignKeyConditions, 1);
            inserts.Check("Columns with the same name", "Offer a condition for every column name that both tables have, besides the foreign keys.",
                () => work.Joins.NameMatchedConditions, v => work.Joins.NameMatchedConditions = v);
            inserts.Check("Same name needs the same type", "A condition by column name is offered only when both columns have the same type.",
                () => work.Joins.RequireSameTypeForNameMatches, v => work.Joins.RequireSameTypeForNameMatches = v, () => work.Joins.NameMatchedConditions, 1);

            var snippets = Add("Snippets");
            snippets.Folder("Snippet folder", "Folder with the snippet files, one JSON file per snippet as in SQL Prompt's snippet folder. Empty uses %APPDATA%\\SqlPrompter\\Snippets.",
                () => work.Snippets.Folder, v => work.Snippets.Folder = v ?? string.Empty);

            var analysis = Add("Code analysis", "Underlines suspicious code in the editor; the tooltip says what is wrong. The check runs in the background a moment after you stop typing.");
            analysis.Check("Mark problems in the editor", "A wavy line: orange for warnings, gray for hints.", () => work.Analysis.Enabled, v => work.Analysis.Enabled = v);
            Func<bool> checks = () => work.Analysis.Enabled;
            analysis.Heading("Warnings");
            analysis.Check("Comparison with NULL", "= NULL, <> NULL and != NULL in a condition are never true; use IS NULL or IS NOT NULL.", () => work.Analysis.EqualsNull, v => work.Analysis.EqualsNull = v, checks, 1);
            analysis.Check("JOIN without ON", "A JOIN (other than CROSS JOIN) without an ON condition combines every row with every row.", () => work.Analysis.JoinWithoutOn, v => work.Analysis.JoinWithoutOn = v, checks, 1);
            analysis.Heading("Hints");
            analysis.Check("INSERT without a column list", "INSERT INTO table VALUES (...) or SELECT depends on the order of the table's columns and breaks when one is added.", () => work.Analysis.InsertWithoutColumns, v => work.Analysis.InsertWithoutColumns = v, checks, 1);
            analysis.Check("Unused variable", "A variable declared with DECLARE that the batch never mentions again.", () => work.Analysis.UnusedVariable, v => work.Analysis.UnusedVariable = v, checks, 1);
            analysis.Check("SELECT *", "SELECT * and alias.* in a select list (not COUNT(*) and not EXISTS (SELECT *)). Off by default because it is everyday in ad-hoc queries.", () => work.Analysis.SelectStar, v => work.Analysis.SelectStar = v, checks, 1);
            analysis.Check("NOLOCK and READUNCOMMITTED", "Lock hints that can return uncommitted, missing or duplicated rows. Off by default.", () => work.Analysis.LockHint, v => work.Analysis.LockHint = v, checks, 1);

            var formatting = Add("Formatting", "Used by Format Document and Format Selection, and by the lists and keywords SQL Prompter inserts.");
            formatting.Button("Import a SQL Prompt style...", () => { if (SettingsFiles.ImportFormatStyle(this)) { Imported(); } }, "Reads a style exported from SQL Prompt (.json) and applies the options this formatter has.");
            formatting.Heading("Casing");
            formatting.Choice("Keywords", "SELECT, FROM, WHERE and the other reserved words.", caseStyles, () => work.Formatting.KeywordCase, v => work.Formatting.KeywordCase = v);
            formatting.Choice("Built-in functions", "COUNT, ISNULL, DATEADD and so on (only when followed by a parenthesis).", caseStyles, () => work.Formatting.FunctionCase, v => work.Formatting.FunctionCase = v);
            formatting.Choice("Data types", "int, varchar and so on where a type is expected (after a variable, in CAST and CONVERT, in table definitions).", caseStyles, () => work.Formatting.DataTypeCase, v => work.Formatting.DataTypeCase = v);
            formatting.Check("Case keywords while typing", "A reserved word you type gets the keyword case as soon as you type a space, parenthesis, comma or semicolon after it. Off by default.",
                () => work.Formatting.CaseKeywordsWhileTyping, v => work.Formatting.CaseKeywordsWhileTyping = v);
            formatting.Heading("Layout");
            formatting.Check("Use tabs", "Indentation and alignment are made of tabs where a whole tab fits.", () => work.Formatting.UseTabs, v => work.Formatting.UseTabs = v);
            formatting.Number("Tab width", "The width of one indent level.", 1, 16, () => work.Formatting.TabWidth, v => work.Formatting.TabWidth = v, "characters");
            formatting.Check("Commas before items", "Lists put the comma at the start of the next line (, item) instead of the end of the line.", () => work.Formatting.CommaBeforeItems, v => work.Formatting.CommaBeforeItems = v);
            formatting.Check("AND / OR start the line", "Conditions on several lines begin with AND or OR instead of ending with them.", () => work.Formatting.AndOrBeforeConditions, v => work.Formatting.AndOrBeforeConditions = v);
            formatting.Check("Conditions on separate lines", "The conditions of WHERE and HAVING each get a line when there are several.", () => work.Formatting.ConditionsOnSeparateLines, v => work.Formatting.ConditionsOnSeparateLines = v);
            formatting.Check("Line break after DISTINCT and TOP", "SELECT TOP 10 / SELECT DISTINCT end the line; the first item starts the next one.", () => work.Formatting.NewLineAfterDistinctAndTop, v => work.Formatting.NewLineAfterDistinctAndTop = v);
            formatting.Check("Indent BEGIN ... END", "The statements between BEGIN and END are indented.", () => work.Formatting.IndentBlocks, v => work.Formatting.IndentBlocks = v);
            formatting.Check("Keep one blank line", "A blank line between statements stays (as one line); otherwise blank lines are removed.", () => work.Formatting.KeepBlankLines, v => work.Formatting.KeepBlankLines = v);
            formatting.Heading("Short statements");
            formatting.Number("Keep statements shorter than", "A SELECT, INSERT, UPDATE or DELETE that fits in fewer characters stays on one line. 0 turns it off.", 0, 1000, () => work.Formatting.CollapseStatementsShorterThan, v => work.Formatting.CollapseStatementsShorterThan = v, "characters");
            formatting.Number("Keep subqueries shorter than", "A subquery or CTE that fits in fewer characters stays on the line of its item. 0 turns it off.", 0, 1000, () => work.Formatting.CollapseSubqueriesShorterThan, v => work.Formatting.CollapseSubqueriesShorterThan = v, "characters");
            formatting.Number("Keep CREATE TABLE shorter than", "A CREATE TABLE that fits in fewer characters stays on one line. 0 turns it off.", 0, 1000, () => work.Formatting.CollapseDdlShorterThan, v => work.Formatting.CollapseDdlShorterThan = v, "characters");

            var coloringPage = Add("Coloring", "Colors the tab, frame and status bar of a query window by its server and database.");
            var coloring = new ColoringEditor();
            coloringPage.Host(coloring, () => coloring.ShowSettings(work.Coloring), () => work.Coloring = coloring.ToSettings(), () => coloring.CurrentProblems);

            var history = Add("History and tabs", "Everything stays on this computer and is never sent anywhere.");
            history.Check("Keep query history", "Every executed query is written to %APPDATA%\\SqlPrompter\\history.jsonl. Open it with SQL Prompter > Query History.",
                () => work.Behavior.KeepHistory, v => work.Behavior.KeepHistory = v);
            history.Number("Keep history for", "History older than this is removed when SSMS starts. 0 keeps it forever.", 0, 36500,
                () => work.Behavior.HistoryDays, v => work.Behavior.HistoryDays = v, "days", () => work.Behavior.KeepHistory);
            history.Check("Restore query tabs at start", "The open query tabs, with their text and connection, are saved every few seconds to %APPDATA%\\SqlPrompter\\tabs.json and opened again when SSMS starts. " +
                "Windows authentication reconnects; a tab with a SQL login opens unconnected (passwords are never stored) with a comment naming the server and database.",
                () => work.Behavior.RestoreTabs, v => work.Behavior.RestoreTabs = v);

            var warnings = Add("Warnings", "Asks for confirmation before a script runs.");
            warnings.Check("Warn about DELETE without WHERE", "Running a script with a DELETE that has no WHERE (and no TOP, and does not clear a temporary table or table variable) asks for confirmation first.",
                () => work.Behavior.WarnDeleteWithoutWhere, v => work.Behavior.WarnDeleteWithoutWhere = v);
            warnings.Check("Warn about UPDATE without WHERE", "Running a script with an UPDATE that has no WHERE (and no TOP, and does not change a temporary table or table variable) asks for confirmation first.",
                () => work.Behavior.WarnUpdateWithoutWhere, v => work.Behavior.WarnUpdateWithoutWhere = v);

            var editor = Add("Editor helpers");
            editor.Check("Auto-close quotes, brackets and parentheses", "Typing ' [ or ( also inserts the closing character; typing it again steps over it, and Backspace between an empty pair removes both. Nothing is closed inside strings, comments and bracketed names.",
                () => work.Behavior.AutoClose, v => work.Behavior.AutoClose = v);
            editor.Check("Highlight matching names", "Highlights the other places of the script where the name under the caret appears (compared by name, ignoring case and brackets).",
                () => work.Behavior.HighlightMatches, v => work.Behavior.HighlightMatches = v);

            var files = Add("Import / Export", "Move the settings to another computer or take over the settings of SQL Prompt.");
            files.Button("Export settings...", () => SettingsFiles.Export(this), "Saves the settings file (SQL Prompter .json). Unsaved changes in this window are not included; press Apply first.");
            files.Button("Import settings...", () => { if (SettingsFiles.Import(this)) { Imported(); } }, "Reads a SQL Prompter .json or a SQL Prompt .settings file and applies what this extension supports. Unsaved changes in this window are dropped.");
        }
    }
}
