using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
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
    /// "Value does not fall within the expected range" (#85).
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.Backspace")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class BackspaceHandler : ICommandHandler<BackspaceKeyCommandArgs>
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public string DisplayName => "SQL Prompter: Backspace in an empty list";

        public CommandState GetCommandState(BackspaceKeyCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(BackspaceKeyCommandArgs args, CommandExecutionContext executionContext)
        {
            var session = Broker.GetSession(args.TextView);
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
                    return true;
                }
            }

            return false;
        }
    }
}
