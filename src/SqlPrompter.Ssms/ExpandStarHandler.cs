using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Tab right after a <c>*</c> (or <c>alias.*</c>) in a select list replaces it with the column list, one column per line. Everywhere else, and
    /// while a list or the fields of a snippet are active, Tab behaves as before.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SqlPrompter.ExpandStar")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Order(Before = "default")]
    internal sealed class ExpandStarHandler : ICommandHandler<TabKeyCommandArgs>
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public string DisplayName => "SQL Prompter: expand * into the column list";

        public CommandState GetCommandState(TabKeyCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(TabKeyCommandArgs args, CommandExecutionContext executionContext)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var view = args.TextView;
                var expansion = Find(view, Broker, out var start);
                if (expansion == null)
                {
                    return false;
                }

                view.TextBuffer.Replace(new Span(start, expansion.Length), expansion.Text);
                view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, start + expansion.Text.Length));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Expanding * failed", ex);
                return false;
            }
        }

        /// <summary>
        /// The expansion Tab would make at the caret, or null when Tab does nothing special there. <paramref name="start"/> is the position in the
        /// buffer where the replaced text starts. The hint that tells the user about Tab (<see cref="StarHint"/>) asks the same question.
        /// </summary>
        internal static StarExpansion Find(ITextView view, IAsyncCompletionBroker broker, out int start)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            start = 0;
            var caret = view.Caret.Position.BufferPosition;
            var snapshot = caret.Snapshot;
            if (caret.Position == 0 || snapshot[caret.Position - 1] != '*' || !view.Selection.IsEmpty ||
                SnippetSession.IsActive(view) || broker.GetSession(view) != null)
            {
                return null;
            }

            if (!SqlBatch.TryGet(snapshot, caret.Position, out var batchStart, out var batch))
            {
                return null;
            }

            var service = MetadataService.Current;
            var cache = service?.CacheForActiveWindow(out _, SqlBatch.FindUsedDatabase(snapshot, caret.Position));
            var metadata = cache?.Current;
            if (metadata == null)
            {
                return null;
            }

            var caretInBatch = caret.Position - batchStart;
            var script = ScriptModel.Build(batch, caretInBatch);
            var options = SettingsService.Current.ToSuggestionOptions().WithEditor(EditorLayouts.Read(view, snapshot, caret.Position));
            var expansion = StarExpander.TryExpand(batch, caretInBatch, metadata, script, options);
            if (expansion == null)
            {
                return null;
            }

            // The text is aligned with the first column, so the layout is read where the replaced text starts (alias.* starts earlier).
            start = batchStart + expansion.Start;
            return StarExpander.TryExpand(batch, caretInBatch, metadata, script, options.WithEditor(EditorLayouts.Read(view, snapshot, start))) ?? expansion;
        }
    }
}
