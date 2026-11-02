using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace SqlPrompter.Ssms
{
    /// <summary>Příkazy v nabídce Nástroje → SQL Prompter. Našeptávání samo běží přes MEF.</summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    // Druhý parametr je verze nabídek. Po každé změně .vsct ho zvyšte, jinak SSMS použije nabídky z mezipaměti.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SqlPrompterPackage : AsyncPackage
    {
        public const string PackageGuidString = "a3a4db19-c58e-4eac-8a84-ba61357b9b01";
        public static readonly Guid CommandSet = new Guid("15e19ce9-b3c9-47dd-97f9-49fe13869730");
        public const int RefreshMetadataId = 0x0100;
        public const int OpenLogId = 0x0101;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Log.Write("SQL Prompter " + typeof(SqlPrompterPackage).Assembly.GetName().Version + " načten.");
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand((s, e) => RefreshMetadata(), new CommandID(CommandSet, RefreshMetadataId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenLog(), new CommandID(CommandSet, OpenLogId)));
            }
        }

        private static void RefreshMetadata()
        {
            MetadataService.Current?.Caches.InvalidateAll();
            Log.Write("Metadata zahozena, při dalším našeptání se načtou znovu.");
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
                Log.Error("Otevření logu selhalo", ex);
            }
        }
    }
}
