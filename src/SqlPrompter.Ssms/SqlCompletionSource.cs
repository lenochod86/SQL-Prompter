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
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>Content type of the SQL editor in SSMS 22 (verified by the prototype).</summary>
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
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionSource(Metadata, textView));
    }

    /// <summary>
    /// Where this source participates, it suppresses the built-in IntelliSense list (verified by the prototype).
    /// It therefore participates everywhere except in strings and comments so that only its suggestions are shown;
    /// the built-in IntelliSense still underlines errors and shows quick info.
    /// </summary>
    internal sealed class SqlCompletionSource : IAsyncCompletionSource
    {
        private const string DescriptionKey = "SqlPrompter.Description";
        internal const string FromEditKey = "SqlPrompter.FromEdit";
        internal const string CaretBackKey = "SqlPrompter.CaretBack";
        internal const string DetailKey = "SqlPrompter.Detail";
        internal const string KeyKey = "SqlPrompter.Key";
        /// <summary>Session property: true when Enter may insert the selected suggestion (not a place where a name of the user's own is typed).</summary>
        internal const string HardSelectionKey = "SqlPrompter.HardSelection";
        internal const string SnippetKey = "SqlPrompter.Snippet";
        internal const string DescriptionPropertyKey = DescriptionKey;

        /// <summary>Button at the bottom of the list that shows the list comes from SQL Prompter.</summary>
        private static readonly ImmutableArray<CompletionFilter> Filters = ImmutableArray.Create(
            new CompletionFilter("SQL Prompter", "Q", Icon(KnownMonikers.Database, "SQL Prompter")));

        private static readonly IReadOnlyDictionary<SuggestionKind, ImageElement> Icons = new Dictionary<SuggestionKind, ImageElement>
        {
            [SuggestionKind.Keyword] = Icon(KnownMonikers.IntellisenseKeyword, "Keyword"),
            [SuggestionKind.Schema] = Icon(KnownMonikers.Schema, "Schema"),
            [SuggestionKind.Table] = Icon(KnownMonikers.Table, "Table"),
            [SuggestionKind.View] = Icon(KnownMonikers.View, "View"),
            [SuggestionKind.TableFunction] = Icon(KnownMonikers.TableFunction, "Table-valued function"),
            [SuggestionKind.Column] = Icon(KnownMonikers.Column, "Column"),
            [SuggestionKind.Alias] = Icon(KnownMonikers.LocalVariable, "Alias"),
            [SuggestionKind.Function] = Icon(KnownMonikers.ScalarFunction, "Function"),
            [SuggestionKind.ScalarFunction] = Icon(KnownMonikers.ScalarFunction, "Scalar function"),
            [SuggestionKind.DataType] = Icon(KnownMonikers.Type, "Data type"),
            [SuggestionKind.Database] = Icon(KnownMonikers.Database, "Database"),
            [SuggestionKind.LinkedServer] = Icon(KnownMonikers.LinkedServer, "Linked server"),
            [SuggestionKind.JoinCondition] = Icon(KnownMonikers.ForeignKeyRelationship, "Join condition"),
            [SuggestionKind.Snippet] = Icon(KnownMonikers.Snippet, "Snippet"),
            [SuggestionKind.ScriptTable] = Icon(KnownMonikers.Table, "Table defined in the script"),
            [SuggestionKind.Variable] = Icon(KnownMonikers.LocalVariable, "Variable"),
        };

        private readonly MetadataService service;
        private readonly ITextView textView;
        private Prepared pending;

        public SqlCompletionSource(MetadataService service, ITextView textView)
        {
            this.service = service;
            this.textView = textView;
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            try
            {
                // The editor calls InitializeCompletion on the UI thread; ScriptFactory requires it.
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
                // The script model (CTEs, temporary tables, variables) is built here, off the UI thread.
                var script = ScriptModel.Build(prepared.Batch, prepared.CaretInBatch);
                RequestScriptColumns(prepared, script);
                var items = SuggestionBuilder.Build(prepared.Context, prepared.Metadata, script, SettingsService.Current.ToSuggestionOptions().WithRemoteColumns(prepared.RemoteColumns).WithSnippets(SnippetService.Snippets)).Select(s => ToItem(s, prepared)).ToImmutableArray();
                session.Properties[HardSelectionKey] = !prepared.Context.SoftSelection;
                var hint = prepared.Context.SoftSelection ? InitialSelectionHint.SoftSelection : InitialSelectionHint.RegularSelection;
                // Where the user probably types a name of their own (an alias), Enter must keep what was typed instead of inserting
                // a suggestion: the editor then offers the typed text as a "suggestion item" and only Tab inserts a real suggestion.
                var suggestion = prepared.Context.SoftSelection ? new SuggestionItemOptions("name or alias", "Enter keeps what you typed; Tab inserts the selected suggestion") : null;
                return Task.FromResult(new CompletionContext(items, suggestion, hint));
            }
            catch (Exception ex)
            {
                Log.Error("Building suggestions failed", ex);
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
            if (!SettingsService.Current.Behavior.SuggestionsEnabled || !IsTrigger(trigger))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            if (SnippetSession.IsActive(textView))
            {
                // The text of a snippet field is being typed: no list, but ours still takes part so that the built-in one does not show.
                pending = null;
                return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(triggerLocation, 0));
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

            var usedDatabase = SqlBatch.FindUsedDatabase(snapshot, triggerLocation.Position);
            var metadata = PrepareMetadata(context, usedDatabase, out var cache, out var loader);
            if (ForeignDatabase.TryResolve(context, metadata, out var otherDatabase, out var insideDatabase))
            {
                // OtherDb.| : continue with the objects of that database.
                context = insideDatabase;
                if (!string.Equals(otherDatabase, metadata.DatabaseName, StringComparison.OrdinalIgnoreCase))
                {
                    metadata = PrepareMetadata(context, otherDatabase, out cache, out loader);
                }
            }
            else if (LinkedServerName.TryResolve(context, metadata, out var linkedServer, out var linkedDatabase, out var insideLinked))
            {
                // Srv.|, Srv.Db.| : names of a linked server, loaded lazily in the background (nothing until then).
                context = insideLinked;
                metadata = service.LinkedMetadata(linkedServer, linkedDatabase);
                cache = null;
                loader = null;
            }

            pending = new Prepared(snapshot, context, metadata, batch, batchStart, triggerLocation.Position - batchStart, cache, loader, service.LinkedColumns());
            return new CompletionStartData(
                CompletionParticipation.ProvidesItems,
                new SnapshotSpan(snapshot, batchStart + context.ReplaceStart, context.ReplaceLength));
        }

        /// <summary>
        /// Returns metadata from the cache and starts loading missing parts in the background. It never waits for the database:
        /// the list is built from what is available and completed on the next completion.
        /// </summary>
        private IDatabaseMetadata PrepareMetadata(CaretContext context, string database, out DatabaseMetadataCache cache, out IMetadataLoader loader)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            cache = service.CacheForActiveWindow(out loader, database);
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

        /// <summary>
        /// A CTE, derived table or temporary table with SELECT * needs the columns of the tables behind it.
        /// Loading starts in the background and the list is completed on the next completion.
        /// </summary>
        private static void RequestScriptColumns(Prepared prepared, ScriptModel script)
        {
            if (prepared.Cache == null || prepared.Metadata == null || prepared.Loader == null)
            {
                return;
            }

            var readiness = Readiness.Evaluate(prepared.Context, prepared.Metadata, script);
            if (readiness.State == ReadinessState.NeedsColumns)
            {
                _ = prepared.Cache.RequestColumns(readiness.MissingColumns, prepared.Loader);
            }
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

        private CompletionItem ToItem(Suggestion suggestion, Prepared prepared)
        {
            var item = new CompletionItem(
                suggestion.DisplayText,
                this,
                Icons[suggestion.Kind],
                Filters,
                SettingsService.Current.Behavior.MarkItems ? suggestion.MarkedSuffix : suggestion.Suffix ?? string.Empty,
                suggestion.InsertText,
                suggestion.SortText,
                suggestion.FilterText,
                ImmutableArray<ImageElement>.Empty);
            if (suggestion.Tag is SqlPrompter.Core.Snippets.Snippet snippet)
            {
                item.Properties.AddProperty(SnippetKey, snippet);
            }

            if (suggestion.Key != KeyKind.None)
            {
                item.Properties.AddProperty(KeyKey, suggestion.Key);
            }

            if (!string.IsNullOrEmpty(suggestion.Detail))
            {
                item.Properties.AddProperty(DetailKey, suggestion.Detail);
            }

            if (suggestion.Description != null)
            {
                item.Properties.AddProperty(DescriptionKey, suggestion.Description);
            }

            if (suggestion.CaretBack > 0 || suggestion.InsertText.EndsWith("()", StringComparison.Ordinal))
            {
                item.Properties.AddProperty(CaretBackKey, suggestion.CaretBack);
            }

            if (suggestion.FromClause != null && prepared.Context.FromInsertPosition >= 0)
            {
                var position = prepared.BatchStart + prepared.Context.FromInsertPosition;
                item.Properties.AddProperty(FromEditKey, new FromEdit(prepared.Snapshot.CreateTrackingPoint(position, PointTrackingMode.Positive), " " + suggestion.FromClause));
            }

            return item;
        }

        private static ImageElement Icon(ImageMoniker moniker, string name) => new ImageElement(new ImageId(moniker.Guid, moniker.Id), name);

        private sealed class Prepared
        {
            public Prepared(
                ITextSnapshot snapshot,
                CaretContext context,
                IDatabaseMetadata metadata,
                string batch,
                int batchStart,
                int caretInBatch,
                DatabaseMetadataCache cache,
                IMetadataLoader loader,
                Func<TableSource, IReadOnlyList<DbColumn>> remoteColumns)
            {
                Snapshot = snapshot;
                Context = context;
                Metadata = metadata;
                Batch = batch;
                BatchStart = batchStart;
                CaretInBatch = caretInBatch;
                Cache = cache;
                Loader = loader;
                RemoteColumns = remoteColumns;
            }

            public string Batch { get; }

            public Func<TableSource, IReadOnlyList<DbColumn>> RemoteColumns { get; }

            public int BatchStart { get; }

            public int CaretInBatch { get; }

            public DatabaseMetadataCache Cache { get; }

            public IMetadataLoader Loader { get; }

            public ITextSnapshot Snapshot { get; }

            public CaretContext Context { get; }

            public IDatabaseMetadata Metadata { get; }
        }
    }

    /// <summary>Text to insert at the end of the select list when the item is committed.</summary>
    internal sealed class FromEdit
    {
        public FromEdit(ITrackingPoint position, string text)
        {
            Position = position;
            Text = text;
        }

        public ITrackingPoint Position { get; }

        public string Text { get; }
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
    /// Every character that ends a word ends the completion session, so the list never stays open waiting
    /// for a choice. Characters enabled in <see cref="CommitKeys"/> insert the selected suggestion first;
    /// the others are reported as a handled commit without inserting anything, which closes the list and
    /// lets the character be typed (a dot then opens the next list, e.g. alias columns after "e.").
    /// With soft selection (e.g. while typing an alias) the editor does not commit on typed characters.
    /// </summary>
    internal sealed class SqlCommitManager : IAsyncCompletionCommitManager
    {
        private static readonly CommitResult CloseWithoutInserting = new CommitResult(isHandled: true, CommitBehavior.None);

        // Read on every use so that changed settings apply without restarting SSMS.
        private static CommitKeys Keys => SettingsService.Current.Behavior.ToCommitKeys();

        public IEnumerable<char> PotentialCommitCharacters => Keys.PotentialCharacters;

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) =>
            Keys.PotentialCharacters.Contains(typedChar);

        private static void MoveCaret(IAsyncCompletionSession session, ITextBuffer buffer, int position)
        {
            try
            {
                session.TextView.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, position));
            }
            catch (Exception ex)
            {
                Log.Error("Moving the caret failed", ex);
            }
        }

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Enter and Tab arrive as '\n' and '\t'; they always insert.
            if (typedChar == '\n' || typedChar == '\r' || typedChar == '\t' || typedChar == '\0' || Keys.Commits(typedChar))
            {
                // A snippet is expanded instead of inserting its prefix; the typed character (Tab, Enter, Space) is swallowed.
                if (item.Properties.TryGetProperty(SqlCompletionSource.SnippetKey, out SqlPrompter.Core.Snippets.Snippet snippetToExpand) &&
                    SnippetInserter.TryInsert(session, buffer, snippetToExpand))
                {
                    return new CommitResult(isHandled: true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
                }

                // SELECT without FROM: the column is inserted and the missing FROM goes to the end of the select list.
                // Done here in two edits (not by the default commit) because an insertion at the end of the replaced span
                // could otherwise be swallowed by it.
                if (item.Properties.TryGetProperty(SqlCompletionSource.FromEditKey, out FromEdit edit))
                {
                    try
                    {
                        var span = session.ApplicableToSpan.GetSpan(buffer.CurrentSnapshot);
                        buffer.Replace(span, item.InsertText);
                        buffer.Insert(edit.Position.GetPosition(buffer.CurrentSnapshot), edit.Text);
                        MoveCaret(session, buffer, span.Start.Position + item.InsertText.Length);
                        return new CommitResult(isHandled: true, CommitBehavior.None);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Adding the FROM clause failed", ex);
                    }
                }

                // A function: its parentheses come with it and the caret goes between them when it takes arguments. The typed character
                // (Space, Enter, Tab) is swallowed so that it does not land inside the parentheses; when the name is already followed by
                // "(" only the name is inserted.
                if (item.Properties.TryGetProperty(SqlCompletionSource.CaretBackKey, out int caretBack))
                {
                    try
                    {
                        var span = session.ApplicableToSpan.GetSpan(buffer.CurrentSnapshot);
                        var text = item.InsertText;
                        var followed = span.End.Position < buffer.CurrentSnapshot.Length && buffer.CurrentSnapshot[span.End.Position] == '(';
                        if (followed && text.EndsWith("()", StringComparison.Ordinal))
                        {
                            text = text.Substring(0, text.Length - 2);
                            caretBack = 0;
                        }

                        buffer.Replace(span, text);
                        MoveCaret(session, buffer, span.Start.Position + text.Length - caretBack);
                        return new CommitResult(isHandled: true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Placing the caret in the parentheses failed", ex);
                    }
                }

                return CommitResult.Unhandled;
            }

            return CloseWithoutInserting;
        }
    }
}
