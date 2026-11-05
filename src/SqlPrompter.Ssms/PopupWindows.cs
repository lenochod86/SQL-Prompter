using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// A WPF <see cref="Popup"/> lives in its own top-level window that is topmost, so it stays above every application. This makes it behave
    /// like the built-in editor popups: the window is owned by the window of the editor (it stays above that one only, and hides with it) and is
    /// not topmost.
    /// </summary>
    internal static class PopupWindows
    {
        private const int GwlpHwndParent = -8;
        private const uint GaRoot = 2;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private static readonly IntPtr NoTopmost = new IntPtr(-2);

        /// <summary>Makes the popup window owned by the window of <paramref name="owner"/> and not topmost each time it opens.</summary>
        public static void Attach(Popup popup, Func<FrameworkElement> owner)
        {
            popup.Opened += (s, e) =>
            {
                try
                {
                    if (!(PresentationSource.FromVisual(popup.Child) is HwndSource source))
                    {
                        return;
                    }

                    var element = owner();
                    var ownerSource = element == null ? null : PresentationSource.FromVisual(element) as HwndSource;
                    if (ownerSource != null)
                    {
                        var root = GetAncestor(ownerSource.Handle, GaRoot);
                        SetWindowLongPtr(source.Handle, GwlpHwndParent, root == IntPtr.Zero ? ownerSource.Handle : root);
                    }

                    SetWindowPos(source.Handle, NoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                }
                catch (Exception ex)
                {
                    Log.Error("Making a popup not topmost failed", ex);
                }
            };
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        private static void SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(hwnd, index, value);
            }
            else
            {
                SetWindowLong32(hwnd, index, value.ToInt32());
            }
        }
    }
}
