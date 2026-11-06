using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.LspProbe
{
    /// <summary>A language client for the SQL content type that starts a tiny language server (architecture review, #183).</summary>
    [ContentType("SQL")]
    [Export(typeof(ILanguageClient))]
    public sealed class ProbeLanguageClient : ILanguageClient
    {
        private static void Log(string text)
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPrompter", "lspprobe-client.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine);
            }
            catch
            {
            }
        }

        public ProbeLanguageClient()
        {
            Log("client constructed");
        }

        public string Name => "SQL Prompter LSP probe";

        public IEnumerable<string> ConfigurationSections => null;

        public object InitializationOptions => null;

        public IEnumerable<string> FilesToWatch => null;

        public bool ShowNotificationOnInitializeFailed => true;

        public event AsyncEventHandler<EventArgs> StartAsync;

        public event AsyncEventHandler<EventArgs> StopAsync
        {
            add { }
            remove { }
        }

        public async Task<Connection> ActivateAsync(CancellationToken token)
        {
            await Task.Yield();
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPrompter", "lspprobe", "LspProbeServer.exe");
            Log("ActivateAsync, starting " + exe);
            var info = new ProcessStartInfo(exe) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            var process = Process.Start(info);
            return new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        }

        public async Task OnLoadedAsync()
        {
            Log("OnLoadedAsync");
            await StartAsync.InvokeAsync(this, EventArgs.Empty);
        }

        public Task OnServerInitializedAsync()
        {
            Log("OnServerInitializedAsync");
            return Task.CompletedTask;
        }

        public Task<InitializationFailureContext> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
        {
            Log("OnServerInitializeFailedAsync: " + initializationState?.StatusMessage + " " + initializationState?.InitializationException);
            return Task.FromResult(new InitializationFailureContext { FailureMessage = initializationState?.StatusMessage });
        }
    }
}
