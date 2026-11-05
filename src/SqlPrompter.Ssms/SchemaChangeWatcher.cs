using System;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// When the user runs a script that creates, alters or drops objects, checks the window's database for changes
    /// several times over the following seconds (execution is asynchronous and may take a while), bypassing the
    /// regular interval, so that new tables and columns show up in completion right away.
    /// </summary>
    internal sealed class SchemaChangeWatcher
    {
        private const string ExecuteCommand = "Query.Execute";
        private static readonly int[] CheckDelaysMs = { 1000, 3000, 10000, 30000 };

        private readonly DTE dte;

        // DTE events are collected by the GC unless a reference is kept.
        private CommandEvents events;

        private SchemaChangeWatcher(DTE dte)
        {
            this.dte = dte;
        }

        public static SchemaChangeWatcher Start(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var watcher = new SchemaChangeWatcher(dte);
            Command command;
            try
            {
                command = dte.Commands.Item(ExecuteCommand);
            }
            catch (ArgumentException)
            {
                Log.Write("Command " + ExecuteCommand + " not found; metadata is not refreshed after DDL.");
                return null;
            }

            watcher.events = dte.Events.CommandEvents[command.Guid, command.ID];
            watcher.events.BeforeExecute += watcher.OnBeforeExecute;
            Log.Write("Watching " + ExecuteCommand + " for schema changes.");
            return watcher;
        }

        private void OnBeforeExecute(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var script = ExecutedText(dte);
                if (script == null)
                {
                    return;
                }

                var service = MetadataService.Current;
                if (service == null)
                {
                    return;
                }

                var cache = service.CacheForActiveWindow(out var loader);
                if (cache == null)
                {
                    return;
                }

                _ = Task.Run(() => CheckAfterExecutionAsync(script, cache, loader));
            }
            catch (Exception ex)
            {
                Log.Error("Watching query execution failed", ex);
            }
        }

        /// <summary>The selection if there is one, otherwise the whole document, as SSMS executes it.</summary>
        internal static string ExecutedText(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = dte.ActiveDocument;
            if (document == null)
            {
                return null;
            }

            if (document.Selection is TextSelection selection && !selection.IsEmpty)
            {
                return selection.Text;
            }

            return document.Object("TextDocument") is TextDocument text
                ? text.StartPoint.CreateEditPoint().GetText(text.EndPoint)
                : null;
        }

        private static async Task CheckAfterExecutionAsync(string script, DatabaseMetadataCache cache, IMetadataLoader loader)
        {
            try
            {
                if (!SchemaChangeDetector.ChangesSchema(script))
                {
                    return;
                }

                var elapsed = 0;
                foreach (var delay in CheckDelaysMs)
                {
                    await Task.Delay(delay - elapsed).ConfigureAwait(false);
                    elapsed = delay;
                    await cache.CheckForChanges(loader, force: true).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Refreshing metadata after DDL failed", ex);
            }
        }
    }
}
