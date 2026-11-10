using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The manual refresh (#423): drops every snapshot and every loaded definition, starts loading the metadata of the active window at once
    /// and says in the status bar that it runs; the code analysis and the lists update when the load finishes.
    /// </summary>
    internal static class RefreshNow
    {
        public static void Run()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var service = MetadataService.Current;
            if (service == null)
            {
                return;
            }

            service.Caches.InvalidateAll();
            DefinitionPreview.Invalidate();
            Log.Write("Metadata dropped; reloading it.");
            var cache = service.CacheForActiveWindow(out _);
            if (cache == null)
            {
                SetStatus("SQL Prompter: metadata dropped; it loads when a connected window is active.");
                return;
            }

            SetStatus("SQL Prompter: refreshing metadata...");
            EventHandler done = null;
            done = (s, e) =>
            {
                MetadataService.SnapshotReplaced -= done;
#pragma warning disable VSSDK007 // fire and forget on purpose: the load must not wait for the status bar
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    SetStatus("SQL Prompter: metadata refreshed.");
                }).Task.FileAndForget("sqlprompter/refresh");
#pragma warning restore VSSDK007
            };
            MetadataService.SnapshotReplaced += done;
        }

        private static void SetStatus(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText(text);
            }
        }
    }
}
