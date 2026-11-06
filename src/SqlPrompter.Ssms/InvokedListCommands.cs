using System.ComponentModel.Composition;
using System.Windows.Input;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The keys of the list that SQL Prompter runs itself (<see cref="InvokedList"/>): arrows, Page Up/Down, Tab, Enter, Esc and the typed characters.
    /// A handler of the editor's command chain is used because SSMS does not hand the WPF key events of the editor to extensions. While no such list is
    /// open every handler passes the command on at once.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.InvokedList")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = PredefinedCompletionNames.CompletionCommandHandler)]
    internal sealed class InvokedListCommands :
        ICommandHandler<DownKeyCommandArgs>,
        ICommandHandler<UpKeyCommandArgs>,
        ICommandHandler<PageDownKeyCommandArgs>,
        ICommandHandler<PageUpKeyCommandArgs>,
        ICommandHandler<TabKeyCommandArgs>,
        ICommandHandler<ReturnKeyCommandArgs>,
        ICommandHandler<EscapeKeyCommandArgs>,
        ICommandHandler<TypeCharCommandArgs>
    {
        public string DisplayName => "SQL Prompter: keys of the suggestion list";

        public CommandState GetCommandState(DownKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(UpKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(PageDownKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(PageUpKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(TabKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(ReturnKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(EscapeKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(TypeCharCommandArgs args) => CommandState.Unspecified;


        public bool ExecuteCommand(DownKeyCommandArgs args, CommandExecutionContext context) { ThreadHelper.ThrowIfNotOnUIThread(); return Key(args.TextView, System.Windows.Input.Key.Down); }

        public bool ExecuteCommand(UpKeyCommandArgs args, CommandExecutionContext context) { ThreadHelper.ThrowIfNotOnUIThread(); return Key(args.TextView, System.Windows.Input.Key.Up); }

        public bool ExecuteCommand(PageDownKeyCommandArgs args, CommandExecutionContext context) { ThreadHelper.ThrowIfNotOnUIThread(); return Key(args.TextView, System.Windows.Input.Key.PageDown); }

        public bool ExecuteCommand(PageUpKeyCommandArgs args, CommandExecutionContext context) { ThreadHelper.ThrowIfNotOnUIThread(); return Key(args.TextView, System.Windows.Input.Key.PageUp); }

        public bool ExecuteCommand(TabKeyCommandArgs args, CommandExecutionContext context) { ThreadHelper.ThrowIfNotOnUIThread(); return Key(args.TextView, System.Windows.Input.Key.Tab); }

        public bool ExecuteCommand(ReturnKeyCommandArgs args, CommandExecutionContext context) { ThreadHelper.ThrowIfNotOnUIThread(); return Key(args.TextView, System.Windows.Input.Key.Enter); }

        public bool ExecuteCommand(EscapeKeyCommandArgs args, CommandExecutionContext context) { ThreadHelper.ThrowIfNotOnUIThread(); return Key(args.TextView, System.Windows.Input.Key.Escape); }

        public bool ExecuteCommand(TypeCharCommandArgs args, CommandExecutionContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var list = InvokedList.Of(args.TextView);
            return list != null && list.IsOpen && list.OnTypedChar(args.TypedChar);
        }

        private static bool Key(ITextView view, Key key)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var list = InvokedList.Of(view);
            return list != null && list.IsOpen && list.OnKey(key);
        }
    }
}
