using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Editing;

#pragma warning disable VSTHRD010 // the colors are read on the UI thread (adornments are drawn there)
namespace SqlPrompter.Ssms
{
    internal static class BlockKeywordLayer
    {
        public const string LayerName = "SqlPrompter.BlockKeywords";

        /// <summary>Behind the text, so that the colors of the text stay readable.</summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LayerName)]
        [Order(Before = PredefinedAdornmentLayers.Text)]
        internal static AdornmentLayerDefinition Definition = null;
    }

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class BlockKeywordHighlighterListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView.TextBuffer.ContentType.IsOfType(SqlContentType.Name))
            {
                textView.Properties.GetOrCreateSingletonProperty(() => new BlockKeywordHighlighter(textView));
            }
        }
    }

    /// <summary>
    /// Marks the keywords that belong together with the keyword at the caret (BEGIN and END, TRY and CATCH, CASE and END, ...; #435) with a rectangle
    /// behind the text, in the color the editor uses for highlighted references (so it follows the theme), with an outline in the text color so it
    /// differs from the highlight of the word under the caret. The keywords are searched when the caret moves or the text changes (a quick scan of
    /// the tokens of the text; the lexing takes milliseconds).
    /// </summary>
    internal sealed class BlockKeywordHighlighter
    {
        private readonly IWpfTextView view;
        private readonly IAdornmentLayer layer;
        private IReadOnlyList<TextSpan> keywords = new TextSpan[0];
        private ITextSnapshot snapshot;

        public BlockKeywordHighlighter(IWpfTextView view)
        {
            this.view = view;
            layer = view.GetAdornmentLayer(BlockKeywordLayer.LayerName);
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

        private void Refresh()
        {
            try
            {
                var current = view.TextBuffer.CurrentSnapshot;
                IReadOnlyList<TextSpan> found = new TextSpan[0];
                if (SettingsService.Current.Behavior.HighlightBlockKeywords && view.Selection.IsEmpty)
                {
                    found = BlockKeywordFinder.Find(current.GetText(), view.Caret.Position.BufferPosition.Position);
                }

                keywords = found;
                snapshot = current;
                Draw();
            }
            catch (Exception ex)
            {
                Log.Error("Looking for the keywords of the block failed", ex);
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
                if (keywords.Count == 0 || snapshot == null)
                {
                    return;
                }

                var fill = FillBrush();
                var outline = OutlineBrush();
                foreach (var keyword in keywords)
                {
                    var span = new SnapshotSpan(snapshot, keyword.Start, keyword.Length).TranslateTo(view.TextSnapshot, SpanTrackingMode.EdgeExclusive);
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
                Log.Error("Drawing the keywords of the block failed", ex);
            }
        }

        /// <summary>The color of the editor's highlighted references (the word under the caret in the theme), or the text color of the theme with some transparency.</summary>
        private Brush FillBrush()
        {
            try
            {
                var model = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
                var map = model?.GetService<IEditorFormatMapService>()?.GetEditorFormatMap(view);
                var properties = map?.GetProperties("MarkerFormatDefinition/HighlightedReference");
                if (properties != null)
                {
                    if (properties.Contains("Background") && properties["Background"] is SolidColorBrush brush)
                    {
                        return Frozen(brush.Color);
                    }

                    if (properties.Contains("BackgroundColor") && properties["BackgroundColor"] is Color color)
                    {
                        return Frozen(color);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reading the highlight color of the editor failed", ex);
            }

            var themed = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
            return Frozen(Color.FromArgb(0x40, themed.R, themed.G, themed.B));
        }

        private static Brush OutlineBrush()
        {
            var themed = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
            return Frozen(Color.FromArgb(0x90, themed.R, themed.G, themed.B));
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
