using System;
using System.Linq;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace SqlPrompter.Ssms
{
    /// <summary>Commands of the SQL Prompter menu. Completion itself runs through MEF.</summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    // The second parameter is the menu version. Bump it after every .vsct change, otherwise SSMS uses cached menus.
    [ProvideMenuResource("Menus.ctmenu", 47)]
    [ProvideOptionPage(typeof(OptionsLauncherPage), "SQL Prompter", "General", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(HistoryToolWindowPane), Style = VsDockStyle.MDI, Transient = true)]
    public sealed class SqlPrompterPackage : AsyncPackage
    {
        public const string PackageGuidString = "a3a4db19-c58e-4eac-8a84-ba61357b9b01";
        public static readonly Guid CommandSet = new Guid("15e19ce9-b3c9-47dd-97f9-49fe13869730");
        public const int RefreshMetadataId = 0x0100;
        public const int OpenLogId = 0x0101;
        public const int SettingsId = 0x011F;
        public const int FormattingStylesId = 0x0120;
        public const int EditEnvironmentsId = 0x0134;
        public const int ShowSuggestionsId = 0x0121;
        public const int PickerLeftId = 0x0122;
        public const int PickerRightId = 0x0123;
        public const int FormatDocumentId = 0x011A;
        public const int FormatSelectionId = 0x011B;
        public const int UnformatSelectionId = 0x0136;
        public const int AddNPrefixId = 0x011C;
        public const int AddAsId = 0x011D;
        public const int RemoveAsId = 0x011E;
        public const int SurroundWithSnippetId = 0x0107;
        public const int NewSnippetFromSelectionId = 0x0108;
        public const int ManageSnippetsId = 0x0109;
        public const int GoToDefinitionId = 0x010B;
        public const int LocateInObjectExplorerId = 0x0133;
        public const int RunStatementId = 0x010C;
        public const int RunStatementsUpToCaretId = 0x0135;
        public const int ParameterInfoId = 0x010D;
        public const int AddCommasId = 0x010F;
        public const int AddQuotesAndCommasId = 0x0110;
        public const int QueryHistoryId = 0x0111;
        public const int QueryHistoryToolbarId = 0x0131;
        public const int ScriptGridAsInsertId = 0x0112;
        public const int RemoveBracketsId = 0x0113;
        public const int InsertSemicolonsId = 0x0114;
        public const int RemoveUnusedVariablesId = 0x0115;
        public const int ExpandStarsId = 0x0116;
        public const int QualifyNamesId = 0x0117;
        public const int ScriptSummaryId = 0x0118;
        public const int FixCodeIssuesId = 0x0124;
        public const int PreviewAsSelectId = 0x0125;
        public const int DryRunId = 0x0126;
        public const int ScriptOutlineId = 0x012C;
        public const int QuickOpenId = 0x012D;
        public const int FindInCodeId = 0x012E;
        public const int ShowDependenciesId = 0x012F;
        public const int ReopenClosedTabId = 0x0130;
        public const int RenameQueryTabId = 0x0132;
        public const int OpenGridInExcelId = 0x0137;
        public const int CopyGridAsInListId = 0x0127;
        public const int CopyGridAsValuesId = 0x0128;
        public const int CopyGridAsJsonId = 0x0129;
        public const int CopyGridAsCsvId = 0x012A;
        public const int CopyGridAsMarkdownId = 0x012B;

        /// <summary>Kept so that its DTE events stay subscribed.</summary>
        private SchemaChangeWatcher schemaChangeWatcher;
        private ExecutionHooks executionHooks;
        private TabSessionService tabSession;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Log.Write("SQL Prompter " + typeof(SqlPrompterPackage).Assembly.GetName().Version + " loaded.");
            SnippetService.Start();
            UsageService.Start();
            HistoryService.Start();
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            TabColoring.Start();
            HideSsmsTabColor.Register();
            DefaultKeyBindings.Apply();
            GridContextMenu.Install();
            GridCommands.CleanTemporaryExcelFiles();
            ToolbarVisibility.ShowOnce();
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand((s, e) => RefreshMetadata(), new CommandID(CommandSet, RefreshMetadataId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenLog(), new CommandID(CommandSet, OpenLogId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenSettings(), new CommandID(CommandSet, SettingsId)));
                commands.AddCommand(new MenuCommand((s, e) => FormattingStylesWindow.Open(), new CommandID(CommandSet, FormattingStylesId)));
                FormattingStylesMenu.Register(commands, CommandSet);
                TabColorMenu.Register(commands, CommandSet, EditEnvironmentsId);
                commands.AddCommand(new MenuCommand((s, e) => ShowSuggestions(), new CommandID(CommandSet, ShowSuggestionsId)));
                commands.AddCommand(new MenuCommand((s, e) => PickerKey(left: true), new CommandID(CommandSet, PickerLeftId)));
                commands.AddCommand(new MenuCommand((s, e) => PickerKey(left: false), new CommandID(CommandSet, PickerRightId)));
                commands.AddCommand(new MenuCommand((s, e) => SurroundSelection(), new CommandID(CommandSet, SurroundWithSnippetId)));
                commands.AddCommand(new MenuCommand((s, e) => NewSnippetFromSelection(), new CommandID(CommandSet, NewSnippetFromSelectionId)));
                commands.AddCommand(new MenuCommand((s, e) => ManageSnippets(), new CommandID(CommandSet, ManageSnippetsId)));
                commands.AddCommand(new MenuCommand((s, e) => GoToDefinitionHandler.Run(ActiveEditor.GetView()), new CommandID(CommandSet, GoToDefinitionId)));
                commands.AddCommand(new MenuCommand((s, e) => LocateInObjectExplorer.Run(ActiveEditor.GetView()), new CommandID(CommandSet, LocateInObjectExplorerId)));
                commands.AddCommand(new MenuCommand((s, e) => LineCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.LineTools.AddCommas, "commas"), new CommandID(CommandSet, AddCommasId)));
                commands.AddCommand(new MenuCommand((s, e) => LineCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.LineTools.AddQuotesAndCommas, "quotes and commas"), new CommandID(CommandSet, AddQuotesAndCommasId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenQueryHistory(), new CommandID(CommandSet, QueryHistoryId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenQueryHistory(), new CommandID(CommandSet, QueryHistoryToolbarId)));
                commands.AddCommand(new MenuCommand((s, e) => GridCommands.ScriptSelectionAsInsert(), new CommandID(CommandSet, ScriptGridAsInsertId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.Refactorings.RemoveBrackets, "square brackets removed"), new CommandID(CommandSet, RemoveBracketsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.Refactorings.InsertSemicolons, "semicolons inserted"), new CommandID(CommandSet, InsertSemicolonsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.Refactorings.RemoveUnusedVariables, "unused variables removed"), new CommandID(CommandSet, RemoveUnusedVariablesId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), text => SqlPrompter.Core.Editing.CodeFixes.Apply(text, SettingsService.Current.Analysis, MetadataService.Current?.MetadataForScript(text)), "code analysis issues fixed"), new CommandID(CommandSet, FixCodeIssuesId)));
                commands.AddCommand(new MenuCommand((s, e) => PreviewStatement.AsSelect(ActiveEditor.GetView()), new CommandID(CommandSet, PreviewAsSelectId)));
                commands.AddCommand(new MenuCommand((s, e) => PreviewStatement.DryRun(ActiveEditor.GetView()), new CommandID(CommandSet, DryRunId)));
                commands.AddCommand(new MenuCommand((s, e) => { var view = ActiveEditor.GetView(); if (view != null) { QuickActionsPopup.ShowOutline(view); } }, new CommandID(CommandSet, ScriptOutlineId)));
                commands.AddCommand(new MenuCommand((s, e) => { var view = ActiveEditor.GetView(); if (view != null) { QuickActionsPopup.ShowObjects(view); } }, new CommandID(CommandSet, QuickOpenId)));
                commands.AddCommand(new MenuCommand((s, e) => FindInCode.Run(ActiveEditor.GetView()), new CommandID(CommandSet, FindInCodeId)));
                commands.AddCommand(new MenuCommand((s, e) => ShowDependencies.Run(ActiveEditor.GetView()), new CommandID(CommandSet, ShowDependenciesId)));
                commands.AddCommand(new MenuCommand((s, e) => tabSession?.ReopenClosedTab(), new CommandID(CommandSet, ReopenClosedTabId)));
                commands.AddCommand(new MenuCommand((s, e) => tabSession?.RenameActiveTab(), new CommandID(CommandSet, RenameQueryTabId)));
                // An IN list holds the values of one column: the command is off while the selection of the grid covers several (#446). Decided every time a menu opens.
                var copyAsInList = new OleMenuCommand((s, e) => GridCommands.CopyAs(t => SqlPrompter.Core.Editing.GridFormats.AsInList(t), "an IN list"), new CommandID(CommandSet, CopyGridAsInListId));
                copyAsInList.BeforeQueryStatus += (s, e) => copyAsInList.Enabled = !GridSelectionReader.SelectionSpansSeveralColumns();
                commands.AddCommand(copyAsInList);
                commands.AddCommand(new MenuCommand((s, e) => GridCommands.OpenInExcel(), new CommandID(CommandSet, OpenGridInExcelId)));
                commands.AddCommand(new MenuCommand((s, e) => GridCommands.CopyAs(t => SqlPrompter.Core.Editing.GridFormats.AsValues(t), "VALUES"), new CommandID(CommandSet, CopyGridAsValuesId)));
                commands.AddCommand(new MenuCommand((s, e) => GridCommands.CopyAs(t => SqlPrompter.Core.Editing.GridFormats.AsJson(t), "JSON"), new CommandID(CommandSet, CopyGridAsJsonId)));
                commands.AddCommand(new MenuCommand((s, e) => GridCommands.CopyAs(t => SqlPrompter.Core.Editing.GridFormats.AsCsv(t), "CSV"), new CommandID(CommandSet, CopyGridAsCsvId)));
                commands.AddCommand(new MenuCommand((s, e) => GridCommands.CopyAs(t => SqlPrompter.Core.Editing.GridFormats.AsMarkdown(t), "a Markdown table"), new CommandID(CommandSet, CopyGridAsMarkdownId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.ExpandStars(ActiveEditor.GetView()), new CommandID(CommandSet, ExpandStarsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.QualifyNames(ActiveEditor.GetView()), new CommandID(CommandSet, QualifyNamesId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.FormatScript(ActiveEditor.GetView(), true), new CommandID(CommandSet, FormatDocumentId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.FormatScript(ActiveEditor.GetView(), false), new CommandID(CommandSet, FormatSelectionId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.UnformatSelection(ActiveEditor.GetView()), new CommandID(CommandSet, UnformatSelectionId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.AliasRefactorings.AddNPrefix, "N prefix added to strings"), new CommandID(CommandSet, AddNPrefixId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.AliasRefactorings.AddAs, "AS added to aliases"), new CommandID(CommandSet, AddAsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.AliasRefactorings.RemoveAs, "AS removed from aliases"), new CommandID(CommandSet, RemoveAsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.ShowSummary(ActiveEditor.GetView()), new CommandID(CommandSet, ScriptSummaryId)));
                commands.AddCommand(new MenuCommand((s, e) => RunStatement.Run(ActiveEditor.GetView()), new CommandID(CommandSet, RunStatementId)));
                commands.AddCommand(new MenuCommand((s, e) => RunStatement.RunUpToCaret(ActiveEditor.GetView()), new CommandID(CommandSet, RunStatementsUpToCaretId)));
                commands.AddCommand(new MenuCommand((s, e) => { if (ActiveEditor.GetView() is Microsoft.VisualStudio.Text.Editor.IWpfTextView view) { ParameterInfoPopup.Show(view); } }, new CommandID(CommandSet, ParameterInfoId)));
            }

            try
            {
                if (await GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
                {
                    var report = SsmsCompatibility.Run(dte);
                    if (SsmsCompatibility.Has(SqlPrompter.Core.Compatibility.SsmsFeature.ExecuteHook))
                    {
                        schemaChangeWatcher = SchemaChangeWatcher.Start(dte);
                        executionHooks = ExecutionHooks.Start(dte);
                    }

                    TabHistoryService.Start(dte);
                    tabSession = TabSessionService.Start(dte);
                    SsmsCompatibility.TellOnce(this, report);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Starting the schema change watcher failed", ex);
            }
        }

        private static void RefreshMetadata()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RefreshNow.Run();
        }

        private sealed class MainWindowOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle => System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        }

        internal static void OpenSettings(string page = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                using (var window = new SettingsWindow(page))
                {
                    window.ShowDialog(new MainWindowOwner());
                }
            }
            catch (Exception ex)
            {
                Log.Error("Opening the settings window failed", ex);
            }
        }

        private void SurroundSelection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var view = ActiveEditor.GetView();
                if (view == null || view.Selection.IsEmpty)
                {
                    ShowMessage("Select the text to wrap first.");
                    return;
                }

                var candidates = SnippetService.Snippets.Where(SqlPrompter.Core.Snippets.SnippetText.UsesSelection).ToList();
                if (candidates.Count == 0)
                {
                    ShowMessage("No snippet wraps a selection. Use $SELECTEDTEXT$ in the body of a snippet, e.g. ISNULL($SELECTEDTEXT$, 0).");
                    return;
                }

                var picker = new SnippetPickerWindow("Surround selection with snippet", candidates);
                ActiveEditor.SetOwner(picker);
                if (picker.ShowDialog() == true && picker.Chosen != null)
                {
                    var span = view.Selection.SelectedSpans[0];
                    SnippetInserter.Insert(view, span, picker.Chosen, span.GetText());
                    view.VisualElement.Focus();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Surrounding the selection failed", ex);
                ShowMessage("Surrounding the selection failed: " + ex.Message);
            }
        }

        private void NewSnippetFromSelection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var view = ActiveEditor.GetView();
                var selected = view != null && !view.Selection.IsEmpty ? view.Selection.SelectedSpans[0].GetText() : string.Empty;
                var draft = SqlPrompter.Core.Snippets.Snippet.Create(string.Empty, string.Empty, SqlPrompter.Core.Snippets.SnippetText.BodyFromSelection(selected));
                var window = new SnippetManagerWindow(new SqlPrompter.Core.Snippets.SnippetStore(SnippetService.Folder), draft);
                ActiveEditor.SetOwner(window);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error("Creating a snippet from the selection failed", ex);
                ShowMessage("Creating the snippet failed: " + ex.Message);
            }
        }

        private void OpenQueryHistory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (SettingsService.Current.Behavior.HistoryAsTab && ShowHistoryTab())
                {
                    return;
                }

                var window = new HistoryWindow();
                ActiveEditor.SetOwner(window);
                window.ShowDialog();
                if (window.Chosen != null)
                {
                    OpenHistoryEntry(window.Chosen, window.MakeSnippet);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Opening the query history failed", ex);
                ShowMessage("Opening the query history failed: " + ex.Message);
            }
        }

        /// <summary>
        /// The history as a tab of the editor area (#385): the one tool window of the package is created or activated, with the entries as they are now.
        /// False when SSMS could not show it, then the window opens instead.
        /// </summary>
        private bool ShowHistoryTab()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var pane = FindToolWindow(typeof(HistoryToolWindowPane), 0, true) as HistoryToolWindowPane;
                var frame = pane?.Frame as Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame;
                if (frame == null)
                {
                    return false;
                }

                // An open tab is read again every time the history is opened, also when it is behind another tab (IsVisible is false then): the tab
                // showed the entries of the time it was created.
                if (pane.JustCreated)
                {
                    pane.JustCreated = false;
                }
                else
                {
                    pane.Refresh();
                }

                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Showing the history as a tab failed", ex);
                return false;
            }
        }

        /// <summary>
        /// What choosing an entry of the history does (the window and the tab call it): a run becomes a snippet, or the entry is opened in its tab, its file or a
        /// new tab, and continues its tab record (#375, #372, #327, #331).
        /// </summary>
        internal static void OpenHistoryEntry(SqlPrompter.Core.History.HistoryEntry chosen, bool makeSnippet)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var before = (GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ActiveDocument?.FullName;
                if (makeSnippet)
                {
                    var draft = SqlPrompter.Core.Snippets.Snippet.Create(string.Empty, string.Empty, SqlPrompter.Core.Snippets.SnippetText.BodyFromSelection(chosen.Text));
                    var manager = new SnippetManagerWindow(new SqlPrompter.Core.Snippets.SnippetStore(SnippetService.Folder), draft);
                    ActiveEditor.SetOwner(manager);
                    manager.ShowDialog();
                }
                else if (TouchEntry(chosen) && GoToTabOfEntry(chosen))
                {
                    // The entry is of a tab that is open: that tab is in front (#375), no second tab and no second record.
                }
                else if (OpenFileOfEntry(chosen))
                {
                    // The file of the tab was opened (#372).
                    BindOpenedWindow(chosen, before);
                }
                else if (!string.IsNullOrEmpty(chosen.Server) && !string.IsNullOrEmpty(chosen.Login) &&
                    !string.Equals(chosen.Server, SsmsConnection.GetActiveOfWindow()?.Server, StringComparison.OrdinalIgnoreCase))
                {
                    // Another server than the active window, with a SQL login: opened like a restored tab (live or remembered password, SSMS's Connect dialog otherwise).
                    if (!OpenWithoutActiveConnection(chosen))
                    {
                        ShowMessage("The query could not be opened in a new tab (see the log).");
                    }
                    else
                    {
                        BindOpenedWindow(chosen, before);
                    }
                }
                else if (!SsmsConnection.OpenNewWindow(chosen.Database, chosen.Text, chosen.Server, chosen.Login) && !OpenWithoutActiveConnection(chosen))
                {
                    ShowMessage("The query could not be opened in a new tab (see the log).");
                }
                else
                {
                    BindOpenedWindow(chosen, before);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Opening the query history failed", ex);
                ShowMessage("Opening the query history failed: " + ex.Message);
            }
        }
        /// <summary>An entry that is opened from the history comes first in the list afterwards (below the pinned ones, #378). Always true.</summary>
        private static bool TouchEntry(SqlPrompter.Core.History.HistoryEntry chosen)
        {
            HistoryService.Touch(chosen.Id);
            return true;
        }

        /// <summary>
        /// An entry of a tab that is open right now (the Open tabs view) goes to that tab (#375) instead of opening a copy. False when the tab is not open (any more),
        /// then the entry is opened as before. An older version of a tab is always opened as text.
        /// </summary>
        private static bool GoToTabOfEntry(SqlPrompter.Core.History.HistoryEntry chosen)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return !chosen.FromOlderVersion && !chosen.ClosedAtUtc.HasValue && !string.IsNullOrEmpty(chosen.Key) &&
                TabSessionService.Current != null && TabSessionService.Current.ActivateDocument(chosen.Key);
        }

        /// <summary>
        /// The window that was opened from the entry continues the tab record of the entry (#375): the history keeps one entry, and the window gets the name of the
        /// record as its caption. Nothing is bound when no new window came to the front (the user cancelled).
        /// </summary>
        private static void BindOpenedWindow(SqlPrompter.Core.History.HistoryEntry chosen, string documentBefore)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var document = (GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ActiveDocument?.FullName;
                if (string.IsNullOrEmpty(document) || (string.Equals(document, documentBefore, StringComparison.OrdinalIgnoreCase) && !string.Equals(document, chosen.File, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                var name = TabHistoryService.Bind(chosen, document);
                if (name != null)
                {
                    TabSessionService.Current?.AdoptName(document, name);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Continuing the tab of a history entry failed", ex);
            }
        }

        /// <summary>
        /// An entry of a tab that was a file opens the file again (#372): the file when it still has the text of the entry, the file or the text (the user
        /// chooses, the file is the default) when the file changed since; a file that is gone is announced in the status bar and the text opens as before.
        /// An older version of a tab always opens as text. False when the entry is to be opened as text in a new tab.
        /// </summary>
        private static bool OpenFileOfEntry(SqlPrompter.Core.History.HistoryEntry chosen)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(chosen.File) || chosen.FromOlderVersion)
            {
                return false;
            }

            string fileText = null;
            try
            {
                fileText = System.IO.File.Exists(chosen.File) ? System.IO.File.ReadAllText(chosen.File) : null;
            }
            catch (Exception ex)
            {
                Log.Error("Reading the file of a history entry failed", ex);
            }

            var action = SqlPrompter.Core.History.HistoryFile.Decide(chosen.File, fileText, chosen.Text);
            if (action == SqlPrompter.Core.History.HistoryOpenAction.FileMissingOpenText)
            {
                SetStatus("SQL Prompter: the file " + chosen.File + " of this entry is gone; its text is opened in a new tab.");
                return false;
            }

            if (action == SqlPrompter.Core.History.HistoryOpenAction.OpenFileOrText)
            {
                var answer = System.Windows.Forms.MessageBox.Show(
                    "The file " + chosen.File + " has changed since this entry was recorded.\n\nYes: open the file\nNo: open the text of the history entry in a new tab",
                    "SQL Prompter", System.Windows.Forms.MessageBoxButtons.YesNoCancel, System.Windows.Forms.MessageBoxIcon.Question, System.Windows.Forms.MessageBoxDefaultButton.Button1);
                if (answer == System.Windows.Forms.DialogResult.Cancel)
                {
                    return true;
                }

                if (answer == System.Windows.Forms.DialogResult.No)
                {
                    return false;
                }
            }

            var tab = new SqlPrompter.Core.History.SavedTab { File = chosen.File, Server = chosen.Server, Database = chosen.Database, Login = chosen.Login };
            if (TabSessionService.Current == null || !TabSessionService.Current.OpenFileTab(tab))
            {
                SetStatus("SQL Prompter: the file " + chosen.File + " could not be opened; its text is opened in a new tab.");
                return false;
            }

            return true;
        }

        private static void SetStatus(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar)) is Microsoft.VisualStudio.Shell.Interop.IVsStatusbar statusBar)
            {
                statusBar.SetText(message);
            }
        }

        /// <summary>
        /// No window is connected to copy the connection from (#327): the query is opened in a new tab with the server, database and login it had,
        /// connected like a restored tab (a live connection of the same login, Windows authentication, or SSMS's own Connect dialog filled in); when
        /// even that is not possible a new query tab is requested from SSMS, which asks for a connection itself, and the text is put into it.
        /// </summary>
        private static bool OpenWithoutActiveConnection(SqlPrompter.Core.History.HistoryEntry chosen)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var tab = new SqlPrompter.Core.History.SavedTab { Text = chosen.Text, Server = chosen.Server, Database = chosen.Database, Login = chosen.Login };
            if (!string.IsNullOrEmpty(chosen.Server) && SsmsConnection.OpenSavedTab(tab, out _))
            {
                return true;
            }

            try
            {
                (GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ExecuteCommand("File.NewQuery");
                var view = ActiveEditor.GetView();
                view?.TextBuffer.Insert(0, chosen.Text ?? string.Empty);
                return view != null;
            }
            catch (Exception ex)
            {
                Log.Error("Opening a new query tab for the history entry failed", ex);
                return false;
            }
        }

        private void ManageSnippets()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            OpenSnippetManager();
        }

        /// <summary>Opens the snippet manager (the menu command, the settings window and the Options page use it); the snippets reload when it closes.</summary>
        internal static void OpenSnippetManager()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var window = new SnippetManagerWindow(new SqlPrompter.Core.Snippets.SnippetStore(SnippetService.Folder), null);
                ActiveEditor.SetOwner(window);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error("Opening the snippet manager failed", ex);
                System.Windows.Forms.MessageBox.Show("Opening the snippet manager failed: " + ex.Message, "SQL Prompter");
            }
        }

        private static void ShowMessage(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider, text, "SQL Prompter", Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        /// <summary>Imports snippet files into the snippet folder (a button of the snippet manager).</summary>
        internal static void ImportSnippets()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                using (var dialog = new System.Windows.Forms.OpenFileDialog
                {
                    Title = "Import snippets (SQL Prompt .sqlpromptsnippet or snippet .json files)",
                    Filter = "Snippet files (*.sqlpromptsnippet;*.json)|*.sqlpromptsnippet;*.json|All files (*.*)|*.*",
                    Multiselect = true,
                })
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    {
                        return;
                    }

                    var store = new SqlPrompter.Core.Snippets.SnippetStore(SnippetService.Folder);
                    bool? replaceAnswer = null;
                    var result = store.Import(
                        dialog.FileNames,
                        prefix => (replaceAnswer ?? (replaceAnswer = AskYesNo("A snippet with the prefix \"" + prefix + "\" exists already (there may be more). Replace snippets with the same prefix?"))).Value);
                    var text = result.Imported + " snippets imported into " + store.Folder + (result.Replaced > 0 ? " (" + result.Replaced + " replaced)" : string.Empty) +
                        (result.Skipped > 0 ? ", " + result.Skipped + " skipped because their prefix exists" : string.Empty) + ".";
                    if (result.Errors.Count > 0)
                    {
                        text += "\n\nNot imported:\n- " + string.Join("\n- ", result.Errors);
                    }

                    SnippetService.Reload();
                    System.Windows.Forms.MessageBox.Show(text, "SQL Prompter", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Importing the snippets failed", ex);
                System.Windows.Forms.MessageBox.Show("Importing the snippets failed: " + ex.Message, "SQL Prompter");
            }
        }

        /// <summary>Opens the snippet folder in Explorer.</summary>
        internal static void OpenSnippetsFolder() => OpenSnippetsFolder(null);

        /// <summary>Opens the snippet folder; <paramref name="configured"/> is the folder as typed in the settings (empty or null: the folder of the saved settings).</summary>
        internal static void OpenSnippetsFolder(string configured)
        {
            try
            {
                var folder = string.IsNullOrWhiteSpace(configured) ? (configured == null ? SnippetService.Folder : SnippetService.DefaultFolder) : Environment.ExpandEnvironmentVariables(configured.Trim());
                System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex)
            {
                Log.Error("Opening the snippet folder failed", ex);
            }
        }

        /// <summary>Writes the built-in snippets over the snippet folder after a confirmation (default No), then reloads the snippets (#417).</summary>
        internal static void ResetBuiltInSnippets(System.Windows.Forms.IWin32Window owner)
        {
            if (System.Windows.Forms.MessageBox.Show(owner,
                    "This writes all the built-in snippets into the snippet folder in their original text.\r\n\r\n" +
                    "Built-in snippets you deleted come back, and those you edited are restored: your changes to them will be lost. " +
                    "A snippet of yours with the prefix of a built-in one is replaced too. Your other snippets are not touched.\r\n\r\nContinue?",
                    "SQL Prompter", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Warning,
                    System.Windows.Forms.MessageBoxDefaultButton.Button2) != System.Windows.Forms.DialogResult.Yes)
            {
                return;
            }

            try
            {
                var store = new SqlPrompter.Core.Snippets.SnippetStore(SnippetService.Folder);
                var result = new SqlPrompter.Core.Snippets.BuiltInInstaller(store).ResetAll();
                SnippetService.Reload();
                Log.Write("Snippets: reset the built-in snippets in " + store.Folder + " (" + result.Restored + " restored, " + result.Added + " added).");
                System.Windows.Forms.MessageBox.Show(owner, result.Restored + " built-in snippets restored and " + result.Added + " added in " + store.Folder + ".", "SQL Prompter");
            }
            catch (Exception ex)
            {
                Log.Error("Resetting the built-in snippets failed", ex);
                System.Windows.Forms.MessageBox.Show(owner, "Resetting the built-in snippets failed: " + ex.Message, "SQL Prompter", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        /// <summary>Forgets which suggestions the user chose how often (a button of the settings window).</summary>
        internal static void ResetUsage(System.Windows.Forms.IWin32Window owner)
        {
            if (System.Windows.Forms.MessageBox.Show(owner, "Forget which suggestions you chose how often? The list goes back to its plain order.", "SQL Prompter",
                    System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question, System.Windows.Forms.MessageBoxDefaultButton.Button2) == System.Windows.Forms.DialogResult.Yes)
            {
                UsageService.Reset();
            }
        }

        private static bool AskYesNo(string text) =>
            System.Windows.Forms.MessageBox.Show(text, "SQL Prompter", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question, System.Windows.Forms.MessageBoxDefaultButton.Button2) == System.Windows.Forms.DialogResult.Yes;
        /// <summary>
        /// Opens the suggestion list at the caret (Ctrl+Space). The key (Edit.CompleteWord) does not reach the command handlers of the SQL editor and a
        /// session started from outside never asks our source for items (#144), so Ctrl+Space is bound to this command and the list is run by
        /// <see cref="InvokedList"/>, which uses the same source, presenter and commit code.
        /// </summary>
        private void ShowSuggestions()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ActiveEditor.GetView() is Microsoft.VisualStudio.Text.Editor.IWpfTextView view)
            {
                InvokedList.Open(view);
            }
        }
        /// <summary>
        /// Ctrl+Left / Ctrl+Right: Ctrl+Left switches the list to the column picker (our own list, or the editor's list where columns are picked, which is
        /// replaced by ours), Ctrl+Right back to all suggestions. The SDK has no command arguments for these keys, so they are bound to hidden commands;
        /// where no list is involved the command does what the key did before (Edit.WordPrevious, Edit.WordNext).
        /// </summary>
        private void PickerKey(bool left)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (ActiveEditor.GetView() is Microsoft.VisualStudio.Text.Editor.IWpfTextView view)
                {
                    var list = InvokedList.Of(view);
                    if (list != null && list.IsOpen)
                    {
                        if (list.OnKey(left ? System.Windows.Input.Key.Left : System.Windows.Input.Key.Right))
                        {
                            return;
                        }
                    }
                    else if (left)
                    {
                        var components = (Microsoft.VisualStudio.ComponentModelHost.IComponentModel)GetGlobalService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel));
                        var broker = components.GetService<Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.IAsyncCompletionBroker>();
                        var session = broker.GetSession(view);
                        if (session != null && InvokedList.CanPick(view))
                        {
                            session.Dismiss();
                            InvokedList.Open(view, picker: true);
                            return;
                        }
                    }
                }

                if (SsmsCompatibility.Has(SqlPrompter.Core.Compatibility.SsmsFeature.WordKeys))
                {
                    var dte = (EnvDTE.DTE)GetGlobalService(typeof(EnvDTE.DTE));
                    dte.ExecuteCommand(left ? "Edit.WordPrevious" : "Edit.WordNext");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Switching the suggestion list failed", ex);
            }
        }

        private void OpenLog()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                VsShellUtilities.OpenDocument(this, Log.FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("Opening the log failed", ex);
            }
        }
    }
}
