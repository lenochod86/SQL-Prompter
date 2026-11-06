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

namespace SqlPrompter.Ssms
{
    /// <summary>The suggestions for the place of the caret, built by <see cref="SqlCompletionSource.BuildForInvoke"/>.</summary>
    internal sealed class InvokedSuggestions
    {
        public InvokedSuggestions(ImmutableArray<CompletionItem> items, ITrackingSpan span, bool softSelection)
        {
            Items = items;
            Span = span;
            SoftSelection = softSelection;
        }

        public ImmutableArray<CompletionItem> Items { get; }

        /// <summary>The text typed so far that the chosen item replaces.</summary>
        public ITrackingSpan Span { get; }

        /// <summary>Where the user probably types a name of their own: Enter keeps what was typed, only Tab inserts the suggestion.</summary>
        public bool SoftSelection { get; }
    }

    /// <summary>
    /// The suggestion list that SQL Prompter runs itself, opened by Ctrl+Space (or the menu command Show Suggestions). The editor's completion session
    /// does not work for a list that is started from outside the typing path (its source is never asked for items, #144), so this controller does what
    /// the session would: it asks our source for the items, filters them, shows them in the same popup (<see cref="SqlCompletionPresenter"/>), moves
    /// the selection with the keys and inserts the chosen item with the same commit code (<see cref="SqlCommitManager.Commit"/>).
    /// As soon as the user types a letter the editor's own session takes over (it starts by itself on typing), so this list only has to handle the
    /// moment between the key and the first typed character, and the keys that choose.
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
                    Commit(e.Item, '\t');
                }
            };
            presenter.CompletionItemSelected += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (IsOpen)
                {
                    var index = shown.FindIndex(x => ReferenceEquals(x.CompletionItem, e.SelectedItem));
                    if (index >= 0)
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

        /// <summary>Opens the list at the caret of the view (does nothing where SQL Prompter has no suggestions).</summary>
        public static void Open(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!SettingsService.Current.Behavior.SuggestionsEnabled || SnippetSession.IsActive(view))
                {
                    return;
                }

                var list = view.Properties.GetOrCreateSingletonProperty(() => Create(view));
                list?.OpenAtCaret();
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

        private void OpenAtCaret()
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
            Filter();
            if (shown.Count == 0)
            {
                suggestions = null;
                return;
            }

            presenter.Show();
            Show();
        }

        /// <summary>The items that match the typed text, the ones that start with it first, in the order of the sort text.</summary>
        private void Filter()
        {
            var typed = TypedText();
            var all = suggestions.Items;
            IEnumerable<CompletionItem> matches;
            if (typed.Length == 0)
            {
                matches = all;
            }
            else
            {
                matches = all.Where(i => i.FilterText.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(i => i.FilterText.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
            }

            shown = matches.Select(i => new CompletionItemWithHighlight(i, Highlight(i, typed))).ToList();
            selected = Math.Min(selected, Math.Max(0, shown.Count - 1));
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
            presenter.Present(shown, shown, selected, suggestions.SoftSelection, false, null, false, suggestions.Span);
        }

        private void Close()
        {
            if (suggestions == null)
            {
                return;
            }

            suggestions = null;
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
                    case Key.Tab:
                        if (shown.Count > 0)
                        {
                            Commit(shown[selected].CompletionItem, '\t');
                            handled = true;
                        }

                        break;
                    case Key.Enter:
                        if (suggestions.SoftSelection || shown.Count == 0)
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

        /// <summary>A typed letter hands over to the editor's own session; a character that ends a word inserts the choice first (as in the session).</summary>
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

            selected = Math.Max(0, Math.Min(shown.Count - 1, selected + step));
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
    }
}
