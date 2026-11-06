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
        private Action chosen;
        private bool closing;
        private readonly CtrlTapDetector closeTap = new CtrlTapDetector();

        private QuickActionsPopup(IWpfTextView view, bool hasSelection)
        {
            this.view = view;
            ThreadHelper.ThrowIfNotOnUIThread();
            entries = BuildEntries(view, hasSelection);
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
            filter.ToolTip = "Type to filter; Enter runs the item, Esc closes";
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
            Loaded += (s, e) => filter.Focus();
            Closing += (s, e) => closing = true;
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

            var popup = new QuickActionsPopup(view, !view.Selection.IsEmpty);
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
            var dip = source?.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice.Transform(device) : device;
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(dip.X, area.Right - Width));
            Top = dip.Y + Height > area.Bottom ? Math.Max(area.Top, dip.Y - Height - line.Height - 4) : dip.Y;
        }

        private static List<Entry> BuildEntries(IWpfTextView view, bool hasSelection)
        {
            var result = new List<Entry>();
            foreach (var action in QuickActionCatalog.Actions(hasSelection))
            {
                var id = action.Id;
                result.Add(new Entry(action.Name, action.Description, () => { ThreadHelper.ThrowIfNotOnUIThread(); Run(view, id, hasSelection); }));
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
                case QuickActionCatalog.QualifyNames: RefactorCommands.QualifyNames(view); break;
                case QuickActionCatalog.ExpandStars: RefactorCommands.ExpandStars(view); break;
                case QuickActionCatalog.RemoveBrackets: RefactorCommands.Run(view, Refactorings.RemoveBrackets, "square brackets removed"); break;
                case QuickActionCatalog.InsertSemicolons: RefactorCommands.Run(view, Refactorings.InsertSemicolons, "semicolons inserted"); break;
                case QuickActionCatalog.RemoveUnusedVariables: RefactorCommands.Run(view, Refactorings.RemoveUnusedVariables, "unused variables removed"); break;
                case QuickActionCatalog.FixCodeIssues: RefactorCommands.Run(view, text => CodeFixes.Apply(text, SettingsService.Current.Analysis), "code analysis issues fixed"); break;
                case QuickActionCatalog.AddNPrefix: RefactorCommands.Run(view, AliasRefactorings.AddNPrefix, "N prefix added to strings"); break;
                case QuickActionCatalog.AddAs: RefactorCommands.Run(view, AliasRefactorings.AddAs, "AS added to aliases"); break;
                case QuickActionCatalog.RemoveAs: RefactorCommands.Run(view, AliasRefactorings.RemoveAs, "AS removed from aliases"); break;
                case QuickActionCatalog.AddCommas: LineCommands.Run(view, LineTools.AddCommas, "commas"); break;
                case QuickActionCatalog.AddQuotesAndCommas: LineCommands.Run(view, LineTools.AddQuotesAndCommas, "quotes and commas"); break;
            }
        }

        private void Fill()
        {
            list.Items.Clear();
            foreach (var entry in entries.Where(e => QuickActionCatalog.Matches(e.Name, e.Description, filter.Text)))
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

        private void Choose()
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is Entry entry)
            {
                chosen = entry.Run;
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
                case Key.Enter: Choose(); e.Handled = true; break;
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.PageDown: Move(8); e.Handled = true; break;
                case Key.PageUp: Move(-8); e.Handled = true; break;
            }
        }

        private sealed class Entry
        {
            public Entry(string name, string description, Action run)
            {
                Name = name;
                Description = description;
                Run = run;
            }

            public string Name { get; }

            public string Description { get; }

            public Action Run { get; }
        }
    }
}
