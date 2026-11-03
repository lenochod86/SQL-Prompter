using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Prototype
{
    /// <summary>
    /// Question 1: if the SQL editor uses IAsyncCompletionSource, it calls GetOrCreate
    /// and the static items "hello" appear in the completion list.
    /// </summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SqlPrompter.Prototype.Hello")]
    [ContentType("any")]
    internal sealed class HelloCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView)
        {
            return textView.Properties.GetOrCreateSingletonProperty(() =>
            {
                Log.Write(
                    "q1",
                    "IAsyncCompletionSourceProvider.GetOrCreate for contentType=" +
                    SqlContentTypes.Describe(textView.TextBuffer.ContentType));
                return new HelloCompletionSource();
            });
        }
    }

    internal sealed class HelloCompletionSource : IAsyncCompletionSource
    {
        public const string Description = "Static suggestion from the SQL Prompter prototype.";

        /// <summary>Question 2: turning it off compares the built-in IntelliSense with and without our source.</summary>
        public static volatile bool Enabled = true;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            if (!Enabled || !SqlContentTypes.IsSql(triggerLocation.Snapshot.ContentType))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            Log.Write(
                "q1",
                string.Format(
                    "InitializeCompletion: reason={0}, char='{1}', contentType=\"{2}\"",
                    trigger.Reason,
                    trigger.Character,
                    triggerLocation.Snapshot.ContentType.TypeName));

            var span = WordSpan(triggerLocation);
            if (trigger.Reason == CompletionTriggerReason.Insertion && !IsWordChar(trigger.Character))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
        }

        public Task<CompletionContext> GetCompletionContextAsync(
            IAsyncCompletionSession session,
            CompletionTrigger trigger,
            SnapshotPoint triggerLocation,
            SnapshotSpan applicableToSpan,
            CancellationToken token)
        {
            Log.Write("q1", "GetCompletionContextAsync: returning static hello items, UI thread=" + ThreadHelper.CheckAccess());
            var items = ImmutableArray.Create(
                new CompletionItem("hello", this),
                new CompletionItem("hello_sql_prompter", this));
            return Task.FromResult(new CompletionContext(items));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            return Task.FromResult<object>(Description);
        }

        internal static SnapshotSpan WordSpan(SnapshotPoint point)
        {
            var start = point;
            while (start.Position > 0 && IsWordChar((start - 1).GetChar()))
            {
                start -= 1;
            }

            return new SnapshotSpan(start, point);
        }

        internal static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';
        }
    }
}
