using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;
using SqlPrompter.Core.Coloring;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Colors the status strip inside every query window (the yellow one with "Connected.", the server, the login and the database), each by the
    /// connection of its own window, not only the active one. The strip is a WinForms <c>StatusStrip</c> in the <c>SqlScriptEditorControl</c> of the
    /// window, hosted as a child window; it is found by walking the child windows of the main window and its owner gives the connection
    /// (<c>Connection</c>, <c>IsConnected</c>), see docs/architecture.md. SSMS types are only reached through reflection here.
    /// </summary>
    internal static class WindowStatusColoring
    {
        private const string EditorControlType = "SqlScriptEditorControl";
        private static readonly ConditionalWeakTable<StatusStrip, Original> Applied = new ConditionalWeakTable<StatusStrip, Original>();

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        internal static object FrontEditor(IntPtr mainWindow)
        {
            object editor = null;
            EnumChildWindows(mainWindow, (handle, lParam) =>
            {
                if (editor == null && Control.FromHandle(handle) is StatusStrip strip && strip.Parent != null && strip.Parent.GetType().Name == EditorControlType && IsWindowVisible(handle))
                {
                    editor = strip.Parent;
                }

                return true;
            }, IntPtr.Zero);
            return editor;
        }

        /// <summary>The connection state of the query window that is shown: <c>IsConnected</c> and the <c>Connection</c> (UIConnectionInfo) of its editor control; false when no query window is shown.</summary>
        public static bool TryGetFront(IntPtr mainWindow, out bool connected, out object info)
        {
            var found = false;
            var isConnected = false;
            object connection = null;
            EnumChildWindows(mainWindow, (handle, lParam) =>
            {
                if (!found && Control.FromHandle(handle) is StatusStrip strip && strip.Parent != null && strip.Parent.GetType().Name == EditorControlType && IsWindowVisible(handle))
                {
                    var type = strip.Parent.GetType();
                    found = true;
                    isConnected = type.GetProperty("IsConnected")?.GetValue(strip.Parent) is bool value && value;
                    connection = isConnected ? type.GetProperty("Connection")?.GetValue(strip.Parent) : null;
                }

                return true;
            }, IntPtr.Zero);
            connected = isConnected;
            info = connection;
            return found;
        }

        /// <summary>
        /// Colors the strips by the rules, or gives them their own color back when <paramref name="enabled"/> is false or no rule matches.
        /// Returns what the window in front wants (<c>Found</c> false when no query window is shown): the tab and the shell status bar follow it,
        /// decided from the connection state of that window itself (#317), never from the connection that was last active.
        /// </summary>
        public static Front Update(IntPtr mainWindow, ColorSettings coloring, bool enabled)
        {
            var front = new Front();
            var strips = new List<StatusStrip>();
            EnumChildWindows(mainWindow, (handle, lParam) =>
            {
                if (Control.FromHandle(handle) is StatusStrip strip && strip.Parent != null && strip.Parent.GetType().Name == EditorControlType)
                {
                    strips.Add(strip);
                    if (!front.Found && IsWindowVisible(strip.Handle))
                    {
                        front.Found = true;
                        front.Color = Wanted(strip.Parent, coloring);
                    }
                }

                return true;
            }, IntPtr.Zero);

            foreach (var strip in strips)
            {
                try
                {
                    Apply(strip, enabled ? Wanted(strip.Parent, coloring) : null);
                }
                catch (Exception ex)
                {
                    Log.Error("Coloring the status bar of a query window failed", ex);
                }
            }

            return front;
        }

        /// <summary>The environment color wanted for the query window in front.</summary>
        internal sealed class Front
        {
            public bool Found;

            /// <summary>Null when the window has no connection or no rule matches it.</summary>
            public Color? Color;
        }

        internal static Color? Wanted(Control editor, ColorSettings coloring)
        {
            var type = editor.GetType();
            if (!(type.GetProperty("IsConnected")?.GetValue(editor) is bool connected) || !connected)
            {
                return null;
            }

            if (!SsmsConnection.TryReadServerAndDatabase(type.GetProperty("Connection")?.GetValue(editor), out var server, out var database))
            {
                return null;
            }

            var environment = EnvironmentMatcher.Find(coloring, server, database);
            if (environment != null && EnvironmentMatcher.TryParseColor(environment.Color, out var red, out var green, out var blue))
            {
                return Color.FromArgb(red, green, blue);
            }

            return null;
        }

        private static void Apply(StatusStrip strip, Color? wanted)
        {
            var known = Applied.TryGetValue(strip, out var original);
            if (known && strip.BackColor != original.Applied)
            {
                // SSMS painted the strip itself since (a new connection, its own connection color): that is the color to give back.
                original.Back = strip.BackColor;
                original.Fore = strip.ForeColor;
            }

            if (wanted == null)
            {
                if (known)
                {
                    Unhook(strip, original);
                    settingOurselves = true;
                    try
                    {
                        strip.BackColor = original.Back;
                        strip.ForeColor = original.Fore;
                    }
                    finally
                    {
                        settingOurselves = false;
                    }

                    Applied.Remove(strip);
                    Log.Write("Status bar color of a query window removed.");
                }

                return;
            }

            if (!known)
            {
                original = new Original { Back = strip.BackColor, Fore = strip.ForeColor };
                Applied.Add(strip, original);
            }

            var color = wanted.Value;
            original.Applied = color;
            Hook(strip, original);
            if (strip.BackColor != color)
            {
                Set(strip, color);
                Log.Write("Status bar of a query window set to " + color.Name + ".");
            }
        }

        private static bool settingOurselves;

        private static void Set(StatusStrip strip, Color color)
        {
            settingOurselves = true;
            try
            {
                strip.BackColor = color;
                strip.ForeColor = Luminance(color) < 140 ? Color.White : Color.Black;
            }
            finally
            {
                settingOurselves = false;
            }
        }

        /// <summary>
        /// SSMS paints the strip itself when a query starts and ends (the state text and color of the run, #476), which showed its color until the next tick of the timer.
        /// A watch on the colors puts ours back in the same call, before the strip is drawn; our own writes are not answered.
        /// </summary>
        private static void Hook(StatusStrip strip, Original original)
        {
            if (original.Handler != null)
            {
                return;
            }

            original.Handler = (s, e) =>
            {
                if (settingOurselves || strip.BackColor == original.Applied)
                {
                    return;
                }

                original.Back = strip.BackColor;
                original.Fore = strip.ForeColor;
                Set(strip, original.Applied);
            };
            strip.BackColorChanged += original.Handler;
        }

        private static void Unhook(StatusStrip strip, Original original)
        {
            if (original.Handler != null)
            {
                strip.BackColorChanged -= original.Handler;
                original.Handler = null;
            }
        }

        private static double Luminance(Color color) => 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;

        private sealed class Original
        {
            public Color Back;
            public Color Fore;
            public Color Applied;
            public EventHandler Handler;
        }
    }
}
