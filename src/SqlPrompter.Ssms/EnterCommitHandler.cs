using System.ComponentModel.Composition;
using System.Threading;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Enter in a list invoked explicitly (Ctrl+Space) before anything was typed: the editor selects the first item only softly then, so Enter
    /// would just add a line. Where the context is not one for a name of the user's own (aliases), Enter inserts the selected item (#85).
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.EnterCommit")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = PredefinedCompletionNames.CompletionCommandHandler)]
    internal sealed class EnterCommitHandler : ICommandHandler<ReturnKeyCommandArgs>
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public string DisplayName => "SQL Prompter: Enter inserts the selected suggestion";

        public CommandState GetCommandState(ReturnKeyCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(ReturnKeyCommandArgs args, CommandExecutionContext executionContext)
        {
            var session = Broker.GetSession(args.TextView);
            if (session == null)
            {
                return false;
            }

            var hard = session.Properties.TryGetProperty(SqlCompletionSource.HardSelectionKey, out bool hardValue) && hardValue;
            var ownListOpen = InvokedList.Of(args.TextView)?.IsOpen == true;
            var items = session.IsDismissed ? null : session.GetComputedItems(CancellationToken.None);
            // The list SQL Prompter runs itself (opened after a snippet, by Ctrl+Space) has the selection the user sees; it handles Enter itself (#517).
            if (items == null || !SqlPrompter.Core.Completion.EnterCommitRule.EditorSessionShouldCommit(ownListOpen, session.IsDismissed, hard, items.SuggestionItemSelected, items.SelectedItem != null))
            {
                return false;
            }

            // '\t': the commit manager treats it like Enter and the editor does not type it.
            session.Commit('\t', CancellationToken.None);
            return true;
        }
    }
}
