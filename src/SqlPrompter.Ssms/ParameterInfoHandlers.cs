using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Ssms
{
    /// <summary>Typing "(" or "," shows the parameter info of the call; the character is typed first.</summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.ParameterInfoOnType")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class ParameterInfoTypeHandler : ICommandHandler<TypeCharCommandArgs>
    {
        public string DisplayName => "SQL Prompter: parameter info";

        public CommandState GetCommandState(TypeCharCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(TypeCharCommandArgs args, CommandExecutionContext executionContext)
        {
            if (args.TypedChar != '(' && args.TypedChar != ',')
            {
                return false;
            }

            // The editor types the character in its own handler, which runs after this one: show the info once that has happened.
            var view = args.TextView as IWpfTextView;
            if (view != null)
            {
#pragma warning disable VSSDK007 // fire and forget on purpose: the info is shown after the typed character has been processed
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Yield();
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    try
                    {
                        ParameterInfoPopup.Show(view);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Parameter info failed", ex);
                    }
                }).Task.FileAndForget("SqlPrompter/ParameterInfo");
#pragma warning restore VSSDK007
            }

            return false;
        }
    }

    /// <summary>Esc closes the parameter info and still reaches the other handlers (a list of suggestions closes too).</summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.ParameterInfoEscape")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class ParameterInfoEscapeHandler : ICommandHandler<EscapeKeyCommandArgs>
    {
        public string DisplayName => "SQL Prompter: close parameter info";

        public CommandState GetCommandState(EscapeKeyCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(EscapeKeyCommandArgs args, CommandExecutionContext executionContext)
        {
            ParameterInfoPopup.Hide(args.TextView);
            return false;
        }
    }
}
