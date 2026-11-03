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
    /// Kde se zdroj účastní, potlačí vestavěný seznam IntelliSense (ověřeno prototypem).
    /// Účastní se proto všude mimo řetězce a komentáře, aby se ukazovaly jen jeho návrhy;
    /// vestavěné IntelliSense dál podtrhává chyby a zobrazuje popisky.
    /// </summary>
    internal sealed class SqlCompletionSource : IAsyncCompletionSource
    {
        private const string DescriptionKey = "SqlPrompter.Description";

        /// <summary>Tlačítko dole v seznamu, podle kterého je vidět, že seznam je od SQL Prompteru.</summary>
        private static readonly ImmutableArray<CompletionFilter> Filters = ImmutableArray.Create(
            new CompletionFilter("SQL Prompter", "Q", Icon(KnownMonikers.Database, "SQL Prompter")));

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
            [SuggestionKind.DataType] = Icon(KnownMonikers.Type, "Datový typ"),
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
            var insertion = trigger.Reason == CompletionTriggerReason.Insertion;
            if (context.Kind == ContextKind.None ||
                (insertion && trigger.Character == ' ' && context.Kind != ContextKind.ObjectName) ||
                (insertion && trigger.Character == '.' && context.Qualifier.Count == 0 && context.Kind != ContextKind.Name))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var metadata = PrepareMetadata(context);
            pending = new Prepared(snapshot, context, metadata);
            return new CompletionStartData(
                CompletionParticipation.ProvidesItems,
                new SnapshotSpan(snapshot, batchStart + context.ReplaceStart, context.ReplaceLength));
        }

        /// <summary>
        /// Vrátí metadata z cache a případně spustí načtení chybějících částí na pozadí. Na databázi se nečeká:
        /// seznam se sestaví z toho, co je k dispozici, a doplní se při dalším našeptání.
        /// </summary>
        private IDatabaseMetadata PrepareMetadata(CaretContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var cache = service.CacheForActiveWindow(out var loader);
            var metadata = cache?.Current;
            if (cache == null || metadata == null)
            {
                return metadata;
            }

            _ = cache.CheckForChanges(loader);
            var readiness = Readiness.Evaluate(context, metadata);
            if (readiness.State == ReadinessState.NeedsColumns)
            {
                _ = cache.RequestColumns(readiness.MissingColumns, loader);
            }

            return metadata;
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
                    return char.IsLetter(c) || c == '_' || c == '.' || c == '[' || c == ' ' || c == '@' || c == '#';
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
                Filters,
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
    /// Mezera, tečka, čárka, závorky a operátory vloží vybraný návrh a pak se zapíšou samy,
    /// takže „e.“ vloží alias a otevře jeho sloupce. Bez nich by seznam zůstal otevřený a čekal na volbu.
    /// Při naznačeném výběru (SoftSelection, např. při psaní aliasu) editor tyto znaky nevkládá jako návrh.
    /// </summary>
    internal sealed class SqlCommitManager : IAsyncCompletionCommitManager
    {
        private static readonly char[] CommitCharacters = { ' ', '.', ',', '(', ')', ';', '=', '<', '>', '+', '-', '*', '/', '%' };

        public IEnumerable<char> PotentialCommitCharacters => CommitCharacters;

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) =>
            Array.IndexOf(CommitCharacters, typedChar) >= 0;

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token) =>
            CommitResult.Unhandled;
    }
}
