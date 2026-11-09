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
                var documents = new List<object>();
                foreach (Window w in Application.Current.Windows)
                {
                    CollectDocumentViews(w, documents);
                }

                foreach (var document in documents)
                {
                    try
                    {
                        Apply(document, coloring.Enabled && coloring.ColorTab ? WantedBrush(EditorOf(document), coloring) : null);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Coloring a tab failed", ex);
                    }
                }

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
                brushProperty.SetValue(document, applied.OriginalBrush);
                colorizedProperty.SetValue(document, applied.OriginalColorized);
                Applied.Remove(document);
                Log.Write("Tab color removed.");
                return;
            }

            if (current is SolidColorBrush a && wanted is SolidColorBrush b && a.Color == b.Color && (bool)colorizedProperty.GetValue(document))
            {
                return;
            }

            colorizedProperty.SetValue(document, true);
            brushProperty.SetValue(document, wanted);
            Log.Write("Tab color set to " + ((SolidColorBrush)wanted).Color + ".");
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
        private static void CollectDocumentViews(DependencyObject root, List<object> found)
        {
            if (root == null)
            {
                return;
            }

            if (root.GetType().Name == "DocumentTabItem" && root is FrameworkElement tab && tab.DataContext?.GetType().Name == "DocumentView")
            {
                if (!found.Contains(tab.DataContext))
                {
                    found.Add(tab.DataContext);
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
        }
    }
}
