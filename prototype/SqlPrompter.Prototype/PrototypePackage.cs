using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace SqlPrompter.Prototype
{
    /// <summary>
    /// Package loaded in the background after the shell starts. Logs which host it runs in
    /// and adds commands to the Tools -> SQL Prompter (prototype) menu.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    // The second parameter is the menu version. Bump it after every .vsct change, otherwise SSMS uses cached menus.
    [ProvideMenuResource("Menus.ctmenu", 2)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class PrototypePackage : AsyncPackage
    {
        public const string PackageGuidString = "3ec85663-3d4b-488d-9bf5-3df6d2104b8a";
        public static readonly Guid CommandSet = new Guid("86694616-b33b-4f53-b202-d741c0bf3567");
        public const int DumpConnectionId = 0x0100;
        public const int TestConnectionId = 0x0101;
        public const int OpenLogId = 0x0102;
        public const int ToggleHelloId = 0x0103;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            LogHost();

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (await GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
            {
                Log.Write("host", "DTE: Name=" + dte.Name + ", Version=" + dte.Version + ", Edition=" + SafeEdition(dte));
            }

            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand((s, e) => DumpConnection(), new CommandID(CommandSet, DumpConnectionId)));
                commands.AddCommand(new MenuCommand((s, e) => TestConnection(), new CommandID(CommandSet, TestConnectionId)));
                commands.AddCommand(new MenuCommand((s, e) => OpenLog(), new CommandID(CommandSet, OpenLogId)));
                commands.AddCommand(new MenuCommand(ToggleHello, new CommandID(CommandSet, ToggleHelloId)) { Checked = HelloCompletionSource.Enabled });
                Log.Write("host", "Commands registered.");
            }
            else
            {
                Log.Write("host", "IMenuCommandService is not available; commands are not registered.");
            }
        }

        private static void LogHost()
        {
            var process = Process.GetCurrentProcess();
            Log.Write("host", "=== SQL Prompter prototype loaded ===");
            Log.Write("host", "Proces: " + process.ProcessName + ", 64bit=" + Environment.Is64BitProcess +
                ", architektura=" + RuntimeInformation.ProcessArchitecture + ", CLR=" + Environment.Version);
            Log.Write("host", "Variables: " + SqlContentTypes.OverrideVariable + "=" +
                (Environment.GetEnvironmentVariable(SqlContentTypes.OverrideVariable) ?? "(nenastaveno)") + ", " +
                LegacyCompletionProbeProvider.EnableVariable + "=" + LegacyCompletionProbeProvider.Enabled);

            var interesting = new[]
            {
                "Microsoft.VisualStudio.Shell.Framework",
                "Microsoft.VisualStudio.Language",
                "Microsoft.VisualStudio.Language.Intellisense",
                "Microsoft.VisualStudio.Text.Data",
                "Microsoft.Data.SqlClient",
                "SQLEditors",
                "SqlWorkbench.Interfaces",
            };
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                         .Where(a => interesting.Contains(a.GetName().Name, StringComparer.OrdinalIgnoreCase))
                         .OrderBy(a => a.GetName().Name))
            {
                Log.Write("host", "Assembly: " + assembly.GetName().Name + " " + assembly.GetName().Version);
            }
        }

        private static string SafeEdition(EnvDTE.DTE dte)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return dte.Edition;
            }
            catch (Exception ex)
            {
                return "(" + ex.GetType().Name + ")";
            }
        }

        private void DumpConnection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // The result is not kept: data including the password lives in memory only during the command.
            ConnectionProbe.DumpActiveConnection();
        }

        private void TestConnection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = ConnectionProbe.DumpActiveConnection();
            if (snapshot == null || string.IsNullOrEmpty(snapshot.ServerName))
            {
                Log.Write("q3", "Connection test skipped: connection data missing.");
                return;
            }

            _ = JoinableTaskFactory.RunAsync(() => ConnectionProbe.TestConnectionAsync(snapshot, DisposalToken));
        }

        private static void ToggleHello(object sender, EventArgs e)
        {
            HelloCompletionSource.Enabled = !HelloCompletionSource.Enabled;
            if (sender is MenuCommand command)
            {
                command.Checked = HelloCompletionSource.Enabled;
            }

            Log.Write("q2", "Hello suggestions " + (HelloCompletionSource.Enabled ? "enabled" : "disabled") + ".");
        }

        private void OpenLog()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Log.Write("host", "Opening the log.");
            try
            {
                VsShellUtilities.OpenDocument(this, Log.FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("host", "Opening the log failed", ex);
            }
        }
    }
}
