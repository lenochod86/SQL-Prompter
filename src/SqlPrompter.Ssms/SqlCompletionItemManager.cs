using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Completion;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The order and the filter of the editor's own list: the rules of <see cref="ListOrdering"/>, the same as in the list that SQL Prompter runs itself
    /// (#521). The built-in manager showed the unfiltered list after the first typed character and did not select the best match.
    /// </summary>
    [Export(typeof(IAsyncCompletionItemManagerProvider))]
    [Name("SqlPrompter.ItemManager")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = "default")]
    internal sealed class SqlCompletionItemManagerProvider : IAsyncCompletionItemManagerProvider
    {
        public IAsyncCompletionItemManager GetOrCreate(ITextView textView) => new SqlCompletionItemManager();
    }

    internal sealed class SqlCompletionItemManager : IAsyncCompletionItemManager
    {
        public Task<ImmutableArray<CompletionItem>> SortCompletionListAsync(IAsyncCompletionSession session, AsyncCompletionSessionInitialDataSnapshot data, CancellationToken token) =>
            Task.FromResult(ListOrdering.Sort(data.InitialItemList, i => i.SortText).ToImmutableArray());

        public Task<FilteredCompletionModel> UpdateCompletionListAsync(IAsyncCompletionSession session, AsyncCompletionSessionDataSnapshot data, CancellationToken token)
        {
            var typed = session.ApplicableToSpan.GetText(data.Snapshot);
            var loose = SettingsService.Current.Behavior.LooseMatching;
            var matches = ListOrdering.Filter(data.InitialSortedItemList, typed, i => i.FilterText, loose);
            var items = matches
                .Select(i => new CompletionItemWithHighlight(i, Highlight(i, typed, loose)))
                .ToImmutableArray();
            var hard = session.Properties.TryGetProperty(SqlCompletionSource.HardSelectionKey, out bool hardValue) && hardValue;
            var hint = hard || typed.Length > 0 ? UpdateSelectionHint.Selected : UpdateSelectionHint.SoftSelected;
            var model = new FilteredCompletionModel(items, items.Length == 0 ? -1 : 0, data.SelectedFilters, hint, centerSelection: false, uniqueItem: null);
            return Task.FromResult(model);
        }

        private static ImmutableArray<Span> Highlight(CompletionItem item, string typed, bool loose)
        {
            if (typed.Length == 0)
            {
                return ImmutableArray<Span>.Empty;
            }

            var match = NameMatcher.Match(item.DisplayText, typed, loose);
            return match == null ? ImmutableArray<Span>.Empty : match.Runs().Select(run => new Span(run.Key, run.Value)).ToImmutableArray();
        }
    }
}
