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
                ThreadHelper.ThrowIfNotOnUIThread();
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

            ThreadHelper.ThrowIfNotOnUIThread();
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

            var completion = Add("Completion", "When the suggestion list appears and which keys insert the selected item.");
            completion.Check("Enable suggestions", "When off, SQL Prompter stays out of the way and the built-in SSMS list is shown.",
                () => work.Behavior.SuggestionsEnabled, v => work.Behavior.SuggestionsEnabled = v);
            completion.Check("Show suggestions after a comma", "The list opens after a comma and the space that follows it (next column, next item), except in VALUES lists, lists of literals and DECLARE.",
                () => work.Behavior.SuggestAfterComma, v => work.Behavior.SuggestAfterComma = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Show a hint for expanding *", "While the caret is right behind a * in a select list that Tab can expand, a small note under it says \"Tab: expand columns\".",
                () => work.Behavior.ShowStarHint, v => work.Behavior.ShowStarHint = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Show a tooltip over names", "Resting the mouse on an alias, table, view, column, variable, function or procedure says what it is (type, columns, parameters), from the loaded metadata.",
                () => work.Behavior.ShowHover, v => work.Behavior.ShowHover = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Show descriptions from the database", "The description area also shows the MS_Description extended properties of the selected table, view, procedure or function and of its columns (read in the background, nothing is shown when there are none).",
                () => work.Behavior.ShowDbDescriptions, v => work.Behavior.ShowDbDescriptions = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Mark items with \"SQL Prompter\"", "Shows \"SQL Prompter\" on the right of every item so that it is obvious the list is ours and not the built-in one.",
                () => work.Behavior.MarkItems, v => work.Behavior.MarkItems = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Match initials and loose characters", "Besides a prefix or a part of the name the typed text may pick the capital letters or the words of a name (BKT finds BurzaKlientTyp, kt finds Burza_Klient_Typ) or its characters in order (bzkl). Exact and prefix matches stay first, loose matches last.",
                () => work.Behavior.LooseMatching, v => work.Behavior.LooseMatching = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Rank by usage", "Items you choose more often come first in their group. The counts are kept on this computer only (%APPDATA%\\SqlPrompter\\usage.json); reset them with SQL Prompter > Reset Usage Statistics.",
                () => work.Behavior.RankByUsage, v => work.Behavior.RankByUsage = v, () => work.Behavior.SuggestionsEnabled);
            completion.Choice("Keyword case", "How keywords are inserted. \"Match what I typed\" inserts upper case, or lower case if you started typing in lower case.",
                new[] { O(KeywordCase.MatchTyped, "Match what I typed"), O(KeywordCase.Upper, "UPPER CASE"), O(KeywordCase.Lower, "lower case") },
                () => work.Behavior.KeywordCase, v => work.Behavior.KeywordCase = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Show the column picker hint", "A line under the list that says Ctrl+Left switches to the column picker (Ctrl+Space opens the list).",
                () => work.Behavior.ShowColumnPickerHint, v => work.Behavior.ShowColumnPickerHint = v, () => work.Behavior.SuggestionsEnabled);
            completion.Check("Run the list myself while typing (experimental)", "SQL Prompter shows and drives the list that opens while you type, the same list as Ctrl+Space, instead of the editor's completion session. Turn it off if typing misbehaves.",
                () => work.Behavior.OwnTypingList, v => work.Behavior.OwnTypingList = v, () => work.Behavior.SuggestionsEnabled);
            completion.Button("Reset usage statistics...", () => SqlPrompterPackage.ResetUsage(this), "Forgets which suggestions you chose how often; the list goes back to its plain order.");
            completion.Heading("Size of the list");
            completion.Help("Drag the triangle in the bottom-right corner of the list to resize it; the size is kept. Here you can type it or go back to the default.");
            completion.Number("Rows", "How many suggestions are visible at once.", 4, 40, () => work.Behavior.ListRows, v => work.Behavior.ListRows = v, "rows", () => work.Behavior.SuggestionsEnabled);
            completion.Number("Width", "0 makes the list as wide as its longest item.", 0, 1600, () => work.Behavior.ListWidth, v => work.Behavior.ListWidth = v, "pixels", () => work.Behavior.SuggestionsEnabled);
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
            snippets.Folder("Shared snippet folders", "Folders with snippet files that are read in addition to the personal folder, separated by semicolons (a folder in a git repository or on a network share). A personal snippet with the same prefix wins. The folders are watched for changes.",
                () => work.Snippets.SharedFolders, v => work.Snippets.SharedFolders = v ?? string.Empty);
            snippets.Check("Install the built-in snippets", "Puts the snippets that ship with SQL Prompter (top100, upd, merge, cp, tran and more) into the snippet folder, each one once. Snippets you edit or delete are not touched again.",
                () => work.Snippets.InstallBuiltIn, v => work.Snippets.InstallBuiltIn = v);
            snippets.Button("Edit snippets...", () =>
            {
                // The manager reads the folder of the saved settings, so a changed folder is applied first.
                ThreadHelper.ThrowIfNotOnUIThread();
                if (Apply())
                {
                    SqlPrompterPackage.OpenSnippetManager();
                }
            }, "Opens the snippet manager (the same window as SQL Prompter > Manage Snippets). The settings are applied first.");

            var analysis = Add("Code analysis", "Underlines suspicious code in the editor; the tooltip says what is wrong. The check runs in the background a moment after you stop typing.");
            analysis.Check("Mark problems in the editor", "A wavy line: orange for warnings, gray for hints.", () => work.Analysis.Enabled, v => work.Analysis.Enabled = v);
            Func<bool> checks = () => work.Analysis.Enabled;
            analysis.Heading("Warnings");
            analysis.Check("@@IDENTITY", "@@IDENTITY also returns identity values generated by triggers; use SCOPE_IDENTITY(). SQL Prompter > Fix Code Analysis Issues repairs it.", () => work.Analysis.AtAtIdentity, v => work.Analysis.AtAtIdentity = v, checks, 1);
            analysis.Check("Type without a length", "varchar, nvarchar, char ... without a length is 1 character in a declaration and 30 in CAST and CONVERT. Fix Code Analysis Issues adds (100), or (10) for char and nchar.", () => work.Analysis.TypeWithoutLength, v => work.Analysis.TypeWithoutLength = v, checks, 1);
            analysis.Check("Comparison with NULL", "= NULL, <> NULL and != NULL in a condition are never true; use IS NULL or IS NOT NULL (Fix Code Analysis Issues repairs it).", () => work.Analysis.EqualsNull, v => work.Analysis.EqualsNull = v, checks, 1);
            analysis.Check("JOIN without ON", "A JOIN (other than CROSS JOIN) without an ON condition combines every row with every row.", () => work.Analysis.JoinWithoutOn, v => work.Analysis.JoinWithoutOn = v, checks, 1);
            analysis.Check("Unexpected end", "A script that stops in the middle of a construct: an unclosed parenthesis, string or bracketed name, a CTE that no statement follows, BEGIN or CASE without END, a batch that ends with FROM, WHERE, AND, a comma or an operator. Checked at the end of every batch (before GO and at the end of the script).",
                () => work.Analysis.UnexpectedEnd, v => work.Analysis.UnexpectedEnd = v, checks, 1);
            analysis.Heading("Hints");
            analysis.Check("Function on a column in a condition", "WHERE YEAR(d) = 2025 or UPPER(c) = 'X': the index on the column cannot be used.", () => work.Analysis.FunctionOnColumn, v => work.Analysis.FunctionOnColumn = v, checks, 1);
            analysis.Check("LIKE with a leading %", "LIKE '%text' cannot use an index.", () => work.Analysis.LeadingWildcard, v => work.Analysis.LeadingWildcard = v, checks, 1);
            analysis.Check("TOP without ORDER BY", "SELECT TOP n without ORDER BY returns arbitrary rows (not inside EXISTS or a UNION).", () => work.Analysis.TopWithoutOrderBy, v => work.Analysis.TopWithoutOrderBy = v, checks, 1);
            analysis.Check("NOT IN with a subquery", "NOT IN (SELECT ...) returns no rows when the subquery returns a NULL; NOT EXISTS does not.", () => work.Analysis.NotInSubquery, v => work.Analysis.NotInSubquery = v, checks, 1);
            analysis.Check("Old-style join", "FROM a, b WHERE ... joins the tables in the WHERE clause; JOIN ... ON says what is meant.", () => work.Analysis.OldStyleJoin, v => work.Analysis.OldStyleJoin = v, checks, 1);
            analysis.Check("INSERT without a column list", "INSERT INTO table VALUES (...) or SELECT depends on the order of the table's columns and breaks when one is added.", () => work.Analysis.InsertWithoutColumns, v => work.Analysis.InsertWithoutColumns = v, checks, 1);
            analysis.Check("Unused variable", "A variable declared with DECLARE that the batch never mentions again.", () => work.Analysis.UnusedVariable, v => work.Analysis.UnusedVariable = v, checks, 1);
            analysis.Check("SELECT *", "SELECT * and alias.* in a select list (not COUNT(*) and not EXISTS (SELECT *)). Off by default because it is everyday in ad-hoc queries.", () => work.Analysis.SelectStar, v => work.Analysis.SelectStar = v, checks, 1);
            analysis.Check("NOLOCK and READUNCOMMITTED", "Lock hints that can return uncommitted, missing or duplicated rows. Off by default.", () => work.Analysis.LockHint, v => work.Analysis.LockHint = v, checks, 1);

            var formatting = Add("Formatting", "Used by Format Document and Format Selection, and by the lists and keywords SQL Prompter inserts.");
            formatting.Info(() => "Active style: " + work.ActiveStyle.Name + "   (" + work.FormattingStyles.Count + (work.FormattingStyles.Count == 1 ? " style)" : " styles)"));
            formatting.Button("Edit formatting styles...", () =>
            {
                // The styles window starts from the saved settings and saves them itself, so the changes of this window are applied first.
                ThreadHelper.ThrowIfNotOnUIThread();
                if (Apply())
                {
                    FormattingStylesWindow.Open();
                    Imported();
                }
            }, "Several named styles with one active, a tree of the options with a search box and a preview of a sample for every group (or of your current query).");
            formatting.Button("Import a SQL Prompt style...", () => { if (SettingsFiles.ImportFormatStyle(this)) { Imported(); } }, "Reads a style exported from SQL Prompt (.json) and applies the options this formatter has to the active style.");
            var coloringPage = Add("Coloring", "Colors the tab, frame and status bar of every query window by its own server and database.");
            var coloring = new ColoringEditor();
            coloringPage.Host(coloring, () => coloring.ShowSettings(work.Coloring), () => work.Coloring = coloring.ToSettings(), () => coloring.CurrentProblems);

            var history = Add("History and tabs", "Everything stays on this computer and is never sent anywhere.");
            history.Check("Keep query history", "Every executed query is written to %APPDATA%\\SqlPrompter\\history.jsonl. Open it with SQL Prompter > Query History.",
                () => work.Behavior.KeepHistory, v => work.Behavior.KeepHistory = v);
            history.Number("Keep history for", "History older than this is removed when SSMS starts. 0 keeps it forever.", 0, 36500,
                () => work.Behavior.HistoryDays, v => work.Behavior.HistoryDays = v, "days", () => work.Behavior.KeepHistory);
            history.Number("Closed tabs to remember", "How many closed query tabs Reopen Closed Query Tab (and the quick actions) can bring back. The list lives until SSMS ends.", 1, 200,
                () => work.Behavior.ClosedTabsKept, v => work.Behavior.ClosedTabsKept = v, "tabs");
            history.Number("Remember a closed tab for", "A closed query tab can be reopened for this many hours. 0 keeps it until SSMS ends.", 0, 8760,
                () => work.Behavior.ClosedTabsHours, v => work.Behavior.ClosedTabsHours = v, "hours");
            history.Check("Restore query tabs at start", "The open query tabs, with their text and connection, are saved every few seconds to %APPDATA%\\SqlPrompter\\tabs.json and opened again when SSMS starts. " +
                "Windows authentication reconnects; a tab with a SQL login opens unconnected (passwords are never stored) with a comment naming the server and database.",
                () => work.Behavior.RestoreTabs, v => work.Behavior.RestoreTabs = v);

            var warnings = Add("Warnings", "Asks for confirmation before a script runs.");
            warnings.Check("Warn about DELETE without WHERE", "Running a script with a DELETE that has no WHERE (and no TOP, and does not clear a temporary table or table variable) asks for confirmation first.",
                () => work.Behavior.WarnDeleteWithoutWhere, v => work.Behavior.WarnDeleteWithoutWhere = v);
            warnings.Check("Warn about UPDATE without WHERE", "Running a script with an UPDATE that has no WHERE (and no TOP, and does not change a temporary table or table variable) asks for confirmation first.",
                () => work.Behavior.WarnUpdateWithoutWhere, v => work.Behavior.WarnUpdateWithoutWhere = v);
            warnings.Check("Warn about an open transaction", "Running a script with a BEGIN TRAN that has no COMMIT or ROLLBACK in the same batch asks for confirmation first.",
                () => work.Behavior.WarnOpenTransaction, v => work.Behavior.WarnOpenTransaction = v);

            var editor = Add("Editor helpers");
            editor.Check("Auto-close quotes, brackets and parentheses", "Typing ' [ or ( also inserts the closing character; typing it again steps over it, and Backspace between an empty pair removes both. Nothing is closed inside strings, comments and bracketed names.",
                () => work.Behavior.AutoClose, v => work.Behavior.AutoClose = v);
            editor.Check("Quick actions on a tap of Ctrl", "Pressing and releasing the Ctrl key on its own opens a popup at the left of the editor with the actions of the SQL Prompter menu and the snippets that fit (type to filter, Enter runs, Esc closes). Ctrl together with another key or the mouse does not open it.",
                () => work.Behavior.QuickActionsOnCtrl, v => work.Behavior.QuickActionsOnCtrl = v);
            editor.Check("Highlight matching names", "Highlights the other places of the script where the name under the caret appears (compared by name, ignoring case and brackets).",
                () => work.Behavior.HighlightMatches, v => work.Behavior.HighlightMatches = v);

            var files = Add("Import / Export", "Move the settings to another computer or take over the settings of SQL Prompt.");
            files.Button("Export settings...", () => SettingsFiles.Export(this), "Saves the settings file (SQL Prompter .json). Unsaved changes in this window are not included; press Apply first.");
            files.Button("Import settings...", () => { if (SettingsFiles.Import(this)) { Imported(); } }, "Reads a SQL Prompter .json or a SQL Prompt .settings file and applies what this extension supports. Unsaved changes in this window are dropped.");
        }
    }
}
