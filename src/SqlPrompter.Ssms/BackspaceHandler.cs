using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Backspace between an empty auto-closed pair removes both characters. Backspace in a list invoked explicitly (Ctrl+Space) before anything was typed: the character before the list is deleted, so the list
    /// is dismissed first. Otherwise SSMS's own language service (<c>RadLangSvc.SqlCompletionSet.GetTextTypedSoFar</c>) fails with
    /// "Value does not fall within the expected range" (#85). After Backspace or Delete with a list open the list is made again for the new caret
    /// position (#352): the editor's session only filters the items of the old position, which are wrong once the context changed (the columns of
    /// <c>dbo.Cities</c> still shown after deleting back to <c>dbo.Citi</c>).
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.Backspace")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class BackspaceHandler : IChainedCommandHandler<BackspaceKeyCommandArgs>, IChainedCommandHandler<DeleteKeyCommandArgs>
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public string DisplayName => "SQL Prompter: Backspace in an empty list";

        public CommandState GetCommandState(BackspaceKeyCommandArgs args, Func<CommandState> nextCommandHandler) => nextCommandHandler();

        public CommandState GetCommandState(DeleteKeyCommandArgs args, Func<CommandState> nextCommandHandler) => nextCommandHandler();

        public void ExecuteCommand(BackspaceKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var session = Broker.GetSession(args.TextView);
            var listWasOpen = session != null && !session.IsDismissed;
            if (session != null && session.ApplicableToSpan.GetSpan(args.TextView.TextSnapshot).Length == 0)
            {
                session.Dismiss();
            }

            // Backspace between an empty pair of ' [ ( removes both characters (the pair the auto-closing inserted).
            var view = args.TextView;
            if (SettingsService.Current.Behavior.AutoClose && view.Selection.IsEmpty)
            {
                var caret = view.Caret.Position.BufferPosition;
                if (caret.Position > 0 && caret.Position < caret.Snapshot.Length && AutoClose.DeletesPair(caret.Snapshot.GetText(), caret.Position))
                {
                    view.TextBuffer.Delete(new Span(caret.Position - 1, 2));
                    AskAgain(view, listWasOpen);
                    return;
                }
            }

            nextCommandHandler();
            AskAgain(view, listWasOpen);
        }

        public void ExecuteCommand(DeleteKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var session = Broker.GetSession(args.TextView);
            var listWasOpen = session != null && !session.IsDismissed;
            nextCommandHandler();
            AskAgain(args.TextView, listWasOpen);
        }

        /// <summary>
        /// The editor's session of the position before the deletion is replaced by our list for the new position (the same code as Ctrl+Space: the
        /// context is analyzed again, so a qualifier or a statement position that changed gives the items that belong to it).
        /// </summary>
        private void AskAgain(ITextView view, bool listWasOpen)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!listWasOpen)
            {
                return;
            }

            try
            {
                Broker.GetSession(view)?.Dismiss();
                if (view is IWpfTextView wpf)
                {
                    InvokedList.Open(wpf);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Making the suggestion list again after a deletion failed", ex);
            }
        }
    }
}
