using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Auto-closing of <c>'</c>, <c>[</c> and <c>(</c>. The decision is made by <see cref="AutoClose"/> from the text before the character is typed.
    /// A closing partner is inserted after the editor has typed the character; a closing character that is already after the caret is
    /// stepped over instead of typed.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.AutoClose")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class AutoCloseHandler : ICommandHandler<TypeCharCommandArgs>
    {
        public string DisplayName => "SQL Prompter: auto-close";

        public CommandState GetCommandState(TypeCharCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(TypeCharCommandArgs args, CommandExecutionContext executionContext)
        {
            var typed = args.TypedChar;
            var pairs = SettingsService.Current.Behavior.AutoClosePairs;
            if ((typed != '\'' && typed != '[' && typed != ']' && typed != '(' && typed != ')' && typed != '*' && typed != '/') || pairs == AutoClosePairs.None)
            {
                return false;
            }

            var view = args.TextView;
            if (!view.Selection.IsEmpty || view.Selection.Mode != TextSelectionMode.Stream)
            {
                return false;
            }

            try
            {
                var caret = view.Caret.Position.BufferPosition;
                var action = AutoClose.Decide(caret.Snapshot.GetText(), caret.Position, typed, pairs);
                if (action == AutoCloseAction.Overtype)
                {
                    view.Caret.MoveTo(new SnapshotPoint(caret.Snapshot, caret.Position + 1));
                    return true;
                }

                if (action == AutoCloseAction.OvertypeCommentStar)
                {
                    // The closing  */  of a comment that was closed automatically: step over its space (if any) and star.
                    var skip = caret.Snapshot[caret.Position] == ' ' ? 2 : 1;
                    view.Caret.MoveTo(new SnapshotPoint(caret.Snapshot, caret.Position + skip));
                    return true;
                }

                if (action == AutoCloseAction.InsertPair)
                {
                    // The editor types the character in its own handler, which runs after this one: add the closing partner once the
                    // very next change of the buffer (that character) has happened.
                    var closing = typed == '[' ? "]" : typed == '(' ? ")" : typed == '*' ? AutoClose.BlockCommentClosing : "'";
                    var position = caret.Position;
                    var nextWas = position < caret.Snapshot.Length ? caret.Snapshot[position] : '\0';
                    var buffer = view.TextBuffer;
                    var expected = caret.Snapshot.Version.VersionNumber + 1;
                    EventHandler handler = null;
                    handler = (s, e) =>
                    {
                        buffer.PostChanged -= handler;
                        if (buffer.CurrentSnapshot.Version.VersionNumber != expected)
                        {
                            return;
                        }
#pragma warning disable VSSDK007 // fire and forget on purpose: the closing character is added after the typed one has been processed
                        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                        {
                            await System.Threading.Tasks.Task.Yield();
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            InsertClosing(view, position, typed, closing, nextWas);
                        }).Task.FileAndForget("SqlPrompter/AutoClose");
#pragma warning restore VSSDK007
                    };
                    buffer.PostChanged += handler;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Auto-close failed", ex);
            }

            return false;
        }

        private static void InsertClosing(ITextView view, int typedAt, char opening, string closing, char nextWas)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var snapshot = view.TextBuffer.CurrentSnapshot;
                var after = typedAt + 1;

                // Only when the character really was typed and the caret is right behind it (nothing else happened in the meantime).
                if (after > snapshot.Length || snapshot[typedAt] != opening || view.Caret.Position.BufferPosition.Position != after)
                {
                    return;
                }

                // SSMS may close a parenthesis by itself (an option of its editor): then the closing one is already there.
                if (opening == '(' && after < snapshot.Length && snapshot[after] == ')' && nextWas != ')')
                {
                    return;
                }

                var edited = view.TextBuffer.Insert(after, closing);
                view.Caret.MoveTo(new SnapshotPoint(edited, after));
            }
            catch (Exception ex)
            {
                Log.Error("Inserting the closing character failed", ex);
            }
        }
    }
}
