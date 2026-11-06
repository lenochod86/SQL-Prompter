using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Counts the keys (other than Ctrl), mouse buttons and wheel turns the UI thread receives, with thread hooks. Keys that SSMS routes to
    /// commands (Ctrl+A, Ctrl+Space …) never reach the key events of the editor, so a tap of Ctrl cannot rely on those events alone (#198).
    /// </summary>
    internal static class InputWatcher
    {
        private const int WhKeyboard = 2;
        private const int WhMouse = 7;
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        // Kept in fields so that the delegates are not collected while the hooks are installed.
        private static HookProc keyboardProc;
        private static HookProc mouseProc;
        private static long count;
        private static bool installed;

        /// <summary>Grows with every key (except Ctrl) pressed and every mouse button or wheel input on the UI thread.</summary>
        public static long Count => Interlocked.Read(ref count);

        /// <summary>Installs the hooks on the calling (UI) thread, once. Without them <see cref="Count"/> stays 0 and the key events of the editor decide alone.</summary>
        public static void EnsureInstalled()
        {
            if (installed)
            {
                return;
            }

            installed = true;
            try
            {
                var thread = GetCurrentThreadId();
                keyboardProc = OnKeyboard;
                mouseProc = OnMouse;
                SetWindowsHookEx(WhKeyboard, keyboardProc, IntPtr.Zero, thread);
                SetWindowsHookEx(WhMouse, mouseProc, IntPtr.Zero, thread);
            }
            catch (Exception ex)
            {
                Log.Error("Installing the input hooks failed", ex);
            }
        }

        private static IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var key = wParam.ToInt64();
                var released = (lParam.ToInt64() & (1L << 31)) != 0;
                // 0x11 Ctrl, 0xA2 left Ctrl, 0xA3 right Ctrl.
                if (!released && key != 0x11 && key != 0xA2 && key != 0xA3)
                {
                    Interlocked.Increment(ref count);
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private static IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                switch ((int)wParam.ToInt64())
                {
                    case 0x201: // WM_LBUTTONDOWN
                    case 0x204: // WM_RBUTTONDOWN
                    case 0x207: // WM_MBUTTONDOWN
                    case 0x20A: // WM_MOUSEWHEEL
                    case 0x20B: // WM_XBUTTONDOWN
                    case 0x20E: // WM_MOUSEHWHEEL
                    case 0xA1:  // WM_NCLBUTTONDOWN
                    case 0xA4:  // WM_NCRBUTTONDOWN
                        Interlocked.Increment(ref count);
                        break;
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookId, HookProc proc, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
