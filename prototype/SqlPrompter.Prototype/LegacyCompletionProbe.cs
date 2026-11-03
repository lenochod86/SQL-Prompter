using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Prototype
{
    /// <summary>
    /// Questions 1 and 2, legacy API: if the SQL editor uses ICompletionSource instead of
    /// IAsyncCompletionSource, this probe logs the other completion sets (built-in IntelliSense)
    /// and adds its own "SQL Prompter" set.
    ///
    /// In VS a legacy source for a content type may switch the editor to legacy
    /// completion. The probe is therefore off by default and is turned on by the environment
    /// variable SQLPROMPTER_LEGACY_PROBE=1. It is run separately, see the README.
    /// </summary>
    [Export(typeof(ICompletionSourceProvider))]
    [Name("SqlPrompter.Prototype.LegacyProbe")]
    [ContentType("any")]
    internal sealed class LegacyCompletionProbeProvider : ICompletionSourceProvider
    {
        public const string EnableVariable = "SQLPROMPTER_LEGACY_PROBE";

        public static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer)
        {
            if (!Enabled || !SqlContentTypes.IsSql(textBuffer.ContentType))
            {
                return null;
            }

            Log.Write("q1", "ICompletionSourceProvider (legacy API) called for contentType=" + SqlContentTypes.Describe(textBuffer.ContentType));
            return textBuffer.Properties.GetOrCreateSingletonProperty(() => new LegacyCompletionProbe(textBuffer));
        }
    }

    internal sealed class LegacyCompletionProbe : ICompletionSource
    {
        private readonly ITextBuffer buffer;

        public LegacyCompletionProbe(ITextBuffer buffer)
        {
            this.buffer = buffer;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
        {
            try
            {
                Log.Write("q2", "Legacy completion session, existing sets: " + completionSets.Count);
                foreach (var set in completionSets)
                {
                    Log.Write(
                        "q2",
                        string.Format(
                            "  set moniker=\"{0}\" name=\"{1}\" items={2} sample=[{3}]",
                            set.Moniker,
                            set.DisplayName,
                            set.Completions.Count,
                            string.Join(", ", set.Completions.Take(5).Select(c => c.DisplayText))));
                }

                var trigger = session.GetTriggerPoint(buffer.CurrentSnapshot);
                if (!trigger.HasValue)
                {
                    return;
                }

                var span = HelloCompletionSource.WordSpan(trigger.Value);
                var tracking = buffer.CurrentSnapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive);
                completionSets.Add(new CompletionSet(
                    "SqlPrompter",
                    "SQL Prompter",
                    tracking,
                    new[]
                    {
                        new Completion("hello", "hello", HelloCompletionSource.Description, null, null),
                        new Completion("hello_sql_prompter", "hello_sql_prompter", HelloCompletionSource.Description, null, null),
                    },
                    null));
            }
            catch (Exception ex)
            {
                Log.Error("q2", "AugmentCompletionSession selhalo", ex);
            }
        }

        public void Dispose()
        {
        }
    }
}
