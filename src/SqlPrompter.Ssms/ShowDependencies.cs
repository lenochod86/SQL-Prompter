using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Navigation;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Show Dependencies (#222): what the object at the caret uses and what uses it, from <c>sys.sql_expression_dependencies</c>, read in the
    /// background and listed in the popup; Enter opens the script of an object of the same database.
    /// </summary>
    internal static class ShowDependencies
    {
        private const string ApplicationName = "SQL Prompter";

        public static void Run(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                var snapshot = view.TextBuffer.CurrentSnapshot;
                var caret = view.Caret.Position.BufferPosition.Position;
                var reference = ObjectReferenceFinder.Find(snapshot.GetText(), caret);
                var connection = SsmsConnection.GetActive();
                if (reference == null || connection == null || !connection.CanConnect)
                {
                    Tell(reference == null ? "SQL Prompter: no table, view or routine under the caret." : "SQL Prompter: the window is not connected.");
                    return;
                }

                var database = reference.Database ?? SqlBatch.FindUsedDatabase(snapshot, caret) ?? connection.Database;
                var target = string.IsNullOrEmpty(database) ? connection : connection.WithDatabase(database);
                var loader = new DependencyLoader(() => SqlClientConnections.Create(target.BuildConnectionString(ApplicationName)));
                Tell("SQL Prompter: reading the dependencies of " + reference + "...");
#pragma warning disable VSSDK007 // fire and forget on purpose
                ThreadHelper.JoinableTaskFactory.RunAsync(() => ShowAsync(view, loader, reference)).Task.FileAndForget("sqlprompter/dependencies");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Log.Error("Show dependencies failed", ex);
            }
        }

        private static async Task ShowAsync(IWpfTextView view, DependencyLoader loader, ObjectReference reference)
        {
            DependencyResult result = null;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    result = await Task.Run(() => loader.LoadAsync(null, reference.Schema, reference.Name, timeout.Token)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reading the dependencies of " + reference + " failed", ex);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (result == null)
            {
                Tell("SQL Prompter: the dependencies of " + reference + " could not be read (see the log).");
                return;
            }

            var items = new List<Tuple<string, string, Action>>();
            Add(items, view, "Uses", result.Uses);
            Add(items, view, "Used by", result.UsedBy);
            if (items.Count == 0)
            {
                Tell("SQL Prompter: " + reference + " uses nothing and nothing uses it (as far as SQL Server records).");
                return;
            }

            Tell("SQL Prompter: " + result.Uses.Count + " used, " + result.UsedBy.Count + " using " + reference + ".");
            QuickActionsPopup.ShowItems(view, items, "Dependencies of " + reference + ": type to filter; Enter opens the object, Esc closes");
        }

        private static void Add(List<Tuple<string, string, Action>> items, IWpfTextView view, string heading, IReadOnlyList<DependencyItem> list)
        {
            foreach (var dependency in list)
            {
                var item = dependency;
                items.Add(Tuple.Create<string, string, Action>(heading + ": " + item.FullName, item.KindText, () =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (!string.IsNullOrEmpty(item.Database))
                    {
                        Tell("SQL Prompter: " + item.FullName + " is in another database; open it from a window of that database.");
                        return;
                    }

                    GoToDefinitionHandler.OpenObject(view, item.Schema, item.Name);
                }));
            }
        }

        private static void Tell(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText(text);
            }
        }
    }
}
