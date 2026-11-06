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

        /// <summary>The provider the editor created, so that <see cref="InvokedList"/> reaches the source of a view.</summary>
        internal static SqlCompletionSourceProvider Current { get; private set; }

        public SqlCompletionSourceProvider()
        {
            Current = this;
        }

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
        internal const string BlockKey = "SqlPrompter.Block";
        internal const string UsageKeyKey = "SqlPrompter.UsageKey";
        internal const string DefinitionKey = "SqlPrompter.Definition";
        internal const string BeforeLineKey = "SqlPrompter.BeforeLine";
        internal const string DetailKey = "SqlPrompter.Detail";
        internal const string KeyKey = "SqlPrompter.Key";
        /// <summary>Session property: true when Enter may insert the selected suggestion (not a place where a name of the user's own is typed).</summary>
        /// <summary>Item property: the item is a heading of the column picker (the value is the text of the heading); it cannot be chosen.</summary>
        internal const string PickerHeaderKey = "SqlPrompter.PickerHeader";
        /// <summary>Session property: true when the column picker is available at the caret.</summary>
        internal const string PickerAvailableKey = "SqlPrompter.PickerAvailable";
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
            [SuggestionKind.Alias] = Icon(KnownMonikers.Rename, "Alias"),
            [SuggestionKind.Function] = Icon(KnownMonikers.ScalarFunction, "Function"),
            [SuggestionKind.ScalarFunction] = Icon(KnownMonikers.ScalarFunction, "Scalar function"),
            [SuggestionKind.DataType] = Icon(KnownMonikers.Type, "Data type"),
            [SuggestionKind.Database] = Icon(KnownMonikers.Database, "Database"),
            [SuggestionKind.LinkedServer] = Icon(KnownMonikers.LinkedServer, "Linked server"),
            [SuggestionKind.JoinCondition] = Icon(KnownMonikers.ForeignKeyRelationship, "Join condition"),
            [SuggestionKind.Snippet] = Icon(KnownMonikers.KeywordSnippet, "Snippet"),
            [SuggestionKind.ScriptTable] = Icon(KnownMonikers.TableScript, "Table defined in the script"),
            [SuggestionKind.Variable] = Icon(KnownMonikers.LocalVariable, "Variable"),
            [SuggestionKind.Procedure] = Icon(KnownMonikers.StoredProcedure, "Stored procedure"),
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
                var start = Initialize(trigger, triggerLocation, out var prepared);
                if (prepared != null && trigger.Reason == CompletionTriggerReason.Insertion && SettingsService.Current.Behavior.OwnTypingList)
                {
                    // The list that opens while typing is run by InvokedList; the session takes part (so that the built-in list does not show)
                    // but offers nothing.
                    pending = null;
                    return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(triggerLocation, 0));
                }

                pending = prepared;
                return start;
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
                var items = BuildItems(prepared, out var scriptModel, out var builtOptions);
                session.Properties[HardSelectionKey] = !prepared.Context.SoftSelection;
                // Where columns are picked the list says so in its status bar and Ctrl+Left / the tab open the column picker.
                session.Properties[PickerAvailableKey] = ColumnPicker.TryBuild(prepared.Batch, prepared.CaretInBatch, prepared.Context, prepared.Metadata, scriptModel, builtOptions) != null;
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

        private ImmutableArray<CompletionItem> BuildItems(Prepared prepared) => BuildItems(prepared, out _, out _);

        private ImmutableArray<CompletionItem> BuildItems(Prepared prepared, out ScriptModel script, out SuggestionOptions options)
        {
            script = ScriptModel.Build(prepared.Batch, prepared.CaretInBatch);
            RequestScriptColumns(prepared, script);
            options = SettingsService.Current.ToSuggestionOptions().WithRemoteColumns(prepared.RemoteColumns).WithSnippets(SnippetService.Snippets).WithEditor(prepared.Editor);
            return SuggestionBuilder.Build(prepared.Context, prepared.Metadata, script, options).Select(s => ToItem(s, prepared)).ToImmutableArray();
        }

        /// <summary>
        /// The suggestions for the list that opens while typing (the option <see cref="BehaviorSettings.OwnTypingList"/>): the preparation (UI thread,
        /// the same rules as the editor's session for a typed character) is done here, the items are built off the UI thread. Null when this
        /// place has no suggestions.
        /// </summary>
        internal async Task<InvokedSuggestions> BuildForTypingAsync(SnapshotPoint location, char typed)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Prepared prepared;
            CompletionStartData start;
            try
            {
                start = Initialize(new CompletionTrigger(CompletionTriggerReason.Insertion, location.Snapshot, typed), location, out prepared);
            }
            catch (Exception ex)
            {
                Log.Error("Preparing the suggestions for typing failed", ex);
                return null;
            }

            if (start.ApplicableToSpan.Snapshot == null || prepared == null || prepared.Snapshot != location.Snapshot)
            {
                return null;
            }

            try
            {
                var span = location.Snapshot.CreateTrackingSpan(start.ApplicableToSpan, SpanTrackingMode.EdgeInclusive);
                return await Task.Run(() =>
                {
                    var items = ListOrdering.Sort(BuildItems(prepared, out var script, out var options), i => i.SortText).ToImmutableArray();
                    var picker = ColumnPicker.TryBuild(prepared.Batch, prepared.CaretInBatch, prepared.Context, prepared.Metadata, script, options);
                    return new InvokedSuggestions(items, span, prepared.Context.SoftSelection, picker, options, this);
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error("Building the suggestions for typing failed", ex);
                return null;
            }
        }

        /// <summary>
        /// The suggestions for a list that SQL Prompter runs itself (<see cref="InvokedList"/>): the same preparation and the same items as the editor's
        /// session, built on the UI thread (it takes milliseconds and never waits for the database). Null when this place has no suggestions.
        /// </summary>
        internal InvokedSuggestions BuildForInvoke(SnapshotPoint location)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var start = Initialize(new CompletionTrigger(CompletionTriggerReason.Invoke, location.Snapshot), location, out var prepared);
                if (start.ApplicableToSpan.Snapshot == null || prepared == null || prepared.Snapshot != location.Snapshot)
                {
                    return null;
                }

                // The editor sorts the items of its own sessions by their sort text; the own list has to do it itself.
                var items = ListOrdering.Sort(BuildItems(prepared, out var script, out var options), i => i.SortText).ToImmutableArray();
                var picker = ColumnPicker.TryBuild(prepared.Batch, prepared.CaretInBatch, prepared.Context, prepared.Metadata, script, options);
                return new InvokedSuggestions(items, location.Snapshot.CreateTrackingSpan(start.ApplicableToSpan, SpanTrackingMode.EdgeInclusive), prepared.Context.SoftSelection, picker, options, this);
            }
            catch (Exception ex)
            {
                Log.Error("Building the suggestions failed", ex);
                return null;
            }
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            return Task.FromResult<object>(item.Properties.TryGetProperty(DescriptionKey, out string description) ? description : null);
        }

        /// <summary>Decides whether SQL Prompter takes part at the trigger and prepares what the items are built from (null when there is nothing to build).</summary>
        private CompletionStartData Initialize(CompletionTrigger trigger, SnapshotPoint triggerLocation, out Prepared prepared)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            prepared = null;
            if (!SettingsService.Current.Behavior.SuggestionsEnabled || !IsTrigger(trigger))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            if (SnippetSession.IsActive(textView))
            {
                // The text of a snippet field is being typed: no list, but ours still takes part so that the built-in one does not show.
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
                (insertion && trigger.Character == ' ' && context.Kind != ContextKind.ObjectName && !AfterComma(batch, triggerLocation.Position - batchStart, context)) ||
                (insertion && trigger.Character == ',' && !AfterComma(batch, triggerLocation.Position - batchStart, context)) ||
                (insertion && trigger.Character == '(' && context.Kind != ContextKind.InsertColumns) ||
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

            prepared = new Prepared(snapshot, context, metadata, batch, batchStart, triggerLocation.Position - batchStart, cache, loader, service.LinkedColumns(), BuildEditorLayout(snapshot, batchStart + context.ReplaceStart));
            return new CompletionStartData(
                CompletionParticipation.ProvidesItems,
                new SnapshotSpan(snapshot, batchStart + context.ReplaceStart, context.ReplaceLength));
        }

        /// <summary>The list opens by itself after a comma (or the space after it) where the next item is a name or an expression.</summary>
        private static bool AfterComma(string batch, int caret, CaretContext context) =>
            SettingsService.Current.Behavior.SuggestAfterComma && CommaTrigger.ShouldOpen(batch, caret, context);

        private EditorLayout BuildEditorLayout(ITextSnapshot snapshot, int position) => EditorLayouts.Read(textView, snapshot, position);

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

        /// <summary>A typed character that may start the list (the same characters as in <see cref="IsTrigger"/>).</summary>
        internal static bool StartsList(char c) => char.IsLetter(c) || c == '_' || c == '.' || c == '[' || c == ' ' || c == ',' || c == '@' || c == '#' || c == '(';

        private static bool IsTrigger(CompletionTrigger trigger)
        {
            switch (trigger.Reason)
            {
                case CompletionTriggerReason.Invoke:
                case CompletionTriggerReason.InvokeAndCommitIfUnique:
                    return true;
                case CompletionTriggerReason.Insertion:
                    var c = trigger.Character;
                    return char.IsLetter(c) || c == '_' || c == '.' || c == '[' || c == ' ' || c == ',' || c == '@' || c == '#' || c == '(';
                default:
                    return false;
            }
        }

        private CompletionItem ToItem(Suggestion suggestion, Prepared prepared)
        {
            var rank = SettingsService.Current.Behavior.RankByUsage;
            var usageKey = (prepared.Metadata?.DatabaseName ?? string.Empty) + "|" + suggestion.UsageKey;
            var item = new CompletionItem(
                suggestion.DisplayText,
                this,
                Icons[suggestion.Kind],
                Filters,
                SettingsService.Current.Behavior.MarkItems ? suggestion.MarkedSuffix : suggestion.Suffix ?? string.Empty,
                suggestion.InsertText,
                rank ? suggestion.RankedSortText(UsageService.Statistics.CountOf(usageKey)) : suggestion.SortText,
                suggestion.FilterText,
                ImmutableArray<ImageElement>.Empty);
            item.Properties.AddProperty(UsageKeyKey, usageKey);
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

            if (suggestion.IsBlock)
            {
                item.Properties.AddProperty(BlockKey, true);
            }

            if (suggestion.DefinitionName != null && prepared.Metadata is DatabaseSnapshot)
            {
                item.Properties.AddProperty(DefinitionKey, new DefinitionRef(prepared.Metadata.DatabaseName, suggestion.DefinitionSchema, suggestion.DefinitionName));
            }

            if (suggestion.BeforeLine != null)
            {
                item.Properties.AddProperty(BeforeLineKey, suggestion.BeforeLine);
            }

            if (suggestion.FromClause != null && prepared.Context.FromInsertPosition >= 0)
            {
                var position = prepared.BatchStart + prepared.Context.FromInsertPosition;
                item.Properties.AddProperty(FromEditKey, new FromEdit(prepared.Snapshot.CreateTrackingPoint(position, PointTrackingMode.Positive), " " + suggestion.FromClause));
            }

            return item;
        }

        /// <summary>The item of a column in the column picker.</summary>
        internal CompletionItem PickerItem(PickerColumn column)
        {
            var item = new CompletionItem(column.Name, this, Icon(KnownMonikers.Column, "Column"), Filters, column.Owner, column.InsertText, column.Name, column.Name, ImmutableArray<ImageElement>.Empty);
            item.Properties.AddProperty(DetailKey, column.Column.TypeName + (column.Column.IsNullable ? " null" : " not null"));
            if (column.Key != KeyKind.None)
            {
                item.Properties.AddProperty(KeyKey, column.Key);
            }

            return item;
        }

        /// <summary>The heading of a source in the column picker.</summary>
        internal CompletionItem PickerHeader(string group, string owner)
        {
            var item = new CompletionItem(group, this, Icon(KnownMonikers.Table, "Table"), Filters, owner ?? string.Empty, group, group, group, ImmutableArray<ImageElement>.Empty);
            item.Properties.AddProperty(PickerHeaderKey, group);
            return item;
        }

        /// <summary>The first item of the column picker in GROUP BY: all the non-aggregated items of the select list.</summary>
        internal CompletionItem PickerNonAggregated(string text) =>
            new CompletionItem("All non-aggregated columns", this, Icon(KnownMonikers.Column, "Column"), Filters, "select list", text, " ", "All non-aggregated columns", ImmutableArray<ImageElement>.Empty);

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
                Func<TableSource, IReadOnlyList<DbColumn>> remoteColumns,
                EditorLayout editor)
            {
                Editor = editor;
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

            public EditorLayout Editor { get; }

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

        private static void MoveCaret(ITextView view, ITextBuffer buffer, int position)
        {
            try
            {
                view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, position));
            }
            catch (Exception ex)
            {
                Log.Error("Moving the caret failed", ex);
            }
        }

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Commit(session.TextView, session.ApplicableToSpan, buffer, item, typedChar);
        }

        /// <summary>
        /// The commit of an item in place of the typed text (<paramref name="applicable"/>), shared by the editor's session (above) and the list SQL Prompter
        /// runs itself (<see cref="InvokedList"/>).
        /// </summary>
        internal static CommitResult Commit(ITextView view, ITrackingSpan applicable, ITextBuffer buffer, CompletionItem item, char typedChar)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // A space typed while nothing has been typed yet (the list opened after a comma) only closes the list.
            if (typedChar == ' ' && applicable.GetSpan(buffer.CurrentSnapshot).Length == 0)
            {
                return CloseWithoutInserting;
            }

            // Enter and Tab arrive as '\n' and '\t'; they always insert.
            if (typedChar == '\n' || typedChar == '\r' || typedChar == '\t' || typedChar == '\0' || Keys.Commits(typedChar))
            {
                if (item.Properties.TryGetProperty(SqlCompletionSource.UsageKeyKey, out string chosenKey) && SettingsService.Current.Behavior.RankByUsage)
                {
                    UsageService.Record(chosenKey);
                }

                // A snippet is expanded instead of inserting its prefix; the typed character (Tab, Enter, Space) is swallowed.
                if (item.Properties.TryGetProperty(SqlCompletionSource.SnippetKey, out SqlPrompter.Core.Snippets.Snippet snippetToExpand) &&
                    SnippetInserter.TryInsert(view, applicable, buffer, snippetToExpand))
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
                        var span = applicable.GetSpan(buffer.CurrentSnapshot);
                        buffer.Replace(span, item.InsertText);
                        buffer.Insert(edit.Position.GetPosition(buffer.CurrentSnapshot), edit.Text);
                        MoveCaret(view, buffer, span.Start.Position + item.InsertText.Length);
                        return new CommitResult(isHandled: true, CommitBehavior.None);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Adding the FROM clause failed", ex);
                    }
                }

                // INSERT with its column list, EXEC with its parameters: several lines, optionally with lines above (DECLARE for OUTPUT
                // parameters). The typed character (Space, Enter, Tab) is swallowed so that it does not land after the text.
                if (item.Properties.TryGetProperty(SqlCompletionSource.BlockKey, out bool block) && block)
                {
                    try
                    {
                        var span = applicable.GetSpan(buffer.CurrentSnapshot);
                        var before = item.Properties.TryGetProperty(SqlCompletionSource.BeforeLineKey, out string lines) ? lines : string.Empty;
                        using (var blockEdit = buffer.CreateEdit())
                        {
                            blockEdit.Replace(span, item.InsertText);
                            if (before.Length > 0)
                            {
                                blockEdit.Insert(span.Snapshot.GetLineFromPosition(span.Start.Position).Start.Position, before);
                            }

                            blockEdit.Apply();
                        }

                        MoveCaret(view, buffer, span.Start.Position + before.Length + item.InsertText.Length);
                        return new CommitResult(isHandled: true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Inserting the statement text failed", ex);
                    }
                }

                // A function: its parentheses come with it and the caret goes between them when it takes arguments. The typed character
                // (Space, Enter, Tab) is swallowed so that it does not land inside the parentheses; when the name is already followed by
                // "(" only the name is inserted.
                if (item.Properties.TryGetProperty(SqlCompletionSource.CaretBackKey, out int caretBack))
                {
                    try
                    {
                        var span = applicable.GetSpan(buffer.CurrentSnapshot);
                        var text = item.InsertText;
                        var followed = span.End.Position < buffer.CurrentSnapshot.Length && buffer.CurrentSnapshot[span.End.Position] == '(';
                        if (followed && text.EndsWith("()", StringComparison.Ordinal))
                        {
                            text = text.Substring(0, text.Length - 2);
                            caretBack = 0;
                        }

                        buffer.Replace(span, text);
                        MoveCaret(view, buffer, span.Start.Position + text.Length - caretBack);
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
