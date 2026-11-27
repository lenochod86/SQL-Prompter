using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    internal static class InsertPairHighlightLayer
    {
        public const string LayerName = "SqlPrompter.InsertPairHighlight";

        /// <summary>Above the highlight of the matching names, behind the text.</summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LayerName)]
        [Order(After = MatchHighlightLayer.LayerName, Before = PredefinedAdornmentLayers.Text)]
        internal static AdornmentLayerDefinition Definition = null;
    }

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class InsertPairHighlighterListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView.TextBuffer.ContentType.IsOfType(SqlContentType.Name))
            {
                textView.Properties.GetOrCreateSingletonProperty(() => new InsertPairHighlighter(textView));
            }
        }
    }

    /// <summary>
    /// Marks the value of the <c>INSERT</c> column at the caret, or the column of the value at the caret (#554), with a rectangle in the accent color
    /// of the theme with an outline, so it differs from the highlight of the matching names.
    /// </summary>
    internal sealed class InsertPairHighlighter
    {
        private readonly IWpfTextView view;
        private readonly IAdornmentLayer layer;
        private IReadOnlyList<TextSpan> partners = new TextSpan[0];
        private ITextSnapshot snapshot;

        public InsertPairHighlighter(IWpfTextView view)
        {
            this.view = view;
            layer = view.GetAdornmentLayer(InsertPairHighlightLayer.LayerName);
            view.Caret.PositionChanged += (s, e) => Refresh();
            view.Selection.SelectionChanged += (s, e) => Refresh();
            view.LayoutChanged += (s, e) =>
            {
                if (e.NewSnapshot != snapshot)
                {
                    Refresh();
                }
                else
                {
                    Draw();
                }
            };
        }

        private void Refresh()
        {
            try
            {
                var current = view.TextBuffer.CurrentSnapshot;
                IReadOnlyList<TextSpan> found = new TextSpan[0];
                if (SettingsService.Current.Behavior.HighlightInsertPairs && view.Selection.IsEmpty)
                {
                    found = InsertValuePairFinder.Find(current.GetText(), view.Caret.Position.BufferPosition.Position);
                }

                partners = found;
                snapshot = current;
                Draw();
            }
            catch (Exception ex)
            {
                Log.Error("Highlighting the INSERT columns and values failed", ex);
            }
        }

        private void Draw()
        {
            try
            {
                if (view.IsClosed || view.InLayout)
                {
                    return;
                }

                layer.RemoveAllAdornments();
                if (partners.Count == 0 || snapshot == null)
                {
                    return;
                }

                var accent = VSColorTheme.GetThemedColor(EnvironmentColors.SystemHighlightColorKey);
                var fill = Frozen(Color.FromArgb(0x48, accent.R, accent.G, accent.B));
                var outline = Frozen(Color.FromArgb(0xFF, accent.R, accent.G, accent.B));
                foreach (var partner in partners)
                {
                    var span = new SnapshotSpan(snapshot, partner.Start, partner.Length).TranslateTo(view.TextSnapshot, SpanTrackingMode.EdgeExclusive);
                    var geometry = view.TextViewLines.GetMarkerGeometry(span);
                    if (geometry == null)
                    {
                        continue;
                    }

                    var rectangle = new Path { Data = geometry, Fill = fill, Stroke = outline, StrokeThickness = 1, IsHitTestVisible = false };
                    layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, rectangle, null);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Drawing the INSERT columns and values failed", ex);
            }
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
