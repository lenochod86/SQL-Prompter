using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Research prototype (#61): our own completion list. Each row shows the kind icon, the name with the typed text
    /// highlighted, a detail (column type) after it and the suffix (alias, schema, "SQL Prompter") on the right.
    /// </summary>
    [Export(typeof(ICompletionPresenterProvider))]
    [Name("SqlPrompter.Presenter")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = "default")]
    internal sealed class SqlCompletionPresenterProvider : ICompletionPresenterProvider
    {
        public CompletionPresenterOptions Options { get; } = new CompletionPresenterOptions(12);

        public ICompletionPresenter GetOrCreate(ITextView textView)
        {
            Log.Write("Presenter requested for a text view.");
            return textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionPresenter((IWpfTextView)textView));
        }
    }

    internal sealed class SqlCompletionPresenter : ICompletionPresenter
    {
        private const double RowHeight = 22;

        private readonly IWpfTextView view;
        private readonly Popup popup;
        private readonly StackPanel rows = new StackPanel();
        private readonly ScrollViewer scroll;
        private object lastItemList;
        private IReadOnlyList<CompletionItemWithHighlight> items = new CompletionItemWithHighlight[0];
        private bool soft;

        public SqlCompletionPresenter(IWpfTextView view)
        {
            this.view = view;
            scroll = new ScrollViewer
            {
                Content = rows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                MinWidth = 420,
                MaxWidth = 900,
                MaxHeight = RowHeight * 12,
            };
            var border = new Border { BorderThickness = new Thickness(1), Child = scroll, Focusable = false };
            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            popup = new Popup
            {
                Child = border,
                AllowsTransparency = false,
                StaysOpen = true,
                Focusable = false,
                Placement = PlacementMode.Relative,
                PlacementTarget = view.VisualElement,
            };
        }

#pragma warning disable CS0067 // never raised in the prototype: no filter buttons yet
        public event EventHandler<CompletionFilterChangedEventArgs> FiltersChanged;

        public event EventHandler<CompletionClosedEventArgs> CompletionClosed;
#pragma warning restore CS0067

        public event EventHandler<CompletionItemSelectedEventArgs> CompletionItemSelected;

        public event EventHandler<CompletionItemEventArgs> CommitRequested;



        public void Open(IAsyncCompletionSession session, CompletionPresentationViewModel presentation)
        {
            Log.Write("Presenter opened with " + presentation.ItemList.Count + " items.");
            Update(session, presentation);
            popup.IsOpen = true;
        }

        public void Update(IAsyncCompletionSession session, CompletionPresentationViewModel presentation)
        {
            soft = presentation.UseSoftSelection;
            if (!ReferenceEquals(lastItemList, presentation.ItemList))
            {
                lastItemList = presentation.ItemList;
                items = presentation.ItemList.ToList();
                Rebuild();
            }

            Select(presentation.SelectedItemIndex);
            Position(presentation);
        }

        public void Dispose()
        {
            popup.IsOpen = false;
        }

        public void Close()
        {
            popup.IsOpen = false;
        }

        private void Position(CompletionPresentationViewModel presentation)
        {
            try
            {
                var span = presentation.ApplicableToSpan.GetSpan(view.TextSnapshot);
                var line = view.GetTextViewLineContainingBufferPosition(span.Start);
                var bounds = line.GetCharacterBounds(span.Start);
                popup.HorizontalOffset = bounds.Left - view.ViewportLeft;
                popup.VerticalOffset = bounds.Bottom - view.ViewportTop + 2;
            }
            catch (Exception ex)
            {
                Log.Error("Positioning the presenter failed", ex);
            }
        }

        private void Rebuild()
        {
            rows.Children.Clear();
            for (var i = 0; i < items.Count; i++)
            {
                rows.Children.Add(CreateRow(items[i], i));
            }
        }

        private FrameworkElement CreateRow(CompletionItemWithHighlight entry, int index)
        {
            var item = entry.CompletionItem;
            var grid = new Grid { Height = RowHeight, Background = Brushes.Transparent, Focusable = false };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new CrispImage
            {
                Moniker = new ImageMoniker { Guid = item.Icon.ImageId.Guid, Id = item.Icon.ImageId.Id },
                Width = 16,
                Height = 16,
                Margin = new Thickness(6, 0, 6, 0),
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            text.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            AddHighlighted(text, item.DisplayText, entry.HighlightedSpans);
            if (item.Properties.TryGetProperty(SqlCompletionSource.DetailKey, out string detail))
            {
                var run = new Run(" " + detail) { FontSize = 11 };
                run.SetResourceReference(TextElement.ForegroundProperty, VsBrushes.ControlLinkTextKey);
                text.Inlines.Add(run);
            }

            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var suffix = new TextBlock
            {
                Text = item.Suffix,
                Margin = new Thickness(16, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6,
                FontSize = 11,
            };
            suffix.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetColumn(suffix, 2);
            grid.Children.Add(suffix);

            grid.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount >= 2)
                {
                    CommitRequested?.Invoke(this, new CompletionItemEventArgs(item));
                }
                else
                {
                    CompletionItemSelected?.Invoke(this, new CompletionItemSelectedEventArgs(item, suggestionItemSelected: false));
                }

                e.Handled = true;
            };
            if (item.Properties.TryGetProperty("SqlPrompter.Description", out string description))
            {
                grid.ToolTip = description;
            }

            return grid;
        }

        private static void AddHighlighted(TextBlock block, string text, ImmutableArray<Microsoft.VisualStudio.Text.Span> spans)
        {
            var position = 0;
            foreach (var span in spans.OrderBy(s => s.Start))
            {
                if (span.Start < position || span.End > text.Length)
                {
                    continue;
                }

                if (span.Start > position)
                {
                    block.Inlines.Add(new Run(text.Substring(position, span.Start - position)));
                }

                block.Inlines.Add(new Run(text.Substring(span.Start, span.Length)) { FontWeight = FontWeights.Bold });
                position = span.End;
            }

            if (position < text.Length)
            {
                block.Inlines.Add(new Run(text.Substring(position)));
            }
        }

        private void Select(int index)
        {
            for (var i = 0; i < rows.Children.Count; i++)
            {
                var row = (Grid)rows.Children[i];
                if (i == index)
                {
                    if (soft)
                    {
                        row.SetResourceReference(Panel.BackgroundProperty, VsBrushes.ToolWindowBorderKey);
                    }
                    else
                    {
                        row.SetResourceReference(Panel.BackgroundProperty, VsBrushes.HighlightKey);
                    }
                }
                else
                {
                    row.ClearValue(Panel.BackgroundProperty);
                    row.Background = Brushes.Transparent;
                }
            }

            if (index >= 0 && index < rows.Children.Count)
            {
                ((FrameworkElement)rows.Children[index]).BringIntoView();
            }
        }
    }
}
