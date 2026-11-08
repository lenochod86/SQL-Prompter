using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Editing;

#pragma warning disable VSTHRD010 // the metadata of the script is asked through MetadataOf, which switches to the UI thread itself
namespace SqlPrompter.Ssms
{
    /// <summary>The light bulb of the editor (Ctrl+. and the bulb at the caret) offers the repair of the code analysis issue under the caret and the repair of all (#208).</summary>
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("SqlPrompter.CodeFixes")]
    [ContentType(SqlContentType.Name)]
    internal sealed class CodeFixSuggestedActionsProvider : ISuggestedActionsSourceProvider
    {
        public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new CodeFixSuggestedActionsSource(textView, textBuffer));
    }

    internal sealed class CodeFixSuggestedActionsSource : ISuggestedActionsSource
    {
        private readonly ITextView view;
        private readonly ITextBuffer buffer;

        public CodeFixSuggestedActionsSource(ITextView view, ITextBuffer buffer)
        {
            this.view = view;
            this.buffer = buffer;
        }

#pragma warning disable CS0067 // never raised: the actions are computed when they are asked for
        public event EventHandler<EventArgs> SuggestedActionsChanged;
#pragma warning restore CS0067

        private IReadOnlyList<CodeIssue> IssuesAt(SnapshotSpan range)
        {
            var settings = SettingsService.Current.Analysis;
            if (!settings.Enabled)
            {
                return new CodeIssue[0];
            }

            var text = range.Snapshot.GetText();
            var all = CodeAnalyzer.Analyze(text, settings, MetadataOf(text));
            return all.Where(i => i.Fix != null && i.Start <= range.End.Position && range.Start.Position <= i.Start + i.Length).ToList();
        }

        /// <summary>The metadata of the database of the script; asked on the UI thread (the connection of the window is read there), never waits for the database.</summary>
        internal static SqlPrompter.Core.Model.IDatabaseMetadata MetadataOf(string text)
        {
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                return MetadataService.Current?.MetadataForScript(text);
            });
        }

        public Task<bool> HasSuggestedActionsAsync(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken) =>
            Task.Run(() =>
            {
                try
                {
                    return IssuesAt(range).Count > 0;
                }
                catch (Exception ex)
                {
                    Log.Error("Looking for the repairs under the caret failed", ex);
                    return false;
                }
            }, cancellationToken);

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            var actions = new List<ISuggestedAction>();
            try
            {
                foreach (var issue in IssuesAt(range).Take(5))
                {
                    actions.Add(new FixAction(buffer, range.Snapshot, issue));
                }

                if (actions.Count > 0)
                {
                    actions.Add(new FixAllAction(buffer, range.Snapshot));
                }
            }
            catch (Exception ex)
            {
                Log.Error("Making the repairs of the light bulb failed", ex);
            }

            return actions.Count == 0 ? Enumerable.Empty<SuggestedActionSet>() : new[] { new SuggestedActionSet(PredefinedSuggestedActionCategoryNames.CodeFix, actions) };
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose()
        {
        }
    }

    internal abstract class FixActionBase : ISuggestedAction
    {
        protected FixActionBase(ITextBuffer buffer, ITextSnapshot snapshot)
        {
            Buffer = buffer;
            Snapshot = snapshot;
        }

        protected ITextBuffer Buffer { get; }

        protected ITextSnapshot Snapshot { get; }

        public abstract string DisplayText { get; }

        public bool HasActionSets => false;

        public bool HasPreview => false;

        public string IconAutomationText => null;

        public ImageMoniker IconMoniker => default(ImageMoniker);

        public string InputGestureText => null;

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<SuggestedActionSet>>(null);

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken) => Task.FromResult<object>(null);

        public abstract void Invoke(CancellationToken cancellationToken);

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose()
        {
        }
    }

    internal sealed class FixAction : FixActionBase
    {
        private readonly CodeIssue issue;

        public FixAction(ITextBuffer buffer, ITextSnapshot snapshot, CodeIssue issue)
            : base(buffer, snapshot)
        {
            this.issue = issue;
        }

        public override string DisplayText => "Fix: " + Short(issue.Message);

        private static string Short(string message)
        {
            var cut = message.IndexOf(';');
            if (cut < 0)
            {
                cut = message.IndexOf('.');
            }

            return cut > 0 ? message.Substring(0, cut) : message;
        }

        public override void Invoke(CancellationToken cancellationToken)
        {
            var fix = issue.Fix;
            // One edit: one Undo. Nothing is done when the text changed since the actions were made.
            if (Buffer.CurrentSnapshot.Version.VersionNumber == Snapshot.Version.VersionNumber)
            {
                Buffer.Replace(new Span(fix.Start, fix.Length), fix.Replacement);
            }
        }
    }

    internal sealed class FixAllAction : FixActionBase
    {
        public FixAllAction(ITextBuffer buffer, ITextSnapshot snapshot)
            : base(buffer, snapshot)
        {
        }

        public override string DisplayText => "Fix all code analysis issues in the script";

        public override void Invoke(CancellationToken cancellationToken)
        {
            var current = Buffer.CurrentSnapshot;
            var fixedText = CodeFixes.Apply(current.GetText(), SettingsService.Current.Analysis, CodeFixSuggestedActionsSource.MetadataOf(current.GetText()));
            if (fixedText != current.GetText())
            {
                Buffer.Replace(new Span(0, current.Length), fixedText);
            }
        }
    }
}
