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
    [ProvideMenuResource("Menus.ctmenu", 35)]
    [ProvideOptionPage(typeof(OptionsLauncherPage), "SQL Prompter", "General", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SqlPrompterPackage : AsyncPackage
    {
        public const string PackageGuidString = "a3a4db19-c58e-4eac-8a84-ba61357b9b01";
        public static readonly Guid CommandSet = new Guid("15e19ce9-b3c9-47dd-97f9-49fe13869730");
        public const int RefreshMetadataId = 0x0100;
        public const int OpenLogId = 0x0101;
        public const int SettingsId = 0x011F;
        public const int FormattingStylesId = 0x0120;
        public const int ShowSuggestionsId = 0x0121;
        public const int PickerLeftId = 0x0122;
        public const int PickerRightId = 0x0123;
        public const int FormatDocumentId = 0x011A;
        public const int FormatSelectionId = 0x011B;
        public const int AddNPrefixId = 0x011C;
        public const int AddAsId = 0x011D;
        public const int RemoveAsId = 0x011E;
        public const int SurroundWithSnippetId = 0x0107;
        public const int NewSnippetFromSelectionId = 0x0108;
        public const int ManageSnippetsId = 0x0109;
        public const int GoToDefinitionId = 0x010B;
        public const int RunStatementId = 0x010C;
        public const int ParameterInfoId = 0x010D;
        public const int AddCommasId = 0x010F;
        public const int AddQuotesAndCommasId = 0x0110;
        public const int QueryHistoryId = 0x0111;
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
            DefaultKeyBindings.Apply();
            GridContextMenu.Install();
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand((s, e) => RefreshMetadata(), new CommandID(CommandSet, RefreshMetadataId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenLog(), new CommandID(CommandSet, OpenLogId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenSettings(), new CommandID(CommandSet, SettingsId)));
                commands.AddCommand(new MenuCommand((s, e) => FormattingStylesWindow.Open(), new CommandID(CommandSet, FormattingStylesId)));
                commands.AddCommand(new MenuCommand((s, e) => ShowSuggestions(), new CommandID(CommandSet, ShowSuggestionsId)));
                commands.AddCommand(new MenuCommand((s, e) => PickerKey(left: true), new CommandID(CommandSet, PickerLeftId)));
                commands.AddCommand(new MenuCommand((s, e) => PickerKey(left: false), new CommandID(CommandSet, PickerRightId)));
                commands.AddCommand(new MenuCommand((s, e) => SurroundSelection(), new CommandID(CommandSet, SurroundWithSnippetId)));
                commands.AddCommand(new MenuCommand((s, e) => NewSnippetFromSelection(), new CommandID(CommandSet, NewSnippetFromSelectionId)));
                commands.AddCommand(new MenuCommand((s, e) => ManageSnippets(), new CommandID(CommandSet, ManageSnippetsId)));
                commands.AddCommand(new MenuCommand((s, e) => GoToDefinitionHandler.Run(ActiveEditor.GetView()), new CommandID(CommandSet, GoToDefinitionId)));
                commands.AddCommand(new MenuCommand((s, e) => LineCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.LineTools.AddCommas, "commas"), new CommandID(CommandSet, AddCommasId)));
                commands.AddCommand(new MenuCommand((s, e) => LineCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.LineTools.AddQuotesAndCommas, "quotes and commas"), new CommandID(CommandSet, AddQuotesAndCommasId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenQueryHistory(), new CommandID(CommandSet, QueryHistoryId)));
                commands.AddCommand(new MenuCommand((s, e) => GridCommands.ScriptSelectionAsInsert(), new CommandID(CommandSet, ScriptGridAsInsertId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.Refactorings.RemoveBrackets, "square brackets removed"), new CommandID(CommandSet, RemoveBracketsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.Refactorings.InsertSemicolons, "semicolons inserted"), new CommandID(CommandSet, InsertSemicolonsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.Refactorings.RemoveUnusedVariables, "unused variables removed"), new CommandID(CommandSet, RemoveUnusedVariablesId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), text => SqlPrompter.Core.Editing.CodeFixes.Apply(text, SettingsService.Current.Analysis), "code analysis issues fixed"), new CommandID(CommandSet, FixCodeIssuesId)));
                commands.AddCommand(new MenuCommand((s, e) => PreviewStatement.AsSelect(ActiveEditor.GetView()), new CommandID(CommandSet, PreviewAsSelectId)));
                commands.AddCommand(new MenuCommand((s, e) => PreviewStatement.DryRun(ActiveEditor.GetView()), new CommandID(CommandSet, DryRunId)));
                commands.AddCommand(new MenuCommand((s, e) => { var view = ActiveEditor.GetView(); if (view != null) { QuickActionsPopup.ShowOutline(view); } }, new CommandID(CommandSet, ScriptOutlineId)));
                commands.AddCommand(new MenuCommand((s, e) => { var view = ActiveEditor.GetView(); if (view != null) { QuickActionsPopup.ShowObjects(view); } }, new CommandID(CommandSet, QuickOpenId)));
                commands.AddCommand(new MenuCommand((s, e) => FindInCode.Run(ActiveEditor.GetView()), new CommandID(CommandSet, FindInCodeId)));
                commands.AddCommand(new MenuCommand((s, e) => ShowDependencies.Run(ActiveEditor.GetView()), new CommandID(CommandSet, ShowDependenciesId)));
                commands.AddCommand(new MenuCommand((s, e) => tabSession?.ReopenClosedTab(), new CommandID(CommandSet, ReopenClosedTabId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.ExpandStars(ActiveEditor.GetView()), new CommandID(CommandSet, ExpandStarsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.QualifyNames(ActiveEditor.GetView()), new CommandID(CommandSet, QualifyNamesId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.FormatScript(ActiveEditor.GetView(), true), new CommandID(CommandSet, FormatDocumentId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.FormatScript(ActiveEditor.GetView(), false), new CommandID(CommandSet, FormatSelectionId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.AliasRefactorings.AddNPrefix, "N prefix added to strings"), new CommandID(CommandSet, AddNPrefixId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.AliasRefactorings.AddAs, "AS added to aliases"), new CommandID(CommandSet, AddAsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.Run(ActiveEditor.GetView(), SqlPrompter.Core.Editing.AliasRefactorings.RemoveAs, "AS removed from aliases"), new CommandID(CommandSet, RemoveAsId)));
                commands.AddCommand(new MenuCommand((s, e) => RefactorCommands.ShowSummary(ActiveEditor.GetView()), new CommandID(CommandSet, ScriptSummaryId)));
                commands.AddCommand(new MenuCommand((s, e) => RunStatement.Run(ActiveEditor.GetView()), new CommandID(CommandSet, RunStatementId)));
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
            MetadataService.Current?.Caches.InvalidateAll();
            Log.Write("Metadata dropped; it is loaded again on the next completion.");
        }

        private sealed class MainWindowOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle => System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        }

        internal static void OpenSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                using (var window = new SettingsWindow())
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
                var window = new HistoryWindow();
                ActiveEditor.SetOwner(window);
                window.ShowDialog();
                var chosen = window.Chosen;
                if (chosen != null && !SsmsConnection.OpenNewWindow(chosen.Database, chosen.Text, chosen.Server, chosen.Login))
                {
                    ShowMessage("Open a query window connected to a server first; the new tab is opened like it.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Opening the query history failed", ex);
                ShowMessage("Opening the query history failed: " + ex.Message);
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

        private void ShowMessage(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                this, text, "SQL Prompter", Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO,
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
        internal static void OpenSnippetsFolder()
        {
            try
            {
                var folder = SnippetService.Folder;
                System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex)
            {
                Log.Error("Opening the snippet folder failed", ex);
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
