using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    internal static class MatchHighlightLayer
    {
        public const string LayerName = "SqlPrompter.MatchHighlight";

        /// <summary>Behind the text, so that the colors of the text stay readable.</summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LayerName)]
        [Order(Before = PredefinedAdornmentLayers.Text)]
        internal static AdornmentLayerDefinition Definition = null;
    }

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class MatchHighlighterListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView.TextBuffer.ContentType.IsOfType(SqlContentType.Name))
            {
                textView.Properties.GetOrCreateSingletonProperty(() => new MatchHighlighter(textView));
            }
        }
    }

    /// <summary>
    /// Marks the other places of the script where the name under the caret appears, with a colored rectangle behind the text. The matches
    /// are computed when the caret moves or the text changes (lexing a script takes milliseconds).
    /// </summary>
    internal sealed class MatchHighlighter
    {
        private static readonly Brush Fill = CreateBrush();

        private readonly IWpfTextView view;
        private readonly IAdornmentLayer layer;
        private IReadOnlyList<TextSpan> matches = new TextSpan[0];
        private ITextSnapshot snapshot;

        public MatchHighlighter(IWpfTextView view)
        {
            this.view = view;
            layer = view.GetAdornmentLayer(MatchHighlightLayer.LayerName);
            view.Caret.PositionChanged += (s, e) => Refresh();
            view.LayoutChanged += (s, e) =>
            {
                // The caret does not report text changes, the layout does; a new layout also moves the text on the screen.
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

        private static Brush CreateBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x80, 0x8A, 0xB4, 0xE8));
            brush.Freeze();
            return brush;
        }

        private void Refresh()
        {
            try
            {
                var current = view.TextBuffer.CurrentSnapshot;
                IReadOnlyList<TextSpan> found = new TextSpan[0];
                if (SettingsService.Current.Behavior.HighlightMatches && view.Selection.IsEmpty)
                {
                    found = MatchFinder.Find(current.GetText(), view.Caret.Position.BufferPosition.Position);

                    // A name that appears once has nothing to be compared with.
                    if (found.Count < 2)
                    {
                        found = new TextSpan[0];
                    }
                }

                matches = found;
                snapshot = current;
                Draw();
            }
            catch (Exception ex)
            {
                Log.Error("Highlighting the matching names failed", ex);
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
                if (matches.Count == 0 || snapshot == null)
                {
                    return;
                }

                foreach (var match in matches)
                {
                    var span = new SnapshotSpan(snapshot, match.Start, match.Length).TranslateTo(view.TextSnapshot, SpanTrackingMode.EdgeExclusive);
                    var geometry = view.TextViewLines.GetMarkerGeometry(span);
                    if (geometry == null)
                    {
                        continue;
                    }

                    var rectangle = new Path { Data = geometry, Fill = Fill, IsHitTestVisible = false };
                    layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, rectangle, null);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Drawing the matching names failed", ex);
            }
        }
    }
}
