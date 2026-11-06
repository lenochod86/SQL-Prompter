using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Editing the custom fields of an expanded snippet in the text, as in Visual Studio: the fields are highlighted, Tab and Shift+Tab cycle
    /// between them (the field text is selected, so typing replaces it), Enter moves on and ends the editing after the last field,
    /// a field that appears several times is updated everywhere at once, Esc ends the editing at once, and so does leaving the snippet.
    /// The caret then goes to <c>$CURSOR$</c> (not when the snippet is left).
    /// </summary>
    internal sealed class SnippetSession
    {
        public const string LayerName = "SqlPrompter.SnippetFields";
        private const string PropertyKey = "SqlPrompter.SnippetSession";

        private readonly IWpfTextView view;
        private readonly ITextBuffer buffer;
        private readonly List<FieldSpans> fields;
        private readonly ITrackingSpan whole;
        private readonly ITrackingPoint cursor;
        private bool applying;
        private int current = -1;

        private SnippetSession(IWpfTextView view, int start, ExpandedSnippet expanded)
        {
            this.view = view;
            buffer = view.TextBuffer;
            var snapshot = buffer.CurrentSnapshot;
            whole = snapshot.CreateTrackingSpan(start, expanded.Text.Length, SpanTrackingMode.EdgeInclusive);
            cursor = snapshot.CreateTrackingPoint(start + expanded.CursorOffset, PointTrackingMode.Positive);
            fields = expanded.Fields
                .Select(f => new FieldSpans(f.Name, f.Offsets.Select(o => snapshot.CreateTrackingSpan(start + o, f.Length, SpanTrackingMode.EdgeInclusive)).ToList()))
                .ToList();
        }

        /// <summary>The session of the view while fields are being edited; null otherwise.</summary>
        public static SnippetSession Of(ITextView textView) =>
            textView.Properties.TryGetProperty(PropertyKey, out SnippetSession session) ? session : null;

        public static bool IsActive(ITextView textView) => Of(textView) != null;

        /// <summary>Starts editing the fields of a snippet that was just inserted at <paramref name="start"/>.</summary>
        public static void Start(IWpfTextView view, int start, ExpandedSnippet expanded)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Of(view)?.End(moveCaret: false);
            if (expanded.Fields.Count == 0)
            {
                return;
            }

            var session = new SnippetSession(view, start, expanded);
            view.Properties[PropertyKey] = session;
            session.buffer.Changed += session.OnBufferChanged;
            view.Caret.PositionChanged += session.OnCaretMoved;
            view.LayoutChanged += session.OnLayoutChanged;
            session.Activate(0);
        }

        /// <summary>Tab: the next field; after the last one it wraps around to the first. Tab never ends the editing.</summary>
        public void Next() => Activate(FieldNavigation.Cycle(current, fields.Count));

        /// <summary>Shift+Tab: the previous field; before the first one it wraps around to the last.</summary>
        public void Previous() => Activate(FieldNavigation.CycleBack(current, fields.Count));

        /// <summary>Enter: the next field; on the last one the editing ends and the caret goes to <c>$CURSOR$</c>.</summary>
        public void Advance()
        {
            var next = FieldNavigation.Advance(current, fields.Count);
            if (next < 0)
            {
                End(moveCaret: true);
            }
            else
            {
                Activate(next);
            }
        }

        /// <summary>Esc: ends the editing at once, the caret goes to <c>$CURSOR$</c>.</summary>
        public void Finish() => End(moveCaret: true);

        private void Activate(int index)
        {
            current = index;
            var span = fields[index].Occurrences[0].GetSpan(buffer.CurrentSnapshot);
            if (span.Length > 0)
            {
                view.Selection.Select(span, false);
            }
            else
            {
                view.Selection.Clear();
            }

            view.Caret.MoveTo(span.End);
            view.Caret.EnsureVisible();
            Redraw();
        }

        private void End(bool moveCaret)
        {
            buffer.Changed -= OnBufferChanged;
            view.Caret.PositionChanged -= OnCaretMoved;
            view.LayoutChanged -= OnLayoutChanged;
            view.Properties.RemoveProperty(PropertyKey);
            view.GetAdornmentLayer(LayerName).RemoveAllAdornments();
            if (moveCaret)
            {
                view.Selection.Clear();
                view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, cursor.GetPosition(buffer.CurrentSnapshot)));
                view.Caret.EnsureVisible();
            }
        }

        /// <summary>A change inside a field is copied to its other occurrences.</summary>
        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (applying)
            {
                return;
            }

            try
            {
                var after = e.After;
                if (whole.GetSpan(after).Length == 0)
                {
                    End(moveCaret: false);
                    return;
                }

                foreach (var change in e.Changes)
                {
                    var index = fields.FindIndex(f => f.Occurrences.Any(o =>
                    {
                        var s = o.GetSpan(after);
                        return change.NewPosition >= s.Start && change.NewPosition <= s.End;
                    }));
                    if (index < 0)
                    {
                        // Text typed or deleted outside the fields (or the snippet text is gone): the editing of the fields is over.
                        End(moveCaret: false);
                        return;
                    }

                    current = index;
                    var field = fields[index];
                    var source = field.Occurrences.First(o =>
                    {
                        var s = o.GetSpan(after);
                        return change.NewPosition >= s.Start && change.NewPosition <= s.End;
                    });
                    var text = source.GetText(after);
                    var others = field.Occurrences.Where(o => o != source && o.GetText(after) != text).ToList();
                    if (others.Count > 0)
                    {
                        // After this change has been fully processed: an edit made while the buffer is being changed is not allowed.
                        Defer(() => Mirror(field, source, text));
                    }
                }

                Defer(Redraw);
            }
            catch (Exception ex)
            {
                Log.Error("Updating the snippet fields failed", ex);
            }
        }

        /// <summary>Runs the action after the current text change has been fully processed, on the UI thread.</summary>
        private static void Defer(Action action)
        {
#pragma warning disable VSSDK007 // fire and forget on purpose: the action must not block the change being processed
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await System.Threading.Tasks.Task.Yield();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                action();
            }).Task.FileAndForget("SqlPrompter/SnippetSession");
#pragma warning restore VSSDK007
        }

        private void Mirror(FieldSpans field, ITrackingSpan source, string text)
        {
            if (Of(view) != this)
            {
                return;
            }

            applying = true;
            try
            {
                using (var edit = buffer.CreateEdit())
                {
                    var snapshot = buffer.CurrentSnapshot;
                    foreach (var other in field.Occurrences.Where(o => o != source))
                    {
                        var span = other.GetSpan(snapshot);
                        if (span.GetText() != text)
                        {
                            edit.Replace(span, text);
                        }
                    }

                    edit.Apply();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Mirroring a snippet field failed", ex);
            }
            finally
            {
                applying = false;
            }

            Redraw();
        }

        /// <summary>Leaving the text of the snippet ends the editing without moving the caret.</summary>
        private void OnCaretMoved(object sender, CaretPositionChangedEventArgs e)
        {
            if (applying)
            {
                return;
            }

            var snapshot = buffer.CurrentSnapshot;
            var span = whole.GetSpan(snapshot);
            var position = e.NewPosition.BufferPosition.Position;
            if (position < span.Start || position > span.End)
            {
                End(moveCaret: false);
            }
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e) => Redraw();

        private void Redraw()
        {
            if (Of(view) != this)
            {
                return;
            }

            try
            {
                var layer = view.GetAdornmentLayer(LayerName);
                layer.RemoveAllAdornments();
                var snapshot = buffer.CurrentSnapshot;
                for (var i = 0; i < fields.Count; i++)
                {
                    foreach (var occurrence in fields[i].Occurrences)
                    {
                        var span = occurrence.GetSpan(snapshot);
                        var element = Draw(span, i == current);
                        if (element != null)
                        {
                            layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, element, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Drawing the snippet fields failed", ex);
            }
        }

        private FrameworkElement Draw(SnapshotSpan span, bool active)
        {
            var lines = view.TextViewLines;
            if (lines == null || !lines.ContainsBufferPosition(span.Start))
            {
                return null;
            }

            Geometry geometry = span.Length > 0
                ? lines.GetMarkerGeometry(span)
                : null;
            if (geometry == null)
            {
                // An empty field is drawn as a thin bar at its position.
                var bounds = lines.GetCharacterBounds(span.Start);
                geometry = new RectangleGeometry(new Rect(bounds.Left - 1, bounds.Top, 3, bounds.Height));
            }

            var color = Color.FromRgb(0x56, 0x9C, 0xD6);
            var path = new Path
            {
                Data = geometry,
                Fill = new SolidColorBrush(Color.FromArgb(active ? (byte)0x60 : (byte)0x26, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(Color.FromArgb(active ? (byte)0xFF : (byte)0x90, color.R, color.G, color.B)),
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            return path;
        }

        private sealed class FieldSpans
        {
            public FieldSpans(string name, List<ITrackingSpan> occurrences)
            {
                Name = name;
                Occurrences = occurrences;
            }

            public string Name { get; }

            public List<ITrackingSpan> Occurrences { get; }
        }
    }
}
