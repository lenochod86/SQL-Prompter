using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class CtrlTapListener : IWpfTextViewCreationListener
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView.TextBuffer.ContentType.IsOfType(SqlContentType.Name))
            {
                textView.Properties.GetOrCreateSingletonProperty(() => new CtrlTap(textView, Broker));
            }
        }
    }

    /// <summary>
    /// Opens the quick actions popup when the Ctrl key is pressed and released on its own: no other key and no mouse button in between, so Ctrl+C,
    /// Ctrl+Space, Ctrl+click and the chords of SSMS are not affected.
    /// </summary>
    internal sealed class CtrlTap
    {
        private readonly IWpfTextView view;
        private readonly IAsyncCompletionBroker broker;
        private readonly CtrlTapDetector detector = new CtrlTapDetector();

        public CtrlTap(IWpfTextView view, IAsyncCompletionBroker broker)
        {
            this.view = view;
            this.broker = broker;
            InputWatcher.EnsureInstalled();
            var element = view.VisualElement;
            element.PreviewKeyDown += OnKeyDown;
            element.PreviewKeyUp += OnKeyUp;
            element.PreviewMouseDown += (s, e) => detector.Interrupted();
            element.LostKeyboardFocus += (s, e) => detector.Interrupted();
        }

        private static bool IsCtrl(Key key) => key == Key.LeftCtrl || key == Key.RightCtrl;

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // The first Ctrl press arms the tap; any other key (also Shift, Alt) or the key repeat of a held Ctrl disarms it.
            if (IsCtrl(e.Key))
            {
                detector.CtrlDown(e.IsRepeat, Keyboard.Modifiers == ModifierKeys.Control, InputWatcher.Count);
            }
            else
            {
                detector.Interrupted();
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (!IsCtrl(e.Key))
            {
                detector.Interrupted();
                return;
            }

            // Chords that SSMS routes to commands (Ctrl+A, Ctrl+Space ...) never reach the key events above; the input watcher sees them.
            var tapped = detector.CtrlUp(Keyboard.Modifiers == ModifierKeys.None, InputWatcher.Count);
            if (!tapped || !SettingsService.Current.Behavior.QuickActionsOnCtrl || broker.GetSession(view) != null || SnippetSession.IsActive(view))
            {
                return;
            }

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                QuickActionsPopup.Show(view);
            }
            catch (Exception ex)
            {
                Log.Error("Opening the quick actions failed", ex);
            }
        }
    }

    /// <summary>
    /// The quick actions popup: a filter box and a list with the actions of the SQL Prompter menu (for the selection, or the script when nothing is
    /// selected) and the snippets that fit. It opens at the left edge of the editor, below the caret line.
    /// </summary>
    internal sealed class QuickActionsPopup : Window
    {
        private static QuickActionsPopup open;
        private readonly IWpfTextView view;
        private readonly TextBox filter = SnippetUi.Box(false);
        private readonly ListBox list = new ListBox { BorderThickness = new Thickness(0) };
        private readonly List<Entry> entries;
        private Func<string, List<Entry>> search;
        private int initialIndex;
        private Action chosen;
        private bool closing;
        private readonly CtrlTapDetector closeTap = new CtrlTapDetector();

        private QuickActionsPopup(IWpfTextView view, List<Entry> entries, string hint)
        {
            this.view = view;
            ThreadHelper.ThrowIfNotOnUIThread();
            this.entries = entries;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Width = 420;
            Height = 340;
            SnippetUi.Theme(this);
            SnippetUi.Theme(list);
            var root = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 6) };
            root.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            filter.Margin = new Thickness(0, 0, 0, 6);
            filter.ToolTip = hint;
            Grid.SetRow(list, 1);
            grid.Children.Add(filter);
            grid.Children.Add(list);
            root.Child = grid;
            Content = root;

            filter.TextChanged += (s, e) => Fill();
            list.MouseDoubleClick += (s, e) => Choose();
            PreviewKeyDown += OnKeyDown;
            PreviewKeyDown += OnCtrlDown;
            PreviewKeyUp += OnCtrlUp;
            PreviewMouseDown += (s, e) => closeTap.Interrupted();
            Loaded += (s, e) =>
            {
                filter.Focus();
                if (initialIndex > 0 && initialIndex < list.Items.Count && filter.Text.Length == 0)
                {
                    list.SelectedIndex = initialIndex;
                    list.ScrollIntoView(list.SelectedItem);
                }

                StartOutsideClickWatch();
            };
            Closing += (s, e) =>
            {
                closing = true;
                outsideTimer?.Stop();
            };
            Deactivated += (s, e) =>
            {
                if (!closing)
                {
                    closing = true;
                    Close();
                }
            };
            Closed += (s, e) =>
            {
                open = null;
                if (!view.IsClosed)
                {
                    view.VisualElement.Focus();
                    chosen?.Invoke();
                }
            };
            Fill();
        }

        private System.Windows.Threading.DispatcherTimer outsideTimer;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out System.Drawing.Point point);

        /// <summary>
        /// A click anywhere outside the popup closes it, also in the editor behind it: the popup is modal and owned by the SSMS window, so a click
        /// there does not always take the activation away from it. The mouse buttons are polled (a hook would see the click only after SSMS did).
        /// </summary>
        private void StartOutsideClickWatch()
        {
            var wasDown = false;
            outsideTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            outsideTimer.Tick += (s, e) =>
            {
                if (closing)
                {
                    outsideTimer.Stop();
                    return;
                }

                var down = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 || (GetAsyncKeyState(0x04) & 0x8000) != 0;
                if (down && !wasDown && GetCursorPos(out var cursor))
                {
                    var source = PresentationSource.FromVisual(this);
                    var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                    var topLeft = toDevice.Transform(new Point(Left, Top));
                    var bottomRight = toDevice.Transform(new Point(Left + ActualWidth, Top + ActualHeight));
                    if (cursor.X < topLeft.X || cursor.X > bottomRight.X || cursor.Y < topLeft.Y || cursor.Y > bottomRight.Y)
                    {
                        closing = true;
                        chosen = null;
                        outsideTimer.Stop();
                        Close();
                        return;
                    }
                }

                wasDown = down;
            };
            outsideTimer.Start();
        }

        private static bool IsCtrl(Key key) => key == Key.LeftCtrl || key == Key.RightCtrl;

        /// <summary>A tap of Ctrl on its own closes the popup again (#240); the decision is the one that opens it (<see cref="CtrlTapDetector"/>, which also counts the input the editor never sees).</summary>
        private void OnCtrlDown(object sender, KeyEventArgs e)
        {
            if (IsCtrl(e.Key))
            {
                closeTap.CtrlDown(e.IsRepeat, Keyboard.Modifiers == ModifierKeys.Control, InputWatcher.Count);
            }
            else
            {
                closeTap.Interrupted();
            }
        }

        private void OnCtrlUp(object sender, KeyEventArgs e)
        {
            if (!IsCtrl(e.Key))
            {
                closeTap.Interrupted();
                return;
            }

            if (closeTap.CtrlUp(Keyboard.Modifiers == ModifierKeys.None, InputWatcher.Count) && !closing)
            {
                // Nothing is run and nothing changes in the script; the focus goes back to the editor when the window closes.
                closing = true;
                chosen = null;
                e.Handled = true;
                Close();
            }
        }

        public static void Show(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (open != null)
            {
                return;
            }

            var hasSelection = !view.Selection.IsEmpty;
            Open(new QuickActionsPopup(view, BuildEntries(view, hasSelection), "Type to filter; Enter runs the item, Esc closes"));
        }

        /// <summary>The outline of the script (#224): batches, statements, CTEs, temporary tables and variables; Enter moves the caret to the item.</summary>
        public static void ShowOutline(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (open != null)
            {
                return;
            }

            var snapshot = view.TextSnapshot;
            var items = SqlPrompter.Core.Navigation.ScriptOutline.Build(snapshot.GetText());
            var entries = items.Select(item => new Entry(new string(' ', item.Depth * 3) + item.Text, "line " + item.Line, () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var position = new SnapshotPoint(view.TextSnapshot, Math.Min(item.Offset, view.TextSnapshot.Length));
                view.Caret.MoveTo(position);
                view.ViewScroller.EnsureSpanVisible(new SnapshotSpan(position, 0), Microsoft.VisualStudio.Text.Editor.EnsureSpanVisibleOptions.AlwaysCenter);
            })).ToList();
            if (entries.Count == 0)
            {
                return;
            }

            var outline = new QuickActionsPopup(view, entries, "Script outline: type to filter; Enter jumps to the item, Esc closes");
            outline.initialIndex = Math.Max(0, SqlPrompter.Core.Navigation.ScriptOutline.IndexAt(items, view.Caret.Position.BufferPosition.Position));
            Open(outline);
        }

        /// <summary>Quick open (#223): objects of the current database by a part of the name; Enter opens the definition, Shift+Enter inserts the name.</summary>
        public static void ShowObjects(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (open != null)
            {
                return;
            }

            var snapshot = view.TextSnapshot;
            var database = SqlBatch.FindUsedDatabase(snapshot, view.Caret.Position.BufferPosition.Position);
            var metadata = MetadataService.Current?.CacheForActiveWindow(out _, database)?.Current;
            if (metadata == null)
            {
                GoToDefinitionHandler.Tell("SQL Prompter: the objects of the database are not loaded yet (or the window is not connected).");
                return;
            }

            var popup = new QuickActionsPopup(view, new List<Entry>(), "Quick open: type a part of the name; Enter opens the definition, Shift+Enter inserts the name, Esc closes");
            popup.search = text => SqlPrompter.Core.Navigation.ObjectFinder.Search(metadata, text).Select(found =>
            {
                var name = found.FullName;
                var schema = found.Schema;
                var objectName = found.Name;
                return new Entry(found.Name, found.Kind + "  " + found.Schema,
                    () => { ThreadHelper.ThrowIfNotOnUIThread(); GoToDefinitionHandler.OpenObject(view, schema, objectName); },
                    () =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        var caret = view.Caret.Position.BufferPosition;
                        view.TextBuffer.Insert(caret.Position, name);
                    });
            }).ToList();
            popup.Fill();
            Open(popup);
        }

        /// <summary>A fixed list of (name, description, action) in the popup, for results such as those of Find in Database Code (#212).</summary>
        public static void ShowItems(IWpfTextView view, IEnumerable<Tuple<string, string, Action>> items, string hint)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (open != null)
            {
                return;
            }

            Open(new QuickActionsPopup(view, items.Select(i => new Entry(i.Item1, i.Item2, i.Item3)).ToList(), hint));
        }

        private static void Open(QuickActionsPopup popup)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ActiveEditor.SetOwner(popup);
            popup.WindowStartupLocation = WindowStartupLocation.Manual;
            popup.Place();
            open = popup;
            popup.ShowDialog();
        }

        private void Place()
        {
            var line = view.Caret.ContainingTextViewLine;
            var y = Math.Max(0, line.Bottom - view.ViewportTop + 2);
            var device = view.VisualElement.PointToScreen(new Point(0, y));
            var source = PresentationSource.FromVisual(view.VisualElement);
            var scale = source?.CompositionTarget != null ? source.CompositionTarget.TransformToDevice.M11 : 1.0;

            // The monitor that contains the caret decides, not the primary one (#451). Everything is computed in device pixels.
            var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)device.X, (int)device.Y)).WorkingArea;
            PopupPlacement.Place(device.X, device.Y, line.Height * scale, 4 * scale, Width * scale, Height * scale, area.Left, area.Top, area.Right, area.Bottom, out var left, out var top);
            var dip = source?.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice.Transform(new Point(left, top)) : new Point(left, top);
            Left = dip.X;
            Top = dip.Y;

            // Left and Top in DIPs are converted with the DPI of the monitor the window starts on, so the exact position is set in pixels once the window exists.
            SourceInitialized += (s, e) => SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, IntPtr.Zero, (int)Math.Round(left), (int)Math.Round(top), 0, 0, 0x0001 | 0x0004 | 0x0010);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private static List<Entry> BuildEntries(IWpfTextView view, bool hasSelection)
        {
            var result = new List<Entry>();

            // On a BEGIN or END the jump to the other end comes first (#224).
            var match = hasSelection ? null : SqlPrompter.Core.Navigation.BlockMatcher.Find(view.TextSnapshot.GetText(), view.Caret.Position.BufferPosition.Position);
            if (match != null)
            {
                result.Add(new Entry("Go To " + match.Name, "Moves the caret to the " + (match.TargetIsEnd ? "end" : "beginning") + " of this block (line " + match.Line + ").", () =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    var position = new SnapshotPoint(view.TextSnapshot, Math.Min(match.Target, view.TextSnapshot.Length));
                    view.Selection.Clear();
                    view.Caret.MoveTo(position);
                    view.ViewScroller.EnsureSpanVisible(new SnapshotSpan(position, 0), Microsoft.VisualStudio.Text.Editor.EnsureSpanVisibleOptions.ShowStart);
                }));
            }

            foreach (var action in QuickActionCatalog.Actions(hasSelection))
            {
                var id = action.Id;
                result.Add(new Entry(action.Name, action.Description, () => { ThreadHelper.ThrowIfNotOnUIThread(); Run(view, id, hasSelection); }));
            }

            result.Add(new Entry("Rename Query Tab...", "Gives the active query tab a name that is shown on the tab and kept after a restart.", () => { ThreadHelper.ThrowIfNotOnUIThread(); TabSessionService.Current?.RenameActiveTab(); }));

            // The tabs that were closed lately (#226), the last one first.
            var closedTabs = TabSessionService.Current?.ClosedTabsList ?? new SqlPrompter.Core.History.SavedTab[0];
            foreach (var tab in closedTabs.Take(8))
            {
                var closed = tab;
                var title = !string.IsNullOrEmpty(closed.Name) ? closed.Name : (closed.Text ?? string.Empty).Trim().Split('\n')[0].Trim();
                if (title.Length > 50)
                {
                    title = title.Substring(0, 50) + "...";
                }

                var where = closed.IsConnected ? closed.Server + (string.IsNullOrEmpty(closed.Database) ? string.Empty : ", " + closed.Database) : "not connected";
                result.Add(new Entry("Reopen closed tab: " + (title.Length > 0 ? title : "(empty)"), where, () => { ThreadHelper.ThrowIfNotOnUIThread(); TabSessionService.Current?.ReopenClosedTab(closed); }));
            }

            foreach (var snippet in QuickActionCatalog.SnippetsFor(SnippetService.Snippets, hasSelection))
            {
                var current = snippet;
                result.Add(new Entry("Snippet: " + snippet.Prefix, snippet.Description ?? string.Empty, () => { ThreadHelper.ThrowIfNotOnUIThread(); InsertSnippet(view, current, hasSelection); }));
            }

            return result;
        }

        private static void InsertSnippet(IWpfTextView view, SqlPrompter.Core.Snippets.Snippet snippet, bool hasSelection)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (hasSelection && !view.Selection.IsEmpty)
            {
                var span = view.Selection.SelectedSpans[0];
                SnippetInserter.Insert(view, span, snippet, span.GetText());
            }
            else
            {
                SnippetInserter.Insert(view, new SnapshotSpan(view.Caret.Position.BufferPosition, 0), snippet, null);
            }
        }

        private static void Run(IWpfTextView view, string id, bool hasSelection)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            switch (id)
            {
                case QuickActionCatalog.Format: RefactorCommands.FormatScript(view, !hasSelection); break;
                case QuickActionCatalog.Unformat: RefactorCommands.UnformatSelection(view); break;
                case QuickActionCatalog.QualifyNames: RefactorCommands.QualifyNames(view); break;
                case QuickActionCatalog.ExpandStars: RefactorCommands.ExpandStars(view); break;
                case QuickActionCatalog.RemoveBrackets: RefactorCommands.Run(view, Refactorings.RemoveBrackets, "square brackets removed"); break;
                case QuickActionCatalog.InsertSemicolons: RefactorCommands.Run(view, Refactorings.InsertSemicolons, "semicolons inserted"); break;
                case QuickActionCatalog.RemoveUnusedVariables: RefactorCommands.Run(view, Refactorings.RemoveUnusedVariables, "unused variables removed"); break;
                case QuickActionCatalog.PreviewAsSelect: PreviewStatement.AsSelect(view); break;
                case QuickActionCatalog.DryRun: PreviewStatement.DryRun(view); break;
                case QuickActionCatalog.FixCodeIssues: RefactorCommands.Run(view, text => CodeFixes.Apply(text, SettingsService.Current.Analysis, MetadataService.Current?.MetadataForScript(text)), "code analysis issues fixed"); break;
                case QuickActionCatalog.AddNPrefix: RefactorCommands.Run(view, AliasRefactorings.AddNPrefix, "N prefix added to strings"); break;
                case QuickActionCatalog.AddAs: RefactorCommands.Run(view, AliasRefactorings.AddAs, "AS added to aliases"); break;
                case QuickActionCatalog.RemoveAs: RefactorCommands.Run(view, AliasRefactorings.RemoveAs, "AS removed from aliases"); break;
                case QuickActionCatalog.ScriptOutline: ShowOutline(view); break;
                case QuickActionCatalog.AddCommas: LineCommands.Run(view, LineTools.AddCommas, "commas"); break;
                case QuickActionCatalog.AddQuotesAndCommas: LineCommands.Run(view, LineTools.AddQuotesAndCommas, "quotes and commas"); break;
            }
        }

        public void Fill()
        {
            list.Items.Clear();
            foreach (var entry in search != null ? search(filter.Text) : entries.Where(e => QuickActionCatalog.Matches(e.Name, e.Description, filter.Text)).ToList())
            {
                var panel = new StackPanel { Margin = new Thickness(2, 1, 2, 1) };
                panel.Children.Add(new TextBlock { Text = entry.Name });
                var description = new TextBlock { Text = entry.Description, FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
                panel.Children.Add(description);
                list.Items.Add(new ListBoxItem { Content = panel, Tag = entry });
            }

            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }

        private void Choose(bool alternate = false)
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is Entry entry)
            {
                chosen = alternate && entry.Alternate != null ? entry.Alternate : entry.Run;
                Close();
            }
        }

        private void Move(int step)
        {
            if (list.Items.Count == 0)
            {
                return;
            }

            list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + step));
            list.ScrollIntoView(list.SelectedItem);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); e.Handled = true; break;
                case Key.Enter: Choose(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; break;
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.PageDown: Move(8); e.Handled = true; break;
                case Key.PageUp: Move(-8); e.Handled = true; break;
            }
        }

        private sealed class Entry
        {
            public Entry(string name, string description, Action run, Action alternate = null)
            {
                Name = name;
                Description = description;
                Run = run;
                Alternate = alternate;
            }

            /// <summary>What Shift+Enter does, or null.</summary>
            public Action Alternate { get; }

            public string Name { get; }

            public string Description { get; }

            public Action Run { get; }
        }
    }
}
