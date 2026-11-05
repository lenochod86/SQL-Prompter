using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Completion;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The signature of the function or procedure being called with the current parameter in bold, above the caret line. It follows the caret
    /// while the caret stays inside the call and goes away when it leaves the call or the editor loses the focus.
    /// </summary>
    internal sealed class ParameterInfoPopup
    {
        private const string PropertyKey = "SqlPrompter.ParameterInfo";

        private readonly IWpfTextView view;
        private readonly TextBlock text;
        private readonly Popup popup;
        private bool active;

        private ParameterInfoPopup(IWpfTextView view)
        {
            this.view = view;
            text = new TextBlock { Margin = new Thickness(6, 3, 6, 3), TextWrapping = TextWrapping.NoWrap, Focusable = false };
            text.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            var border = new Border { BorderThickness = new Thickness(1), Child = text, Focusable = false };
            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            popup = new Popup { Child = border, AllowsTransparency = false, StaysOpen = true, Focusable = false, Placement = PlacementMode.Relative };
            view.Caret.PositionChanged += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Refresh();
            };
            view.LostAggregateFocus += (s, e) => Hide();
            // The caret event is not raised when text is changed (typed, deleted, pasted), the layout is.
            view.LayoutChanged += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Refresh();
            };
            view.Closed += (s, e) => Hide();
        }

        public static bool IsShown(ITextView textView) =>
            textView.Properties.TryGetProperty(PropertyKey, out ParameterInfoPopup shown) && shown.active;

        /// <summary>Shows the info for the call at the caret, or nothing when the caret is not in a call of a known function or procedure.</summary>
        public static void Show(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var shown = view.Properties.GetOrCreateSingletonProperty(PropertyKey, () => new ParameterInfoPopup(view));
            shown.active = true;
            shown.Refresh();
        }

        public static void Hide(ITextView textView)
        {
            if (textView.Properties.TryGetProperty(PropertyKey, out ParameterInfoPopup shown))
            {
                shown.Hide();
            }
        }

        private void Hide()
        {
            active = false;
            popup.IsOpen = false;
        }

        private void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!active)
            {
                return;
            }

            try
            {
                var caret = view.Caret.Position.BufferPosition;
                var snapshot = caret.Snapshot;
                ParameterInfo info = null;
                var metadata = MetadataService.Current?.CacheForActiveWindow(out _, SqlBatch.FindUsedDatabase(snapshot, caret.Position))?.Current;
                if (metadata != null && SqlBatch.TryGet(snapshot, caret.Position, out var batchStart, out var batch))
                {
                    info = ParameterInfoFinder.Find(batch, caret.Position - batchStart, metadata);
                }

                if (info == null)
                {
                    Hide();
                    return;
                }

                Render(info);
                Position(caret.Position);
                popup.IsOpen = true;
            }
            catch (Exception ex)
            {
                Log.Error("Showing the parameter info failed", ex);
                Hide();
            }
        }

        private void Render(ParameterInfo info)
        {
            text.Inlines.Clear();
            text.Inlines.Add(new Run(info.Name + "("));
            for (var i = 0; i < info.Parameters.Count; i++)
            {
                if (i > 0)
                {
                    text.Inlines.Add(new Run(", "));
                }

                var run = new Run(info.Parameters[i]);
                if (i == info.Current)
                {
                    run.FontWeight = FontWeights.Bold;
                }

                text.Inlines.Add(run);
            }

            text.Inlines.Add(new Run(")"));
        }

        private void Position(int position)
        {
            var line = view.GetTextViewLineContainingBufferPosition(new Microsoft.VisualStudio.Text.SnapshotPoint(view.TextSnapshot, Math.Min(position, view.TextSnapshot.Length)));
            if (line == null)
            {
                return;
            }

            var bounds = line.GetCharacterBounds(new Microsoft.VisualStudio.Text.SnapshotPoint(view.TextSnapshot, Math.Min(position, view.TextSnapshot.Length)));
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var height = text.DesiredSize.Height + 2;
            popup.PlacementTarget = view.VisualElement;
            popup.HorizontalOffset = Math.Max(0, bounds.Left - view.ViewportLeft);

            // Above the line, where the list of suggestions does not cover it; below when there is no room above.
            var above = bounds.Top - view.ViewportTop - height - 2;
            popup.VerticalOffset = above >= 0 ? above : bounds.Bottom - view.ViewportTop + 2;
        }
    }
}
