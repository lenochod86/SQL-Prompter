using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Ssms
{
    /// <summary>Typ obsahu SQL editoru v SSMS 22 (ověřeno prototypem).</summary>
    internal static class SqlContentType
    {
        public const string Name = "SQL";
    }

    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SqlPrompter.Completion")]
    [ContentType(SqlContentType.Name)]
    internal sealed class SqlCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        [Import]
        internal MetadataService Metadata { get; set; }

        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionSource(Metadata));
    }

    /// <summary>
    /// Kde se zdroj účastní, nahrazuje vestavěné IntelliSense (ověřeno prototypem).
    /// Proto se účastní jen tam, kde umí nabídnout úplný seznam; jinak vrací
    /// DoesNotParticipateInCompletion a nechá místo vestavěnému IntelliSense.
    /// </summary>
    internal sealed class SqlCompletionSource : IAsyncCompletionSource
    {
        private const string DescriptionKey = "SqlPrompter.Description";

        private static readonly IReadOnlyDictionary<SuggestionKind, ImageElement> Icons = new Dictionary<SuggestionKind, ImageElement>
        {
            [SuggestionKind.Keyword] = Icon(KnownMonikers.IntellisenseKeyword, "Klíčové slovo"),
            [SuggestionKind.Schema] = Icon(KnownMonikers.Schema, "Schéma"),
            [SuggestionKind.Table] = Icon(KnownMonikers.Table, "Tabulka"),
            [SuggestionKind.View] = Icon(KnownMonikers.View, "Pohled"),
            [SuggestionKind.TableFunction] = Icon(KnownMonikers.TableFunction, "Tabulková funkce"),
            [SuggestionKind.Column] = Icon(KnownMonikers.Column, "Sloupec"),
            [SuggestionKind.Alias] = Icon(KnownMonikers.LocalVariable, "Alias"),
            [SuggestionKind.Function] = Icon(KnownMonikers.ScalarFunction, "Funkce"),
        };

        private readonly MetadataService service;
        private Prepared pending;

        public SqlCompletionSource(MetadataService service)
        {
            this.service = service;
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            try
            {
                // Editor volá InitializeCompletion na vlákně UI; ScriptFactory to vyžaduje.
                ThreadHelper.ThrowIfNotOnUIThread();
                return Initialize(trigger, triggerLocation);
            }
            catch (Exception ex)
            {
                Log.Error("InitializeCompletion selhalo", ex);
                return CompletionStartData.DoesNotParticipateInCompletion;
            }
        }

        public Task<CompletionContext> GetCompletionContextAsync(
            IAsyncCompletionSession session,
            CompletionTrigger trigger,
            SnapshotPoint triggerLocation,
            SnapshotSpan applicableToSpan,
            CancellationToken token)
        {
            var prepared = pending;
            if (prepared == null || prepared.Snapshot != triggerLocation.Snapshot)
            {
                return Task.FromResult(CompletionContext.Empty);
            }

            try
            {
                var items = SuggestionBuilder.Build(prepared.Context, prepared.Metadata).Select(ToItem).ToImmutableArray();
                var hint = prepared.Context.SoftSelection ? InitialSelectionHint.SoftSelection : InitialSelectionHint.RegularSelection;
                return Task.FromResult(new CompletionContext(items, null, hint));
            }
            catch (Exception ex)
            {
                Log.Error("Sestavení návrhů selhalo", ex);
                return Task.FromResult(CompletionContext.Empty);
            }
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            return Task.FromResult<object>(item.Properties.TryGetProperty(DescriptionKey, out string description) ? description : null);
        }

        private CompletionStartData Initialize(CompletionTrigger trigger, SnapshotPoint triggerLocation)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!IsTrigger(trigger))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var snapshot = triggerLocation.Snapshot;
            if (!SqlBatch.TryGet(snapshot, triggerLocation.Position, out var batchStart, out var batch))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var context = ContextAnalyzer.Analyze(batch, triggerLocation.Position - batchStart);
            if (context.Kind == ContextKind.None ||
                (trigger.Character == ' ' && trigger.Reason == CompletionTriggerReason.Insertion && context.Kind != ContextKind.ObjectName) ||
                (trigger.Character == '.' && trigger.Reason == CompletionTriggerReason.Insertion && context.Qualifier.Count == 0))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            IDatabaseMetadata metadata = null;
            if (context.Kind != ContextKind.Keyword)
            {
                metadata = PrepareMetadata(context, out var ready);
                if (!ready)
                {
                    return CompletionStartData.DoesNotParticipateInCompletion;
                }
            }

            pending = new Prepared(snapshot, context, metadata);
            return new CompletionStartData(
                CompletionParticipation.ProvidesItems,
                new SnapshotSpan(snapshot, batchStart + context.ReplaceStart, context.ReplaceLength));
        }

        /// <summary>
        /// Vrátí metadata z cache a případně spustí jejich načtení na pozadí. Na databázi se nečeká:
        /// co ještě není načtené, nabídne vestavěné IntelliSense a naše návrhy přijdou při dalším našeptání.
        /// </summary>
        private IDatabaseMetadata PrepareMetadata(CaretContext context, out bool ready)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = SsmsConnection.GetActive();
            IDatabaseMetadata metadata = null;
            if (settings != null && settings.CanConnect)
            {
                var cache = service.Caches.Get(settings);
                var loader = service.CreateLoader(settings);
                metadata = cache.Current;
                if (metadata == null)
                {
                    _ = cache.EnsureLoaded(loader);
                }
                else
                {
                    _ = cache.CheckForChanges(loader);
                }

                var readiness = Readiness.Evaluate(context, metadata);
                if (readiness.State == ReadinessState.NeedsColumns)
                {
                    _ = cache.RequestColumns(readiness.MissingColumns, loader);
                }

                ready = readiness.State == ReadinessState.Ready;
                return metadata;
            }

            ready = Readiness.Evaluate(context, null).State == ReadinessState.Ready;
            return null;
        }

        private static bool IsTrigger(CompletionTrigger trigger)
        {
            switch (trigger.Reason)
            {
                case CompletionTriggerReason.Invoke:
                case CompletionTriggerReason.InvokeAndCommitIfUnique:
                    return true;
                case CompletionTriggerReason.Insertion:
                    var c = trigger.Character;
                    return char.IsLetter(c) || c == '_' || c == '.' || c == '[' || c == ' ';
                default:
                    return false;
            }
        }

        private CompletionItem ToItem(Suggestion suggestion)
        {
            var item = new CompletionItem(
                suggestion.DisplayText,
                this,
                Icons[suggestion.Kind],
                ImmutableArray<CompletionFilter>.Empty,
                suggestion.Suffix ?? string.Empty,
                suggestion.InsertText,
                suggestion.SortText,
                suggestion.FilterText,
                ImmutableArray<ImageElement>.Empty);
            if (suggestion.Description != null)
            {
                item.Properties.AddProperty(DescriptionKey, suggestion.Description);
            }

            return item;
        }

        private static ImageElement Icon(ImageMoniker moniker, string name) => new ImageElement(new ImageId(moniker.Guid, moniker.Id), name);

        private sealed class Prepared
        {
            public Prepared(ITextSnapshot snapshot, CaretContext context, IDatabaseMetadata metadata)
            {
                Snapshot = snapshot;
                Context = context;
                Metadata = metadata;
            }

            public ITextSnapshot Snapshot { get; }

            public CaretContext Context { get; }

            public IDatabaseMetadata Metadata { get; }
        }
    }

    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("SqlPrompter.Commit")]
    [ContentType(SqlContentType.Name)]
    internal sealed class SqlCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCommitManager());
    }

    /// <summary>
    /// Návrh se vkládá jen klávesou Tab nebo Enter. Mezera, čárka ani tečka nic nevkládají,
    /// aby psaní aliasů a vlastních názvů nepřepsal první návrh.
    /// </summary>
    internal sealed class SqlCommitManager : IAsyncCompletionCommitManager
    {
        public IEnumerable<char> PotentialCommitCharacters => Array.Empty<char>();

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) => false;

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token) =>
            CommitResult.Unhandled;
    }
}
