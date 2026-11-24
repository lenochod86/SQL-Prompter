using System;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    internal static class RunHighlightLayer
    {
        public const string LayerName = "SqlPrompter.RunHighlight";

        /// <summary>Behind the text, so that the colors of the text stay readable.</summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LayerName)]
        [Order(Before = PredefinedAdornmentLayers.Text)]
        internal static AdornmentLayerDefinition Definition = null;
    }

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class RunHighlighterListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView.TextBuffer.ContentType.IsOfType(SqlContentType.Name))
            {
                textView.Properties.GetOrCreateSingletonProperty(() => new RunHighlighter(textView));
            }
        }
    }

    /// <summary>
    /// A subtle green background behind the statements that were just run (Shift+F5, Ctrl+Shift+F5), for about a second (#420). It is an adornment: the text and the
    /// undo stack are not touched. The next run replaces it; an edit, closing the editor or the time ends it.
    /// </summary>
    internal sealed class RunHighlighter
    {
        private static readonly TimeSpan Duration = TimeSpan.FromSeconds(1);

        private readonly IWpfTextView view;
        private readonly IAdornmentLayer layer;
        private readonly DispatcherTimer timer;
        private ITrackingSpan span;

        public RunHighlighter(IWpfTextView view)
        {
            this.view = view;
            layer = view.GetAdornmentLayer(RunHighlightLayer.LayerName);
            timer = new DispatcherTimer { Interval = Duration };
            timer.Tick += (s, e) => Clear();
            view.LayoutChanged += (s, e) => Draw();
            view.TextBuffer.Changed += (s, e) => Clear();
            view.Closed += (s, e) => Clear();
        }

        /// <summary>Marks <paramref name="executed"/> of the view for a moment; nothing happens when the view has no highlighter (not an SQL editor).</summary>
        public static void Show(IWpfTextView view, SnapshotSpan executed)
        {
            if (view.Properties.TryGetProperty(typeof(RunHighlighter), out RunHighlighter highlighter))
            {
                highlighter.Start(executed);
            }
        }

        private void Start(SnapshotSpan executed)
        {
            try
            {
                span = executed.Snapshot.CreateTrackingSpan(executed, SpanTrackingMode.EdgeExclusive);
                timer.Stop();
                timer.Start();
                Draw();
            }
            catch (Exception ex)
            {
                Log.Error("Marking the executed statements failed", ex);
            }
        }

        private void Clear()
        {
            timer.Stop();
            span = null;
            try
            {
                layer.RemoveAllAdornments();
            }
            catch (Exception ex)
            {
                Log.Error("Removing the mark of the executed statements failed", ex);
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
                if (span == null)
                {
                    return;
                }

                var current = span.GetSpan(view.TextSnapshot);
                var geometry = view.TextViewLines.GetMarkerGeometry(current);
                if (geometry == null)
                {
                    return;
                }

                var rectangle = new Path { Data = geometry, Fill = CreateBrush(), IsHitTestVisible = false };
                layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, current, null, rectangle, null);
            }
            catch (Exception ex)
            {
                Log.Error("Drawing the mark of the executed statements failed", ex);
            }
        }

        /// <summary>
        /// A see-through green, so that it takes on the background of the editor in every theme: a stronger one on a dark background, a lighter one on a light
        /// one. Made again for every drawing so that a changed theme or color shows.
        /// </summary>
        private static Brush CreateBrush()
        {
            var background = EditorColors.Background();
            var dark = (background.R * 299 + background.G * 587 + background.B * 114) / 1000 < 128;
            var brush = new SolidColorBrush(dark ? Color.FromArgb(0x50, 0x3F, 0xB9, 0x50) : Color.FromArgb(0x48, 0x2E, 0xA0, 0x43));
            brush.Freeze();
            return brush;
        }
    }
}
