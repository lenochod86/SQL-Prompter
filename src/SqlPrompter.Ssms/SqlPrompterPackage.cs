using System;
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
    [ProvideMenuResource("Menus.ctmenu", 4)]
    [ProvideOptionPage(typeof(BehaviorOptionsPage), "SQL Prompter", "Behavior", 0, 0, true)]
    [ProvideOptionPage(typeof(AliasOptionsPage), "SQL Prompter", "Aliases and Qualification", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SqlPrompterPackage : AsyncPackage
    {
        public const string PackageGuidString = "a3a4db19-c58e-4eac-8a84-ba61357b9b01";
        public static readonly Guid CommandSet = new Guid("15e19ce9-b3c9-47dd-97f9-49fe13869730");
        public const int RefreshMetadataId = 0x0100;
        public const int OpenLogId = 0x0101;
        public const int ExportSettingsId = 0x0102;
        public const int ImportSettingsId = 0x0103;

        /// <summary>Kept so that its DTE events stay subscribed.</summary>
        private SchemaChangeWatcher schemaChangeWatcher;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Log.Write("SQL Prompter " + typeof(SqlPrompterPackage).Assembly.GetName().Version + " loaded.");
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand((s, e) => RefreshMetadata(), new CommandID(CommandSet, RefreshMetadataId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenLog(), new CommandID(CommandSet, OpenLogId)));
                commands.AddCommand(new MenuCommand((s, e) => ExportSettings(), new CommandID(CommandSet, ExportSettingsId)));
                commands.AddCommand(new MenuCommand((s, e) => ImportSettings(), new CommandID(CommandSet, ImportSettingsId)));
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
