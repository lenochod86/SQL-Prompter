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
    [ProvideMenuResource("Menus.ctmenu", 22)]
    [ProvideOptionPage(typeof(BehaviorOptionsPage), "SQL Prompter", "Behavior", 0, 0, true)]
    [ProvideOptionPage(typeof(AliasOptionsPage), "SQL Prompter", "Aliases and Qualification", 0, 0, true)]
    [ProvideOptionPage(typeof(JoinOptionsPage), "SQL Prompter", "Joins", 0, 0, true)]
    [ProvideOptionPage(typeof(InsertOptionsPage), "SQL Prompter", "Inserts", 0, 0, true)]
    [ProvideOptionPage(typeof(SnippetOptionsPage), "SQL Prompter", "Snippets", 0, 0, true)]
    [ProvideOptionPage(typeof(ColoringOptionsPage), "SQL Prompter", "Coloring", 0, 0, true)]
    [ProvideOptionPage(typeof(AnalysisOptionsPage), "SQL Prompter", "Code Analysis", 0, 0, true)]
    [ProvideOptionPage(typeof(FormattingOptionsPage), "SQL Prompter", "Formatting", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SqlPrompterPackage : AsyncPackage
    {
        public const string PackageGuidString = "a3a4db19-c58e-4eac-8a84-ba61357b9b01";
        public static readonly Guid CommandSet = new Guid("15e19ce9-b3c9-47dd-97f9-49fe13869730");
        public const int RefreshMetadataId = 0x0100;
        public const int OpenLogId = 0x0101;
        public const int SettingsId = 0x011F;
        public const int ExportSettingsId = 0x0102;
        public const int ImportSettingsId = 0x0103;
        public const int ImportFormatStyleId = 0x0119;
        public const int FormatDocumentId = 0x011A;
        public const int FormatSelectionId = 0x011B;
        public const int AddNPrefixId = 0x011C;
        public const int AddAsId = 0x011D;
        public const int RemoveAsId = 0x011E;
        public const int ImportSnippetsId = 0x0104;
        public const int OpenSnippetsFolderId = 0x0105;
        public const int ReloadSnippetsId = 0x0106;
        public const int SurroundWithSnippetId = 0x0107;
        public const int NewSnippetFromSelectionId = 0x0108;
        public const int ManageSnippetsId = 0x0109;
        public const int GoToDefinitionId = 0x010B;
        public const int RunStatementId = 0x010C;
        public const int ParameterInfoId = 0x010D;
        public const int ResetUsageId = 0x010E;
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
                commands.AddCommand(new MenuCommand((s, e) => ResetUsage(), new CommandID(CommandSet, ResetUsageId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenSettings(), new CommandID(CommandSet, SettingsId)));
                commands.AddCommand(new MenuCommand((s, e) => ExportSettings(), new CommandID(CommandSet, ExportSettingsId)));
                commands.AddCommand(new MenuCommand((s, e) => ImportSettings(), new CommandID(CommandSet, ImportSettingsId)));
                commands.AddCommand(new MenuCommand((s, e) => ImportFormatStyle(), new CommandID(CommandSet, ImportFormatStyleId)));
                commands.AddCommand(new MenuCommand((s, e) => ImportSnippets(), new CommandID(CommandSet, ImportSnippetsId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenSnippetsFolder(), new CommandID(CommandSet, OpenSnippetsFolderId)));
                commands.AddCommand(new MenuCommand((s, e) => SnippetService.Reload(), new CommandID(CommandSet, ReloadSnippetsId)));
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
                    schemaChangeWatcher = SchemaChangeWatcher.Start(dte);
                    executionHooks = ExecutionHooks.Start(dte);
                    tabSession = TabSessionService.Start(dte);
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

        private void ExportSettings() => SettingsFiles.Export(null);

        private void ImportSettings() => SettingsFiles.Import(null);

        private void ImportFormatStyle() => SettingsFiles.ImportFormatStyle(null);

        /// <summary>The SSMS main window as the owner of a WinForms dialog, so that the dialog stays in front of it.</summary>
        private sealed class MainWindowOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle => System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        }

        private static void OpenSettings()
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

        private void ImportSnippets()
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
                    ShowMessage(text);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Importing the snippets failed", ex);
                ShowMessage("Importing the snippets failed: " + ex.Message);
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
            try
            {
                var window = new SnippetManagerWindow(new SqlPrompter.Core.Snippets.SnippetStore(SnippetService.Folder), null);
                ActiveEditor.SetOwner(window);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error("Opening the snippet manager failed", ex);
                ShowMessage("Opening the snippet manager failed: " + ex.Message);
            }
        }

        private void OpenSnippetsFolder()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
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

        private bool AskYesNo(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return VsShellUtilities.ShowMessageBox(
                this, text, "SQL Prompter", Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_QUERY,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) == 6;
        }

        private void ShowMessage(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                this, text, "SQL Prompter", Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private void ResetUsage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (VsShellUtilities.ShowMessageBox(this, "Forget which suggestions you chose how often? The list goes back to its plain order.", "SQL Prompter",
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_QUERY, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) == 6)
            {
                UsageService.Reset();
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
