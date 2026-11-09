using System;
using System.Reflection;
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
    /// Ctrl+F12: selects the object under the caret in Object Explorer (#222). The object is found in the catalog in the background (its type and the
    /// name of the server as Object Explorer names it); then <c>IObjectExplorerService.FindNode(urn)</c> and <c>SynchronizeTree(node)</c> of SSMS
    /// (called through reflection: the interfaces are SSMS's own) select and expand the node. When Object Explorer has no node for it (another server,
    /// not connected there) the qualified name is copied and the status bar says so.
    /// </summary>
    internal static class LocateInObjectExplorer
    {
        private const string ServiceTypeName = "Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.IObjectExplorerService";
        private const string ApplicationName = "SQL Prompter";

        /// <summary>Whether the Object Explorer service of this SSMS has what is needed.</summary>
        public static string Problem()
        {
            var type = AssemblyTypes.FindLoaded(ServiceTypeName);
            if (type == null)
            {
                return "the type IObjectExplorerService was not found";
            }

            return type.GetMethod("FindNode", new[] { typeof(string) }) == null || type.GetMethod("SynchronizeTree") == null ? "FindNode or SynchronizeTree is missing" : null;
        }

        public static void Run(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                if (!SsmsCompatibility.Has(SqlPrompter.Core.Compatibility.SsmsFeature.ObjectExplorer))
                {
                    Tell("SQL Prompter: this version of SSMS has no Object Explorer service that SQL Prompter can use.");
                    return;
                }

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
                var locator = new ObjectLocator(() => SqlClientConnections.Create(target.BuildConnectionString(ApplicationName)));
                Tell("SQL Prompter: looking for " + reference + " in Object Explorer...");
#pragma warning disable VSSDK007 // fire and forget on purpose: the handler must return at once
                ThreadHelper.JoinableTaskFactory.RunAsync(() => ShowAsync(locator, reference, database)).Task.FileAndForget("sqlprompter/locate");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Log.Error("Locating the object in Object Explorer failed", ex);
            }
        }

        private static async Task ShowAsync(ObjectLocator locator, ObjectReference reference, string database)
        {
            LocatedObject found = null;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    found = await Task.Run(() => locator.LocateAsync(database, reference.Schema, reference.Name, timeout.Token)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Looking for " + reference + " failed", ex);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (found == null)
            {
                Tell("SQL Prompter: " + reference + " was not found.");
                return;
            }

            var path = ObjectExplorerPath.Qualified(found.Database, found.Schema, found.Name);
            var urn = ObjectExplorerPath.Urn(found);
            if (urn == null)
            {
                Copy(path);
                Tell("SQL Prompter: " + path + " has no node that can be selected here; the name was copied.");
                return;
            }

            if (Select(urn))
            {
                Tell("SQL Prompter: " + path + " selected in Object Explorer.");
                Log.Write("Locate in Object Explorer: " + urn);
            }
            else
            {
                Copy(path);
                Tell("SQL Prompter: Object Explorer has no node for " + path + " (is the server connected there?); the name was copied.");
            }
        }

        /// <summary>Selects the node and brings Object Explorer to the front; false when there is no such node.</summary>
        private static bool Select(string urn)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var type = AssemblyTypes.FindLoaded(ServiceTypeName);
                var service = type == null ? null : Package.GetGlobalService(type);
                if (service == null)
                {
                    return false;
                }

                var node = type.GetMethod("FindNode", new[] { typeof(string) }).Invoke(service, new object[] { urn });
                if (node == null)
                {
                    return false;
                }

                type.GetMethod("SynchronizeTree").Invoke(service, new[] { node });
                try
                {
                    (Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ExecuteCommand("View.ObjectExplorer");
                }
                catch (Exception ex)
                {
                    Log.Write("Showing Object Explorer failed: " + ex.Message);
                }

                return true;
            }
            catch (TargetInvocationException ex)
            {
                Log.Error("Selecting the node " + urn + " failed", ex.InnerException ?? ex);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Selecting the node " + urn + " failed", ex);
                return false;
            }
        }

        private static void Copy(string text)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                Log.Write("Copying the name failed: " + ex.Message);
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
