using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Navigation;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The Save As dialog of a query tab that has a name (the name of <c>Rename Query Tab</c>, the name of a definition tab, #437) proposes that name instead of the temporary
    /// <c>SQLQuery3.sql</c>. The document of such a tab is a temporary file and the shell takes the proposal from its name; the caption is only a property of the frame, so the
    /// file name field of the dialog is set when the dialog opens (a window event hook of this process; the field is changed only while it still holds the temporary name).
    /// </summary>
    internal sealed class SaveAsNamer
    {
        private const uint EventSystemDialogStart = 0x0010;
        private const uint WineventOutOfContext = 0;
        private const uint WmSetText = 0x000C;

        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        private readonly Func<string> activeDocument;
        private readonly Func<string, string> nameOf;
        private readonly WinEventProc callback;
        private IntPtr hook;

        private SaveAsNamer(Func<string> activeDocument, Func<string, string> nameOf)
        {
            this.activeDocument = activeDocument;
            this.nameOf = nameOf;
            callback = OnEvent;
        }

        /// <summary>Starts watching the dialogs of this process; the hook lives as long as SSMS.</summary>
        public static SaveAsNamer Start(Func<string> activeDocument, Func<string, string> nameOf)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var namer = new SaveAsNamer(activeDocument, nameOf);
            namer.hook = SetWinEventHook(EventSystemDialogStart, EventSystemDialogStart, IntPtr.Zero, namer.callback, (uint)System.Diagnostics.Process.GetCurrentProcess().Id, 0, WineventOutOfContext);
            if (namer.hook == IntPtr.Zero)
            {
                Log.Write("Save As name: the window event hook could not be set.");
            }

            return namer;
        }

        private void OnEvent(IntPtr eventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (hwnd == IntPtr.Zero || Title(hwnd) != "Save File As")
                {
                    return;
                }

                // The controls of the dialog are created after the event: try a few times.
                var tries = 0;
                var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
                timer.Tick += (s, e) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    tries++;
                    if (TrySet(hwnd) || tries >= 10)
                    {
                        timer.Stop();
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Save As name failed", ex);
            }
        }

        private bool TrySet(IntPtr dialog)
        {
            var document = activeDocument();
            var name = document == null ? null : nameOf(document);
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            var proposed = DefinitionTabName.FileNameOf(name);
            var temporary = System.IO.Path.GetFileName(document);
            IntPtr edit = IntPtr.Zero;
            EnumChildWindows(dialog, (child, p) =>
            {
                if (ClassName(child) == "Edit" && ClassName(GetParent(child)) == "ComboBox")
                {
                    edit = child;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            if (edit == IntPtr.Zero)
            {
                return false;
            }

            if (proposed.Length > 0 && string.Equals(Title(edit), temporary, StringComparison.OrdinalIgnoreCase))
            {
                SendMessage(edit, WmSetText, IntPtr.Zero, proposed);
                Log.Write("Save As proposes " + proposed + " instead of " + temporary + ".");
            }

            return true;
        }

        private static string Title(IntPtr window)
        {
            var text = new StringBuilder(512);
            GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        private static string ClassName(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return string.Empty;
            }

            var text = new StringBuilder(128);
            GetClassName(window, text, text.Capacity);
            return text.ToString();
        }

        private delegate bool EnumChildProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, string lParam);
    }
}
