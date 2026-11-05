using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Analysis;

namespace SqlPrompter.Ssms
{
    internal static class SquiggleLayer
    {
        public const string LayerName = "SqlPrompter.Squiggles";

        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LayerName)]
        [Order(After = PredefinedAdornmentLayers.Text)]
        internal static AdornmentLayerDefinition Definition = null;
    }

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SquiggleListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView.TextBuffer.ContentType.IsOfType(SqlContentType.Name))
            {
                textView.Properties.GetOrCreateSingletonProperty(() => new Squiggles(textView));
            }
        }
    }

    /// <summary>
    /// Underlines what <see cref="CodeAnalyzer"/> finds with a wavy line (orange for warnings, gray for hints) and shows the message in a
    /// tooltip when the mouse rests on it. The analysis runs on a background thread a moment after the last change. The tooltip is driven
    /// by mouse movement over the editor instead of hit testing the squiggle, so the squiggles never take clicks from the text.
    /// </summary>
    internal sealed class Squiggles
    {
        private static readonly Brush WarningBrush = Freeze(Color.FromRgb(0xE8, 0x9B, 0x2D));
        private static readonly Brush HintBrush = Freeze(Color.FromRgb(0x8A, 0x96, 0xA8));

        private readonly IWpfTextView view;
        private readonly IAdornmentLayer layer;
        private readonly DispatcherTimer delay;
        private readonly ToolTip tip = new ToolTip { Placement = PlacementMode.Mouse, StaysOpen = true };
        private IReadOnlyList<CodeIssue> issues = new CodeIssue[0];
        private ITextSnapshot snapshot;
        private int version;
        private CodeIssue shown;

        public Squiggles(IWpfTextView view)
        {
            this.view = view;
            layer = view.GetAdornmentLayer(SquiggleLayer.LayerName);
            delay = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
            delay.Tick += (s, e) =>
            {
                delay.Stop();
                Analyze();
            };
            tip.PlacementTarget = view.VisualElement;
            view.TextBuffer.Changed += (s, e) =>
            {
                delay.Stop();
                delay.Start();
            };
            view.LayoutChanged += (s, e) => Draw();
            view.VisualElement.MouseMove += OnMouseMove;
            view.VisualElement.MouseLeave += (s, e) => HideTip();
            view.Closed += (s, e) =>
            {
                delay.Stop();
                HideTip();
            };
#pragma warning disable VSSDK007 // fire and forget on purpose: the handler must return at once
            EventHandler changed = (s, e) => ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Analyze();
            }).Task.FileAndForget("sqlprompter/analysis");
#pragma warning restore VSSDK007
            SettingsService.Changed += changed;
            view.Closed += (s, e) => SettingsService.Changed -= changed;
            delay.Start();
        }

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void Analyze()
        {
            try
            {
                if (view.IsClosed)
                {
                    return;
                }

                var settings = SettingsService.Current.Analysis;
                var current = view.TextBuffer.CurrentSnapshot;
                if (!settings.Enabled)
                {
                    SetIssues(new CodeIssue[0], current);
                    return;
                }

                var run = ++version;
                var text = current.GetText();
#pragma warning disable VSSDK007 // fire and forget on purpose: the result comes back through SetIssues
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        var found = await Task.Run(() => CodeAnalyzer.Analyze(text, settings));
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // A newer run or a closed window makes this result obsolete.
                        if (run == version && !view.IsClosed)
                        {
                            SetIssues(found, current);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Code analysis failed", ex);
                    }
                }).Task.FileAndForget("sqlprompter/analysis");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Log.Error("Starting the code analysis failed", ex);
            }
        }

        private void SetIssues(IReadOnlyList<CodeIssue> found, ITextSnapshot of)
        {
            issues = found;
            snapshot = of;
            HideTip();
            Draw();
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
                if (issues.Count == 0 || snapshot == null)
                {
                    return;
                }

                foreach (var issue in issues)
                {
                    var span = new SnapshotSpan(snapshot, issue.Start, issue.Length).TranslateTo(view.TextSnapshot, SpanTrackingMode.EdgeExclusive);
                    var geometry = view.TextViewLines.GetMarkerGeometry(span);
                    if (geometry == null)
                    {
                        continue;
                    }

                    var bounds = geometry.Bounds;
                    var line = new Polyline
                    {
                        Points = Zigzag(bounds.Left, bounds.Right, bounds.Bottom - 1.5),
                        Stroke = issue.Severity == CodeIssueSeverity.Warning ? WarningBrush : HintBrush,
                        StrokeThickness = 1,
                        IsHitTestVisible = false,
                    };
                    layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, line, null);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Drawing the squiggles failed", ex);
            }
        }

        private static PointCollection Zigzag(double from, double to, double y)
        {
            var points = new PointCollection();
            var up = true;
            for (var x = from; x < to; x += 2)
            {
                points.Add(new Point(x, up ? y - 1.5 : y + 1));
                up = !up;
            }

            points.Add(new Point(to, up ? y - 1.5 : y + 1));
            return points;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (issues.Count == 0 || snapshot == null || snapshot != view.TextSnapshot)
                {
                    HideTip();
                    return;
                }

                var point = e.GetPosition(view.VisualElement);
                var line = view.TextViewLines.GetTextViewLineContainingYCoordinate(point.Y + view.ViewportTop);
                var position = line?.GetBufferPositionFromXCoordinate(point.X + view.ViewportLeft);
                var issue = position == null ? null : issues.FirstOrDefault(i => i.Start <= position.Value.Position && position.Value.Position < i.Start + i.Length);
                if (issue == null)
                {
                    HideTip();
                }
                else if (issue != shown)
                {
                    shown = issue;
                    tip.Content = issue.Message;
                    tip.IsOpen = false;
                    tip.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Showing the analysis tooltip failed", ex);
            }
        }

        private void HideTip()
        {
            shown = null;
            tip.IsOpen = false;
        }
    }
}
