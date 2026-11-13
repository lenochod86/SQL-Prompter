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

        public static void Tell(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetStatus(text);
        }

        /// <summary>Opens the script of a named object of the database of the window in a new window (quick open, #223).</summary>
        public static void OpenObject(ITextView view, string schema, string name, int line = 0)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var connection = SsmsConnection.GetActive();
                if (connection == null || !connection.CanConnect)
                {
                    SetStatus("SQL Prompter: the window is not connected.");
                    return;
                }

                var snapshot = view.TextBuffer.CurrentSnapshot;
                var database = SqlBatch.FindUsedDatabase(snapshot, view.Caret.Position.BufferPosition.Position) ?? connection.Database;
                var target = string.IsNullOrEmpty(database) ? connection : connection.WithDatabase(database);
                var loader = new DefinitionLoader(() => SqlClientConnections.Create(target.BuildConnectionString(ApplicationName)));
                var reference = new ObjectReference(database, schema, name);
                SetStatus("SQL Prompter: loading " + reference + "...");
#pragma warning disable VSSDK007 // fire and forget on purpose
                ThreadHelper.JoinableTaskFactory.RunAsync(() => OpenAsync(loader, reference, database, line)).Task.FileAndForget("sqlprompter/quickopen");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Log.Error("Quick open failed", ex);
            }
        }

        private static async Task OpenAsync(DefinitionLoader loader, ObjectReference reference, string database, int line = 0)
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
                TabSessionService.Current?.NameDefinitionTab(definition.Schema, definition.Name);
                SetStatus("SQL Prompter: opened " + definition.Schema + "." + definition.Name + ".");
                if (line > 1)
                {
                    await Task.Delay(600);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var opened = ActiveEditor.GetView();
                    if (opened != null && line <= opened.TextSnapshot.LineCount)
                    {
                        var start = opened.TextSnapshot.GetLineFromLineNumber(line - 1).Start;
                        opened.Caret.MoveTo(start);
                        opened.ViewScroller.EnsureSpanVisible(new Microsoft.VisualStudio.Text.SnapshotSpan(start, 0), Microsoft.VisualStudio.Text.Editor.EnsureSpanVisibleOptions.AlwaysCenter);
                    }
                }

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
