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

        /// <summary>Colors the strips by the rules, or gives them their own color back when <paramref name="enabled"/> is false or no rule matches.</summary>
        public static void Update(IntPtr mainWindow, ColorSettings coloring, bool enabled)
        {
            var strips = new List<StatusStrip>();
            EnumChildWindows(mainWindow, (handle, lParam) =>
            {
                if (Control.FromHandle(handle) is StatusStrip strip && strip.Parent != null && strip.Parent.GetType().Name == EditorControlType)
                {
                    strips.Add(strip);
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
        }

        private static Color? Wanted(Control editor, ColorSettings coloring)
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
                    strip.BackColor = original.Back;
                    strip.ForeColor = original.Fore;
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
            if (strip.BackColor != color)
            {
                strip.BackColor = color;
                strip.ForeColor = Luminance(color) < 140 ? Color.White : Color.Black;
                Log.Write("Status bar of a query window set to " + color.Name + ".");
            }
        }

        private static double Luminance(Color color) => 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;

        private sealed class Original
        {
            public Color Back;
            public Color Fore;
            public Color Applied;
        }
    }
}
