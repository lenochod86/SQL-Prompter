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
    /// Otázky 1 a 2, starší rozhraní: pokud SQL editor místo IAsyncCompletionSource
    /// používá ICompletionSource, tato sonda vypíše cizí sady návrhů (vestavěné IntelliSense)
    /// a přidá vlastní sadu "SQL Prompter".
    ///
    /// Ve VS existence staršího zdroje pro daný typ obsahu může přepnout editor do staršího
    /// režimu našeptávání. Sonda je proto ve výchozím stavu vypnutá a zapíná ji proměnná
    /// prostředí SQLPROMPTER_LEGACY_PROBE=1. Pouští se jako samostatný běh, viz README.
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

            Log.Write("q1", "ICompletionSourceProvider (starší API) volán pro typObsahu=" + SqlContentTypes.Describe(textBuffer.ContentType));
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
                Log.Write("q2", "Starší relace našeptávání, existujících sad: " + completionSets.Count);
                foreach (var set in completionSets)
                {
                    Log.Write(
                        "q2",
                        string.Format(
                            "  sada moniker=\"{0}\" název=\"{1}\" položek={2} ukázka=[{3}]",
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
