using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Navigation;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// F12: opens the code of the procedure, view, function or trigger under the caret as CREATE OR ALTER, or the CREATE TABLE script of a table,
    /// in a new window on the same server and in the database of the object. The metadata is read in the background.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.GoToDefinition")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class GoToDefinitionHandler : ICommandHandler<GoToDefinitionCommandArgs>
    {
        private const string ApplicationName = "SQL Prompter";

        public string DisplayName => "SQL Prompter: go to definition";

        public CommandState GetCommandState(GoToDefinitionCommandArgs args) => CommandState.Available;

        public bool ExecuteCommand(GoToDefinitionCommandArgs args, CommandExecutionContext executionContext)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Run(args.TextView);
            return true;
        }

        /// <summary>Opens the definition of the object at the caret of the view; the F12 key binding of the menu command calls this too.</summary>
        public static void Run(ITextView view)
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
                    SetStatus(reference == null ? "SQL Prompter: no table, view or routine under the caret." : "SQL Prompter: the window is not connected.");
                    return;
                }

                var database = reference.Database ?? SqlBatch.FindUsedDatabase(snapshot, caret) ?? connection.Database;
                var target = string.IsNullOrEmpty(database) ? connection : connection.WithDatabase(database);
                var loader = new DefinitionLoader(() => SqlClientConnections.Create(target.BuildConnectionString(ApplicationName)));
                SetStatus("SQL Prompter: loading " + reference + "...");
#pragma warning disable VSSDK007 // fire and forget on purpose: the handler must return at once
                ThreadHelper.JoinableTaskFactory.RunAsync(() => OpenAsync(loader, reference, database)).Task.FileAndForget("sqlprompter/gotodefinition");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Log.Error("Go to definition failed", ex);
            }
        }

        private static async Task OpenAsync(DefinitionLoader loader, ObjectReference reference, string database)
        {
            ObjectDefinition definition = null;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    definition = await Task.Run(() => loader.LoadAsync(null, reference.Schema, reference.Name, timeout.Token)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Loading the definition of " + reference + " failed", ex);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (definition == null)
            {
                SetStatus("SQL Prompter: " + reference + " was not found or has no script.");
                return;
            }

            if (SsmsConnection.OpenNewWindow(database, definition.Script))
            {
                SetStatus("SQL Prompter: opened " + definition.Schema + "." + definition.Name + ".");
                Log.Write("Go to definition: " + definition.Schema + "." + definition.Name + " (" + definition.TypeCode + ") in " + database + ".");
            }
            else
            {
                SetStatus("SQL Prompter: the new window could not be opened.");
            }
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
