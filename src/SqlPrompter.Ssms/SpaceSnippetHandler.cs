using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Space after a complete snippet prefix where the editor selects only softly (a place where an alias may be typed, e.g. after a FROM
    /// clause): the editor then does not commit on typed characters, so the snippet would stay unexpanded. When the typed word is exactly
    /// the prefix of a snippet in the list, Space expands it, as it does elsewhere.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.SpaceSnippet")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = PredefinedCompletionNames.CompletionCommandHandler)]
    internal sealed class SpaceSnippetHandler : ICommandHandler<TypeCharCommandArgs>
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public string DisplayName => "SQL Prompter: Space expands a snippet";

        public CommandState GetCommandState(TypeCharCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(TypeCharCommandArgs args, CommandExecutionContext executionContext)
        {
            try
            {
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                if (args.TypedChar != ' ' || !SettingsService.Current.Behavior.CommitOnSpace)
                {
                    return false;
                }

                var session = Broker.GetSession(args.TextView);
                if (session == null || session.IsDismissed || !session.Properties.TryGetProperty(SqlCompletionSource.HardSelectionKey, out bool hard) || hard)
                {
                    return false;
                }

                var buffer = args.SubjectBuffer;
                var typed = session.ApplicableToSpan.GetText(buffer.CurrentSnapshot);
                var snippet = session.GetComputedItems(CancellationToken.None).Items
                    .Where(i => string.Equals(i.InsertText, typed, StringComparison.OrdinalIgnoreCase) || string.Equals(i.DisplayText, typed, StringComparison.OrdinalIgnoreCase))
                    .Select(i => i.Properties.TryGetProperty(SqlCompletionSource.SnippetKey, out Snippet s) ? s : null)
                    .FirstOrDefault(s => s != null);
                if (snippet == null || !SnippetInserter.TryInsert(session, buffer, snippet))
                {
                    return false;
                }

                session.Dismiss();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Expanding a snippet on Space failed", ex);
                return false;
            }
        }
    }
}
