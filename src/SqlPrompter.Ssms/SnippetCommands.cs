using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Ssms
{
    /// <summary>The adornment layer the fields of a snippet are highlighted in, above the text.</summary>
    internal static class SnippetLayers
    {
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(SnippetSession.LayerName)]
        [Order(After = PredefinedAdornmentLayers.Selection, Before = PredefinedAdornmentLayers.Text)]
        [TextViewRole(PredefinedTextViewRoles.Document)]
#pragma warning disable CS0649 // set by MEF
        public static AdornmentLayerDefinition FieldLayer;
#pragma warning restore CS0649
    }

    /// <summary>Tab, Shift+Tab, Enter and Esc while the fields of a snippet are edited; every other time they pass through.</summary>
    internal abstract class SnippetCommandHandler<T> : ICommandHandler<T>
        where T : EditorCommandArgs
    {
        public abstract string DisplayName { get; }

        public CommandState GetCommandState(T args) =>
            SnippetSession.IsActive(args.TextView) && !ListOpen(args.TextView) ? CommandState.Available : CommandState.Unspecified;

        public bool ExecuteCommand(T args, CommandExecutionContext executionContext)
        {
            var session = SnippetSession.Of(args.TextView);
            if (session == null || ListOpen(args.TextView))
            {
                // A suggestion list is open in a field: Tab and Enter insert its item and Esc closes it; the next key moves on (#551).
                return false;
            }

            Run(session);
            return true;
        }

        protected abstract void Run(SnippetSession session);

        /// <summary>True while a suggestion list is shown: the list of SQL Prompter or the editor's session in our presenter.</summary>
        private static bool ListOpen(ITextView view) =>
            InvokedList.Of(view)?.IsOpen == true ||
            (view.Properties.TryGetProperty(typeof(SqlCompletionPresenter), out SqlCompletionPresenter presenter) && presenter.IsOpen);
    }

    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.SnippetTab")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class SnippetTabHandler : SnippetCommandHandler<TabKeyCommandArgs>
    {
        public override string DisplayName => "SQL Prompter: next snippet field";

        protected override void Run(SnippetSession session) => session.Next();
    }

    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.SnippetBackTab")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class SnippetBackTabHandler : SnippetCommandHandler<BackTabKeyCommandArgs>
    {
        public override string DisplayName => "SQL Prompter: previous snippet field";

        protected override void Run(SnippetSession session) => session.Previous();
    }

    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.SnippetReturn")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class SnippetReturnHandler : SnippetCommandHandler<ReturnKeyCommandArgs>
    {
        public override string DisplayName => "SQL Prompter: next snippet field or finish";

        protected override void Run(SnippetSession session) => session.Advance();
    }

    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.SnippetEscape")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class SnippetEscapeHandler : SnippetCommandHandler<EscapeKeyCommandArgs>
    {
        public override string DisplayName => "SQL Prompter: finish snippet";

        protected override void Run(SnippetSession session) => session.Finish();
    }
}
