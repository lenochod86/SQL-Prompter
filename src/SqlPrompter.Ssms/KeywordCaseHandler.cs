using System;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Formatting;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// "Case keywords while typing" (off by default, Formatting options): when a space, parenthesis, comma or semicolon is typed right after a
    /// reserved word, the word gets the keyword case of the formatting style. Strings, comments, names after a dot and variables are left alone.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.KeywordCase")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class KeywordCaseHandler : ICommandHandler<TypeCharCommandArgs>
    {
        public string DisplayName => "SQL Prompter: case keywords while typing";

        public CommandState GetCommandState(TypeCharCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(TypeCharCommandArgs args, CommandExecutionContext executionContext)
        {
            var typed = args.TypedChar;
            if (typed != ' ' && typed != '(' && typed != ',' && typed != ';' && typed != '\t')
            {
                return false;
            }

            var style = SettingsService.Current.Formatting;
            if (!style.CaseKeywordsWhileTyping || style.KeywordCase == CaseStyle.Keep)
            {
                return false;
            }

            try
            {
                var view = args.TextView;
                if (!view.Selection.IsEmpty)
                {
                    return false;
                }

                var caret = view.Caret.Position.BufferPosition;
                var snapshot = caret.Snapshot;
                if (caret.Position == 0 || !(char.IsLetter(snapshot[caret.Position - 1]) || snapshot[caret.Position - 1] == '_'))
                {
                    return false;
                }

                if (!SqlBatch.TryGet(snapshot, caret.Position, out var batchStart, out var batch))
                {
                    return false;
                }

                var position = caret.Position - batchStart;
                var tokens = Lexer.Tokenize(batch);
                var index = -1;
                for (var i = 0; i < tokens.Count; i++)
                {
                    if (tokens[i].End == position && tokens[i].Kind == TokenKind.Word)
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0 || !Keywords.IsReserved(tokens[index].Text) || (index > 0 && tokens[index - 1].Kind == TokenKind.Dot))
                {
                    return false;
                }

                var cased = style.KeywordCase == CaseStyle.Upper ? tokens[index].Text.ToUpperInvariant() : tokens[index].Text.ToLowerInvariant();
                if (cased == tokens[index].Text)
                {
                    return false;
                }

                var edit = view.TextBuffer.Replace(new Span(batchStart + tokens[index].Start, tokens[index].End - tokens[index].Start), cased);
                view.Caret.MoveTo(new SnapshotPoint(edit, batchStart + tokens[index].End));
            }
            catch (Exception ex)
            {
                Log.Error("Casing the keyword failed", ex);
            }

            return false;
        }
    }
}
