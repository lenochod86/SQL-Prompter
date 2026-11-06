using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Ssms
{
    internal static class StarHintLayer
    {
        public const string LayerName = "SqlPrompter.StarHint";

        /// <summary>Above the text, so that the hint is not covered by it.</summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LayerName)]
        [Order(After = PredefinedAdornmentLayers.Text)]
        internal static AdornmentLayerDefinition Definition = null;
    }

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class StarHintListener : IWpfTextViewCreationListener
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView.TextBuffer.ContentType.IsOfType(SqlContentType.Name))
            {
                textView.Properties.GetOrCreateSingletonProperty(() => new StarHint(textView, Broker));
            }
        }
    }

    /// <summary>
    /// A small note under the caret, "Tab: expand columns", while the caret stands right behind a <c>*</c> that Tab would expand. It asks the same
    /// question as the Tab handler (<see cref="ExpandStarHandler.Find"/>), and only when the character before the caret is a star, so moving
    /// the caret elsewhere costs nothing.
    /// </summary>
    internal sealed class StarHint
    {
        private readonly IWpfTextView view;
        private readonly IAsyncCompletionBroker broker;
        private readonly IAdornmentLayer layer;

        public StarHint(IWpfTextView view, IAsyncCompletionBroker broker)
        {
            this.view = view;
            this.broker = broker;
            layer = view.GetAdornmentLayer(StarHintLayer.LayerName);
            view.Caret.PositionChanged += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); Refresh(); };
            view.LayoutChanged += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); Refresh(); };
            view.LostAggregateFocus += (s, e) => Clear();
        }

        private void Clear() => layer.RemoveAllAdornments();

        private void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (view.IsClosed || view.InLayout)
                {
                    return;
                }

                Clear();
                var caret = view.Caret.Position.BufferPosition;
                if (!SettingsService.Current.Behavior.ShowStarHint || !view.HasAggregateFocus || caret.Position == 0 || caret.Snapshot[caret.Position - 1] != '*')
                {
                    return;
                }

                if (ExpandStarHandler.Find(view, broker, out _) == null)
                {
                    return;
                }

                var line = view.Caret.ContainingTextViewLine;
                var text = new TextBlock
                {
                    Text = "Tab: expand columns",
                    FontSize = Math.Max(10, view.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize * 0.8),
                    Padding = new Thickness(5, 1, 5, 1),
                    Foreground = SystemColors.InfoTextBrush,
                    IsHitTestVisible = false,
                };
                var box = new Border
                {
                    Child = text,
                    Background = SystemColors.InfoBrush,
                    BorderBrush = SystemColors.ControlDarkBrush,
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(box, view.Caret.Left - view.ViewportLeft);
                Canvas.SetTop(box, line.Bottom - view.ViewportTop + 1);
                layer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, null, box, null);
            }
            catch (Exception ex)
            {
                Log.Error("Showing the expand * hint failed", ex);
            }
        }
    }
}
