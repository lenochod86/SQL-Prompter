using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Coloring;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Colors the tab of the active query window by its connection. The tab's DataContext is a shell DocumentView with the
    /// public read/write properties TabAccentBrush and IsTabGroupColorized (verified in SSMS 22.11, see docs/architecture.md);
    /// SSMS types stay inside this class. The check runs on the UI thread once a second and only touches the active tab.
    /// </summary>
    internal static class TabColoring
    {
        private static readonly ConditionalWeakTable<object, AppliedColor> Applied = new ConditionalWeakTable<object, AppliedColor>();
        private static DispatcherTimer timer;

        public static void Start()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (timer != null)
            {
                return;
            }

            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => Update();
            timer.Start();
            SettingsService.Changed += (s, e) => Update();
        }

        private static void Update()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            WarmUpMetadata();
            try
            {
                var window = Application.Current?.MainWindow;
                if (window == null)
                {
                    return;
                }

                var coloring = SettingsService.Current.Coloring;

                // The status bar inside each query window is colored by the connection of that window, whichever tab is in front. The window in
                // front also decides the tab and the shell status bar, from its own connection state: an unconnected window gets no color (#317).
                var front = WindowStatusColoring.Update(new System.Windows.Interop.WindowInteropHelper(window).Handle, coloring, coloring.Enabled && coloring.ColorWindowStatusBar);

                // The tab (accent bar, tint and the frame that comes with it) of every query window follows the connection of that window, so two
                // tab groups side by side each show their own color (#393). Windows that are not query windows get nothing.
                var documents = new List<TabPair>();
                foreach (Window w in Application.Current.Windows)
                {
                    CollectDocumentViews(w, documents);
                }

                foreach (var pair in documents)
                {
                    try
                    {
                        var wanted = coloring.Enabled && coloring.ColorTab ? WantedBrush(EditorOf(pair.Document), coloring) : null;
                        Apply(pair.Document, wanted);
                        ApplyFill(pair.Tab, wanted != null && coloring.FillWholeTab ? wanted : null);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Coloring a tab failed", ex);
                    }
                }

                Prune(documents);

                // The shell status bar follows the window in front only.
                if (front.Found)
                {
                    Brush wanted = null;
                    if (coloring.Enabled && front.Color is System.Drawing.Color color)
                    {
                        wanted = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
                        wanted.Freeze();
                    }

                    ApplyStatusBar(window, coloring.ColorShellStatusBar ? wanted : null);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Coloring the tab failed", ex);
            }
        }

        /// <summary>The database of the active window may have been switched (the drop-down, USE): start loading its metadata at once, not at the first completion (#470).</summary>
        private static void WarmUpMetadata()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                MetadataService.Current?.CacheForActiveWindow(out _);
            }
            catch (Exception ex)
            {
                Log.Error("Starting the metadata load of the active window failed", ex);
            }
        }

        /// <summary>The query editor control (<c>SqlScriptEditorControl</c>) of the window frame of a <c>DocumentView</c>; null for other documents. Verified in SSMS 22.11: the DocView of the frame is that control.</summary>
        private static System.Windows.Forms.Control EditorOf(object document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var frame = document.GetType().GetProperty("WindowFrame", BindingFlags.Public | BindingFlags.Instance)?.GetValue(document) as Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame;
            if (frame == null || frame.GetProperty((int)Microsoft.VisualStudio.Shell.Interop.__VSFPROPID.VSFPROPID_DocView, out var docView) != 0)
            {
                return null;
            }

            return docView is System.Windows.Forms.Control control && control.GetType().Name == "SqlScriptEditorControl" ? control : null;
        }

        private static Brush WantedBrush(System.Windows.Forms.Control editor, SqlPrompter.Core.Settings.ColorSettings coloring)
        {
            if (editor == null || !(WindowStatusColoring.Wanted(editor, coloring) is System.Drawing.Color color))
            {
                return null;
            }

            var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// The whole tab in the color (#438): the Background and the Foreground of the <c>DocumentTabItem</c> are set as local values, which win over the
        /// triggers of the shell style (selected, hovered, active), so the tab is filled also when it is not in front, like SSMS fills the tab with the color of
        /// the Connect dialog. The text is black or white by the luminance of the color. A null brush gives the style its say again.
        /// </summary>
        private static void ApplyFill(FrameworkElement tab, Brush wanted)
        {
            if (!(tab is System.Windows.Controls.Control control))
            {
                return;
            }

            if (wanted == null)
            {
                if (Filled.TryGetValue(control, out _))
                {
                    control.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                    control.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                    Filled.Remove(control);
                }

                return;
            }

            var color = ((SolidColorBrush)wanted).Color;
            var text = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) < 140 ? Brushes.White : Brushes.Black;
            if (!(control.Background is SolidColorBrush current && current.Color == color))
            {
                control.Background = wanted;
            }

            if (!ReferenceEquals(control.Foreground, text))
            {
                control.Foreground = text;
            }

            if (!Filled.TryGetValue(control, out _))
            {
                Filled.Add(control, new object());
            }
        }

        private static readonly ConditionalWeakTable<object, object> Filled = new ConditionalWeakTable<object, object>();

        private sealed class TabPair
        {
            public FrameworkElement Tab;
            public object Document;
        }

        private static void Apply(object document, Brush wanted)
        {
            var type = document.GetType();
            var brushProperty = type.GetProperty("TabAccentBrush", BindingFlags.Public | BindingFlags.Instance);
            var colorizedProperty = type.GetProperty("IsTabGroupColorized", BindingFlags.Public | BindingFlags.Instance);
            if (brushProperty == null || colorizedProperty == null || !brushProperty.CanWrite || !colorizedProperty.CanWrite)
            {
                return;
            }

            var current = brushProperty.GetValue(document) as Brush;
            if (!Applied.TryGetValue(document, out var applied))
            {
                if (wanted == null)
                {
                    return;
                }

                applied = new AppliedColor { OriginalBrush = current, OriginalColorized = (bool)colorizedProperty.GetValue(document) };
                Applied.Add(document, applied);
            }

            if (wanted == null)
            {
                // The connection no longer maps to an environment: give the tab its own color back.
                Unhook(document, applied);
                settingOurselves = true;
                try
                {
                    brushProperty.SetValue(document, applied.OriginalBrush);
                    colorizedProperty.SetValue(document, applied.OriginalColorized);
                }
                finally
                {
                    settingOurselves = false;
                }

                Applied.Remove(document);
                Log.Write("Tab color removed.");
                return;
            }

            applied.Wanted = (SolidColorBrush)wanted;
            Hook(document, applied);
            if (TabColorGuard.IsInPlace(ArgbOf(current), (bool)colorizedProperty.GetValue(document), ArgbOf(wanted).Value))
            {
                return;
            }

            settingOurselves = true;
            try
            {
                colorizedProperty.SetValue(document, true);
                brushProperty.SetValue(document, wanted);
            }
            finally
            {
                settingOurselves = false;
            }

            Log.Write("Tab color set to " + ((SolidColorBrush)wanted).Color + ".");
        }

        private static int? ArgbOf(Brush brush) => brush is SolidColorBrush solid ? (solid.Color.A << 24) | (solid.Color.R << 16) | (solid.Color.G << 8) | solid.Color.B : (int?)null;

        /// <summary>True while we write the accent brush ourselves, so that the handler of the change does not answer our own change.</summary>
        private static bool settingOurselves;

        /// <summary>The documents whose accent brush and colorized flag we watch (strongly held, so they are released when their tab is gone, see <see cref="Prune"/>).</summary>
        private static readonly Dictionary<object, AppliedColor> Watched = new Dictionary<object, AppliedColor>();

        /// <summary>
        /// The shell sets <c>TabAccentBrush</c> and <c>IsTabGroupColorized</c> again whenever the caption of the tab changes (<c>DocumentView.UpdateTabColor</c>, e.g. to
        /// "Executing..." when a query starts, #439), which showed its own color until the next tick of the timer. A watch on both properties puts our color back in the same
        /// call, before the tab is drawn.
        /// </summary>
        private static void Hook(object document, AppliedColor applied)
        {
            if (applied.Handler != null)
            {
                return;
            }

            applied.Handler = (s, e) => Reassert(document, applied);
            var type = document.GetType();
            foreach (var name in new[] { "TabAccentBrushProperty", "IsTabGroupColorizedProperty" })
            {
                var property = type.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) as DependencyProperty;
                if (property == null)
                {
                    continue;
                }

                var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(property, type);
                descriptor.AddValueChanged(document, applied.Handler);
                applied.Descriptors.Add(descriptor);
            }

            Watched[document] = applied;
        }

        private static void Unhook(object document, AppliedColor applied)
        {
            if (applied.Handler != null)
            {
                foreach (var descriptor in applied.Descriptors)
                {
                    descriptor.RemoveValueChanged(document, applied.Handler);
                }

                applied.Descriptors.Clear();
                applied.Handler = null;
            }

            Watched.Remove(document);
        }

        /// <summary>Stops watching the documents that are no longer in the visual tree (a closed tab).</summary>
        private static void Prune(List<TabPair> present)
        {
            if (Watched.Count == 0)
            {
                return;
            }

            foreach (var document in new List<object>(Watched.Keys))
            {
                if (!present.Exists(p => ReferenceEquals(p.Document, document)))
                {
                    Unhook(document, Watched[document]);
                }
            }
        }

        private static void Reassert(object document, AppliedColor applied)
        {
            try
            {
                var type = document.GetType();
                var brushProperty = type.GetProperty("TabAccentBrush", BindingFlags.Public | BindingFlags.Instance);
                var colorizedProperty = type.GetProperty("IsTabGroupColorized", BindingFlags.Public | BindingFlags.Instance);
                if (applied.Wanted == null || brushProperty == null || colorizedProperty == null)
                {
                    return;
                }

                var colorized = (bool)colorizedProperty.GetValue(document);
                if (!TabColorGuard.MustReassert(true, settingOurselves, ArgbOf(brushProperty.GetValue(document) as Brush), colorized, ArgbOf(applied.Wanted).Value))
                {
                    return;
                }

                settingOurselves = true;
                try
                {
                    colorizedProperty.SetValue(document, true);
                    brushProperty.SetValue(document, applied.Wanted);
                }
                finally
                {
                    settingOurselves = false;
                }

                Log.Write("Tab color put back after the shell changed it.");
            }
            catch (Exception ex)
            {
                Log.Error("Putting the tab color back failed", ex);
            }
        }

        // The shell's own status bar (the bar at the bottom of the main window; off by default since #314). The per-window bar with the connection
        // state is a separate control, see WindowStatusColoring.
        private static WeakReference statusBar;
        private static Brush statusBarOriginal;
        private static bool statusBarColored;

        private static void ApplyStatusBar(DependencyObject window, Brush wanted)
        {
            var panel = (statusBar?.Target as System.Windows.Controls.Panel) ?? FindStatusBar(window);
            if (panel == null)
            {
                return;
            }

            if (statusBar == null || !ReferenceEquals(statusBar.Target, panel))
            {
                statusBar = new WeakReference(panel);
                statusBarColored = false;
            }

            if (wanted == null)
            {
                if (statusBarColored)
                {
                    panel.Background = statusBarOriginal;
                    statusBarColored = false;
                }

                return;
            }

            if (!statusBarColored)
            {
                statusBarOriginal = panel.Background;
                statusBarColored = true;
            }

            if (!Equals(panel.Background, wanted))
            {
                panel.Background = wanted;
            }
        }

        private static System.Windows.Controls.Panel FindStatusBar(DependencyObject root)
        {
            if (root is System.Windows.Controls.DockPanel panel && panel.Name == "StatusBarPanel")
            {
                return panel;
            }

            var count = root is Visual || root is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
            for (var i = 0; i < count; i++)
            {
                var found = FindStatusBar(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>The <c>DocumentView</c> of every tab in the visual tree (every tab group, and floating windows when their window is passed), not only the active one.</summary>
        private static void CollectDocumentViews(DependencyObject root, List<TabPair> found)
        {
            if (root == null)
            {
                return;
            }

            if (root.GetType().Name == "DocumentTabItem" && root is FrameworkElement tab && tab.DataContext?.GetType().Name == "DocumentView")
            {
                if (!found.Exists(p => ReferenceEquals(p.Document, tab.DataContext)))
                {
                    found.Add(new TabPair { Tab = tab, Document = tab.DataContext });
                }

                return;
            }

            var count = root is Visual || root is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
            for (var i = 0; i < count; i++)
            {
                CollectDocumentViews(VisualTreeHelper.GetChild(root, i), found);
            }
        }

        private sealed class AppliedColor
        {
            public Brush OriginalBrush;
            public bool OriginalColorized;
            public SolidColorBrush Wanted;
            public EventHandler Handler;
            public readonly List<System.ComponentModel.DependencyPropertyDescriptor> Descriptors = new List<System.ComponentModel.DependencyPropertyDescriptor>();
        }
    }
}
