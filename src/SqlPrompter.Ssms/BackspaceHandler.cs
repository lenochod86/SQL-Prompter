using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Backspace in a list invoked explicitly (Ctrl+Space) before anything was typed: the character before the list is deleted, so the list
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

            return false;
        }
    }
}
