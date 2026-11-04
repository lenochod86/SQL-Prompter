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
    [ProvideMenuResource("Menus.ctmenu", 6)]
    [ProvideOptionPage(typeof(BehaviorOptionsPage), "SQL Prompter", "Behavior", 0, 0, true)]
    [ProvideOptionPage(typeof(AliasOptionsPage), "SQL Prompter", "Aliases and Qualification", 0, 0, true)]
    [ProvideOptionPage(typeof(JoinOptionsPage), "SQL Prompter", "Joins", 0, 0, true)]
    [ProvideOptionPage(typeof(SnippetOptionsPage), "SQL Prompter", "Snippets", 0, 0, true)]
    [ProvideOptionPage(typeof(ColoringOptionsPage), "SQL Prompter", "Coloring", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SqlPrompterPackage : AsyncPackage
    {
        public const string PackageGuidString = "a3a4db19-c58e-4eac-8a84-ba61357b9b01";
        public static readonly Guid CommandSet = new Guid("15e19ce9-b3c9-47dd-97f9-49fe13869730");
        public const int RefreshMetadataId = 0x0100;
        public const int OpenLogId = 0x0101;
        public const int ExportSettingsId = 0x0102;
        public const int ImportSettingsId = 0x0103;
        public const int ImportSnippetsId = 0x0104;
        public const int OpenSnippetsFolderId = 0x0105;
        public const int ReloadSnippetsId = 0x0106;
        public const int SurroundWithSnippetId = 0x0107;
        public const int NewSnippetFromSelectionId = 0x0108;
        public const int ManageSnippetsId = 0x0109;

        /// <summary>Kept so that its DTE events stay subscribed.</summary>
        private SchemaChangeWatcher schemaChangeWatcher;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Log.Write("SQL Prompter " + typeof(SqlPrompterPackage).Assembly.GetName().Version + " loaded.");
            SnippetService.Start();
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            TabColoring.Start();
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand((s, e) => RefreshMetadata(), new CommandID(CommandSet, RefreshMetadataId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenLog(), new CommandID(CommandSet, OpenLogId)));
                commands.AddCommand(new MenuCommand((s, e) => ExportSettings(), new CommandID(CommandSet, ExportSettingsId)));
                commands.AddCommand(new MenuCommand((s, e) => ImportSettings(), new CommandID(CommandSet, ImportSettingsId)));
                commands.AddCommand(new MenuCommand((s, e) => ImportSnippets(), new CommandID(CommandSet, ImportSnippetsId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenSnippetsFolder(), new CommandID(CommandSet, OpenSnippetsFolderId)));
                commands.AddCommand(new MenuCommand((s, e) => SnippetService.Reload(), new CommandID(CommandSet, ReloadSnippetsId)));
                commands.AddCommand(new MenuCommand((s, e) => SurroundSelection(), new CommandID(CommandSet, SurroundWithSnippetId)));
                commands.AddCommand(new MenuCommand((s, e) => NewSnippetFromSelection(), new CommandID(CommandSet, NewSnippetFromSelectionId)));
                commands.AddCommand(new MenuCommand((s, e) => ManageSnippets(), new CommandID(CommandSet, ManageSnippetsId)));
            }

            try
            {
                if (await GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
                {
                    schemaChangeWatcher = SchemaChangeWatcher.Start(dte);
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

        private void ExportSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                using (var dialog = new System.Windows.Forms.SaveFileDialog
                {
                    Title = "Export SQL Prompter settings",
                    Filter = "SQL Prompter settings (*.json)|*.json",
                    FileName = "sqlprompter-settings.json",
                })
                {
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        SqlPrompter.Core.Settings.SettingsJson.Save(dialog.FileName, SettingsService.Current);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exporting the settings failed", ex);
                ShowMessage("Exporting the settings failed: " + ex.Message);
            }
        }

        private void ImportSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                using (var dialog = new System.Windows.Forms.OpenFileDialog
                {
                    Title = "Import settings (SQL Prompter .json or SQL Prompt .settings)",
                    Filter = "Settings files (*.json;*.settings)|*.json;*.settings|All files (*.*)|*.*",
                })
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    {
                        return;
                    }

                    var result = SqlPrompter.Core.Settings.SettingsImporter.ImportFile(dialog.FileName, SettingsService.Current);
                    SettingsService.Update(result.Settings);
                    var text = "Imported:\n- " + string.Join("\n- ", result.Applied);
                    if (result.Notes.Count > 0)
                    {
                        text += "\n\nNot applied:\n- " + string.Join("\n- ", result.Notes);
                    }

                    ShowMessage(text);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Importing the settings failed", ex);
                ShowMessage("Importing the settings failed: " + ex.Message);
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
