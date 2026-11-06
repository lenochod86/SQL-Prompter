using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows.Input;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Completion;

namespace SqlPrompter.Ssms
{
    /// <summary>The suggestions for the place of the caret, built by <see cref="SqlCompletionSource.BuildForInvoke"/>.</summary>
    internal sealed class InvokedSuggestions
    {
        public InvokedSuggestions(ImmutableArray<CompletionItem> items, ITrackingSpan span, bool softSelection, ColumnPicker picker, SuggestionOptions options, SqlCompletionSource source)
        {
            Items = items;
            Span = span;
            SoftSelection = softSelection;
            Picker = picker;
            Options = options;
            Source = source;
        }

        public ImmutableArray<CompletionItem> Items { get; }

        /// <summary>The text typed so far that the chosen item replaces.</summary>
        public ITrackingSpan Span { get; }

        /// <summary>Where the user probably types a name of their own: Enter keeps what was typed, only Tab inserts the suggestion.</summary>
        public bool SoftSelection { get; }

        /// <summary>The columns of the column picker, or null where no columns are picked.</summary>
        public ColumnPicker Picker { get; }

        public SuggestionOptions Options { get; }

        public SqlCompletionSource Source { get; }
    }

    /// <summary>
    /// The suggestion list that SQL Prompter runs itself, opened by Ctrl+Space (or the menu command Show Suggestions) and by Ctrl+Left in the editor's own
    /// list where columns are picked. The editor's completion session does not work for a list that is started from outside the typing path (its source is
    /// never asked for items, #144), so this controller does what the session would: it asks our source for the items, filters them, shows them in the same
    /// popup (<see cref="SqlCompletionPresenter"/>), moves the selection with the keys and inserts the chosen item with the same commit code
    /// (<see cref="SqlCommitManager.Commit"/>). It has two modes: the normal list, and the column picker (check boxes, Space checks a column, Tab inserts
    /// the checked ones, #176). In the normal list a typed letter hands over to the editor's own session (it starts by itself on typing), so the
    /// normal list only covers the moment between the key and the first typed character; in the picker typing filters the columns.
    /// </summary>
    internal sealed class InvokedList
    {
        private readonly IWpfTextView view;
        private readonly SqlCompletionPresenter presenter;
        private readonly SqlCompletionSource source;
        private InvokedSuggestions suggestions;
        private List<CompletionItemWithHighlight> shown = new List<CompletionItemWithHighlight>();
        private int selected;
        private bool refreshing;

        // The column picker.
        private bool pickerMode;
        private readonly HashSet<CompletionItem> checkedItems = new HashSet<CompletionItem>();
        private readonly Dictionary<CompletionItem, PickerColumn> columnOf = new Dictionary<CompletionItem, PickerColumn>();
        private List<CompletionItem> pickerItems = new List<CompletionItem>();
        private CompletionItem nonAggregatedItem;

        private InvokedList(IWpfTextView view, SqlCompletionPresenter presenter, SqlCompletionSource source)
        {
            this.view = view;
            this.presenter = presenter;
            this.source = source;
            // The keys come from the command handlers (InvokedListCommands: SSMS does not give the editor's WPF key events to us); the text and the
            // caret are watched here to filter again after Backspace, Delete or a caret move.
            view.TextBuffer.Changed += (s, e) => LookAgain();
            view.Caret.PositionChanged += (s, e) => LookAgain();
            view.LostAggregateFocus += (s, e) => Close();
            view.Closed += (s, e) => Close();
            presenter.CommitRequested += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (IsOpen)
                {
                    if (pickerMode)
                    {
                        Toggle(e.Item);
                    }
                    else
                    {
                        Commit(e.Item, '\t');
                    }
                }
            };
            presenter.CompletionItemSelected += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (IsOpen)
                {
                    var index = shown.FindIndex(x => ReferenceEquals(x.CompletionItem, e.SelectedItem));
                    if (index >= 0 && !IsHeader(shown[index].CompletionItem))
                    {
                        selected = index;
                        Show();
                    }
                }
            };
        }

        internal bool IsOpen => suggestions != null;

        /// <summary>The list of a view, or null when none was ever opened in it.</summary>
        internal static InvokedList Of(ITextView view) => view.Properties.TryGetProperty(typeof(InvokedList), out InvokedList list) ? list : null;

        private void LookAgain()
        {
            if (IsOpen && !refreshing)
            {
                refreshing = true;
                _ = ThreadHelper.JoinableTaskFactory.StartOnIdle(() =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    refreshing = false;
                    Refresh();
                });
            }
        }

        /// <summary>Opens the list at the caret of the view (does nothing where SQL Prompter has no suggestions). <paramref name="picker"/> starts in the column picker.</summary>
        public static void Open(IWpfTextView view, bool picker = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!SettingsService.Current.Behavior.SuggestionsEnabled || SnippetSession.IsActive(view))
                {
                    return;
                }

                var list = view.Properties.GetOrCreateSingletonProperty(() => Create(view));
                list?.OpenAtCaret(picker);
            }
            catch (Exception ex)
            {
                Log.Error("Opening the suggestion list failed", ex);
            }
        }

        private static InvokedList Create(IWpfTextView view)
        {
            var presenter = view.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionPresenter(view));
            var source = SqlCompletionSourceProvider.Current?.GetOrCreate(view) as SqlCompletionSource;
            return source == null ? null : new InvokedList(view, presenter, source);
        }

        private void OpenAtCaret(bool picker)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Close();
            var caret = view.Caret.Position.BufferPosition;
            var built = source.BuildForInvoke(caret);
            if (built == null || built.Items.Length == 0)
            {
                return;
            }

            suggestions = built;
            selected = 0;
            pickerMode = picker && built.Picker != null;
            BuildPickerItems();
            Filter();
            if (shown.Count == 0)
            {
                suggestions = null;
                return;
            }

            presenter.Show();
            Show();
        }

        /// <summary>Whether the column picker is offered at the caret of the view (the caret stands where columns are picked and they are known).</summary>
        internal static bool CanPick(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var list = view.Properties.GetOrCreateSingletonProperty(() => Create(view));
                return list != null && list.source.BuildForInvoke(view.Caret.Position.BufferPosition)?.Picker != null;
            }
            catch (Exception ex)
            {
                Log.Error("Looking for the column picker failed", ex);
                return false;
            }
        }

        private void BuildPickerItems()
        {
            pickerItems = new List<CompletionItem>();
            columnOf.Clear();
            checkedItems.Clear();
            nonAggregatedItem = null;
            var picker = suggestions.Picker;
            if (picker == null)
            {
                return;
            }

            if (picker.Clause == ColumnClause.GroupBy && picker.NonAggregated.Count > 0)
            {
                nonAggregatedItem = suggestions.Source.PickerNonAggregated(picker.NonAggregatedText(suggestions.Options));
                pickerItems.Add(nonAggregatedItem);
            }

            var several = picker.Columns.Select(c => c.Group).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            string group = null;
            foreach (var column in picker.Ordered(SettingsService.Current.Behavior.PickerTableOrder))
            {
                if (several && !string.Equals(group, column.Group, StringComparison.OrdinalIgnoreCase))
                {
                    group = column.Group;
                    pickerItems.Add(suggestions.Source.PickerHeader(group, column.Owner));
                }

                var item = suggestions.Source.PickerItem(column);
                columnOf[item] = column;
                pickerItems.Add(item);
            }
        }

        private static bool IsHeader(CompletionItem item) => item.Properties.ContainsProperty(SqlCompletionSource.PickerHeaderKey);

        /// <summary>The items that match the typed text, the ones that start with it first, in the order of the sort text.</summary>
        private void Filter()
        {
            var typed = TypedText();
            if (pickerMode)
            {
                FilterPicker(typed);
                return;
            }

            var matches = ListOrdering.Filter(suggestions.Items, typed, i => i.FilterText);
            shown = matches.Select(i => new CompletionItemWithHighlight(i, Highlight(i, typed))).ToList();
            selected = Math.Min(selected, Math.Max(0, shown.Count - 1));
        }

        /// <summary>The picker keeps the order of the sources; a heading stays while a column below it matches.</summary>
        private void FilterPicker(string typed)
        {
            var list = new List<CompletionItemWithHighlight>();
            CompletionItem header = null;
            foreach (var item in pickerItems)
            {
                if (IsHeader(item))
                {
                    header = item;
                    continue;
                }

                if (item != nonAggregatedItem && !ListOrdering.Matches(item.FilterText, typed))
                {
                    continue;
                }

                if (header != null)
                {
                    list.Add(new CompletionItemWithHighlight(header));
                    header = null;
                }

                list.Add(new CompletionItemWithHighlight(item, Highlight(item, typed)));
            }

            shown = list;
            if (selected >= shown.Count || selected < 0 || IsHeader(shown[selected].CompletionItem))
            {
                selected = FirstSelectable();
            }
        }

        private int FirstSelectable()
        {
            var index = shown.FindIndex(x => !IsHeader(x.CompletionItem));
            return index < 0 ? 0 : index;
        }

        private static ImmutableArray<Span> Highlight(CompletionItem item, string typed)
        {
            var at = typed.Length == 0 ? -1 : item.DisplayText.IndexOf(typed, StringComparison.OrdinalIgnoreCase);
            return at < 0 ? ImmutableArray<Span>.Empty : ImmutableArray.Create(new Span(at, typed.Length));
        }

        private string TypedText()
        {
            var span = suggestions.Span.GetSpan(view.TextSnapshot);
            return span.GetText();
        }

        private void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            presenter.CheckedItems = pickerMode ? checkedItems : null;
            presenter.SetFooter(CreateFooter());
            presenter.Present(shown, shown, selected, suggestions.SoftSelection && !pickerMode, false, null, false, suggestions.Span);
        }

        private ListFooter CreateFooter()
        {
            var behavior = SettingsService.Current.Behavior;
            var available = suggestions.Picker != null;
            var footer = new ListFooter
            {
                PickerAvailable = available,
                PickerMode = pickerMode,
                TableOrder = behavior.PickerTableOrder,
                ShowPicker = () => { ThreadHelper.ThrowIfNotOnUIThread(); SwitchMode(true); },
                ShowAll = () => { ThreadHelper.ThrowIfNotOnUIThread(); SwitchMode(false); },
                SelectAll = SelectAll,
                TableOrderChanged = SetTableOrder,
            };
            if (pickerMode)
            {
                footer.Hint = "Space: select columns, Tab: insert selected";
            }
            else if (available && behavior.ShowColumnPickerHint)
            {
                footer.Hint = "Ctrl+Left: column picker";
                footer.HideHint = HideHint;
            }

            return footer;
        }

        private void HideHint()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = SettingsService.Current.Clone();
            settings.Behavior.ShowColumnPickerHint = false;
            SettingsService.Update(settings);
            Show();
        }

        private void SetTableOrder(bool tableOrder)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = SettingsService.Current.Clone();
            settings.Behavior.PickerTableOrder = tableOrder;
            SettingsService.Update(settings);
            var keep = checkedItems.Select(i => columnOf.TryGetValue(i, out var c) ? c : null).Where(c => c != null).ToList();
            var includeNonAggregated = nonAggregatedItem != null && checkedItems.Contains(nonAggregatedItem);
            BuildPickerItems();
            foreach (var pair in columnOf.Where(p => keep.Any(k => k.InsertText == p.Value.InsertText && k.Group == p.Value.Group)))
            {
                checkedItems.Add(pair.Key);
            }

            if (includeNonAggregated && nonAggregatedItem != null)
            {
                checkedItems.Add(nonAggregatedItem);
            }

            Filter();
            Show();
        }

        /// <summary>Switches between the normal list and the column picker (Ctrl+Left, Ctrl+Right and the tabs of the status bar).</summary>
        internal void SwitchMode(bool picker)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!IsOpen || picker == pickerMode || (picker && suggestions.Picker == null))
            {
                return;
            }

            pickerMode = picker;
            selected = 0;
            Filter();
            Show();
        }

        private void SelectAll()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var entry in shown.Where(x => !IsHeader(x.CompletionItem) && x.CompletionItem != nonAggregatedItem))
            {
                checkedItems.Add(entry.CompletionItem);
            }

            Show();
        }

        private void Toggle(CompletionItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (IsHeader(item))
            {
                return;
            }

            if (!checkedItems.Remove(item))
            {
                checkedItems.Add(item);
            }

            Show();
        }

        private void Close()
        {
            if (suggestions == null)
            {
                return;
            }

            suggestions = null;
            pickerMode = false;
            presenter.CheckedItems = null;
            ThreadHelper.ThrowIfNotOnUIThread();
            presenter.SetFooter(null);
            presenter.Close();
        }

        /// <summary>A navigation or choosing key; returns true when the list used it (the editor must not).</summary>
        internal bool OnKey(Key key)
        {
            if (!IsOpen)
            {
                return false;
            }

            var handled = false;
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                switch (key)
                {
                    case Key.Down: Move(1); handled = true; break;
                    case Key.Up: Move(-1); handled = true; break;
                    case Key.PageDown: Move(SqlCompletionPresenter.VisibleRows); handled = true; break;
                    case Key.PageUp: Move(-SqlCompletionPresenter.VisibleRows); handled = true; break;
                    case Key.Escape: Close(); handled = true; break;
                    case Key.Left:
                        // Ctrl+Left
                        if (suggestions.Picker != null)
                        {
                            SwitchMode(true);
                            handled = true;
                        }

                        break;
                    case Key.Right:
                        // Ctrl+Right
                        if (pickerMode)
                        {
                            SwitchMode(false);
                            handled = true;
                        }

                        break;
                    case Key.Tab:
                        if (pickerMode)
                        {
                            InsertChecked();
                            handled = true;
                        }
                        else if (shown.Count > 0)
                        {
                            Commit(shown[selected].CompletionItem, '\t');
                            handled = true;
                        }

                        break;
                    case Key.Enter:
                        if (pickerMode)
                        {
                            InsertChecked();
                            handled = true;
                        }
                        else if (suggestions.SoftSelection || shown.Count == 0)
                        {
                            Close();
                        }
                        else
                        {
                            Commit(shown[selected].CompletionItem, '\n');
                            handled = true;
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Handling a key in the suggestion list failed", ex);
                Close();
            }

            return handled;
        }

        /// <summary>A typed letter hands over to the editor's own session (normal list); in the picker Space checks a column and letters filter.</summary>
        internal bool OnTypedChar(char c)
        {
            if (!IsOpen)
            {
                return false;
            }

            var handled = false;
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (pickerMode)
                {
                    if (c == ' ')
                    {
                        if (shown.Count > 0 && !IsHeader(shown[selected].CompletionItem))
                        {
                            Toggle(shown[selected].CompletionItem);
                        }

                        return true;
                    }

                    if (char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#')
                    {
                        return false;
                    }

                    // Any other character ends the picking; it is typed as usual.
                    Close();
                    return false;
                }

                if (char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || suggestions.SoftSelection || shown.Count == 0)
                {
                    Close();
                    return false;
                }

                var result = Commit(shown[selected].CompletionItem, c);
                handled = result.Behavior.HasFlag(CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
            }
            catch (Exception ex)
            {
                Log.Error("Handling a typed character in the suggestion list failed", ex);
                Close();
            }

            return handled;
        }

        private void Move(int step)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (shown.Count == 0)
            {
                return;
            }

            var direction = step < 0 ? -1 : 1;
            var index = Math.Max(0, Math.Min(shown.Count - 1, selected + step));
            // A heading is skipped.
            while (index >= 0 && index < shown.Count && IsHeader(shown[index].CompletionItem))
            {
                index += direction;
            }

            if (index < 0 || index >= shown.Count)
            {
                return;
            }

            selected = index;
            Show();
        }

        /// <summary>After Backspace, Delete or a caret move: close when the caret left the typed text, otherwise filter again.</summary>
        private void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!IsOpen)
            {
                return;
            }

            var span = suggestions.Span.GetSpan(view.TextSnapshot);
            var caret = view.Caret.Position.BufferPosition.Position;
            if (caret < span.Start.Position || caret > span.End.Position)
            {
                Close();
                return;
            }

            Filter();
            if (shown.Count == 0)
            {
                Close();
                return;
            }

            Show();
        }

        /// <summary>Inserts the item in place of the typed text and closes the list.</summary>
        private CommitResult Commit(CompletionItem item, char typedChar)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var span = suggestions.Span;
            Close();
            var buffer = view.TextBuffer;
            var result = SqlCommitManager.Commit(view, span, buffer, item, typedChar);
            if (!result.IsHandled)
            {
                // The editor's default commit: the text of the item replaces the typed text and the caret goes behind it.
                var target = span.GetSpan(buffer.CurrentSnapshot);
                buffer.Replace(target, item.InsertText);
                view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, target.Start.Position + item.InsertText.Length));
                return new CommitResult(isHandled: true, CommitBehavior.None);
            }

            return result;
        }

        /// <summary>
        /// Inserts the checked columns (the one that is selected when none is checked) as a list laid out by the formatting style, in place of the typed
        /// text. "All non-aggregated columns" comes first and the columns it already contains are not repeated.
        /// </summary>
        private void InsertChecked()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var picker = suggestions.Picker;
            var options = suggestions.Options;
            var chosen = checkedItems.ToList();
            if (chosen.Count == 0 && shown.Count > 0 && !IsHeader(shown[selected].CompletionItem))
            {
                chosen.Add(shown[selected].CompletionItem);
            }

            var texts = new List<string>();
            if (nonAggregatedItem != null && chosen.Remove(nonAggregatedItem))
            {
                texts.AddRange(picker.NonAggregated);
            }

            var columns = chosen.Where(columnOf.ContainsKey).Select(i => columnOf[i]).ToList();
            var ordered = picker.Ordered(SettingsService.Current.Behavior.PickerTableOrder).Where(columns.Contains).Select(c => c.InsertText);
            texts.AddRange(ordered.Where(t => !texts.Contains(t, StringComparer.OrdinalIgnoreCase)));
            var span = suggestions.Span;
            Close();
            if (texts.Count == 0)
            {
                return;
            }

            var text = FormattedList.Join(texts, options.Editor.LinePrefix, options.Editor);
            var buffer = view.TextBuffer;
            var target = span.GetSpan(buffer.CurrentSnapshot);
            buffer.Replace(target, text);
            view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, target.Start.Position + text.Length));
        }
    }
}
