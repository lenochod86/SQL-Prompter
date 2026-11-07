using System;
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

                // The status bar inside each query window is colored by the connection of that window, whichever tab is in front.
                WindowStatusColoring.Update(new System.Windows.Interop.WindowInteropHelper(window).Handle, coloring, coloring.Enabled && coloring.ColorWindowStatusBar);

                // The tab and the shell status bar follow the active window.
                var view = ActiveEditor.GetView();
                var connection = view != null && view.TextBuffer.ContentType.IsOfType("SQL") ? SsmsConnection.GetActive() : null;
                var document = connection == null ? null : FindActiveDocumentView(window);
                if (document == null)
                {
                    return;
                }

                var environment = EnvironmentMatcher.Find(coloring, connection.Server, connection.Database);
                Brush wanted = null;
                if (environment != null && EnvironmentMatcher.TryParseColor(environment.Color, out var red, out var green, out var blue))
                {
                    wanted = new SolidColorBrush(Color.FromRgb(red, green, blue));
                    wanted.Freeze();
                }

                Apply(document, coloring.ColorTab ? wanted : null);
                ApplyStatusBar(window, coloring.ColorShellStatusBar ? wanted : null);
            }
            catch (Exception ex)
            {
                Log.Error("Coloring the tab failed", ex);
            }
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

        private static object FindActiveDocumentView(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root.GetType().Name == "DocumentTabItem" && root is FrameworkElement tab && tab.DataContext?.GetType().Name == "DocumentView")
            {
                var isActive = tab.GetType().GetProperty("IsActive")?.GetValue(tab) as bool?;
                return isActive == true ? tab.DataContext : null;
            }

            var count = root is Visual || root is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
            for (var i = 0; i < count; i++)
            {
                var found = FindActiveDocumentView(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private sealed class AppliedColor
        {
            public Brush OriginalBrush;
            public bool OriginalColorized;
        }
    }
}
