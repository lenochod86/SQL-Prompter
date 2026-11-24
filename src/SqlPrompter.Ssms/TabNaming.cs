using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SqlPrompter.Ssms
{
    /// <summary>Sets the caption of a query window (#226): the document is backed by a temp file (SQLQuery1.sql), so the caption is a property of the window frame.</summary>
    internal static class TabCaption
    {
        /// <summary>
        /// The frame property that says the tab is pinned (found by comparing the properties of a pinned and a loose tab in SSMS 22: <c>-5021</c> is a
        /// Boolean that is true for a pinned tab; there is no name for it in the SDK).
        /// </summary>
        private const int IsPinnedProperty = -5021;

        /// <summary>Whether the window of the document is a pinned tab (#pinned restore).</summary>
        public static bool IsPinned(string documentPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return VsShellUtilities.IsDocumentOpen(ServiceProvider.GlobalProvider, documentPath, Guid.Empty, out _, out _, out var frame) && frame != null &&
                    frame.GetProperty(IsPinnedProperty, out var value) == VSConstants.S_OK && value is bool pinned && pinned;
            }
            catch (Exception ex)
            {
                Log.Error("Reading whether the tab is pinned failed", ex);
                return false;
            }
        }

        /// <summary>Pins the tab of the document (it stays where it is and is not replaced). Returns false when SSMS did not do it.</summary>
        public static bool Pin(string documentPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!VsShellUtilities.IsDocumentOpen(ServiceProvider.GlobalProvider, documentPath, Guid.Empty, out _, out _, out var frame) || frame == null)
                {
                    return false;
                }

                if (frame.SetProperty(IsPinnedProperty, true) != VSConstants.S_OK)
                {
                    Log.Write("Pinning the tab: SetProperty did not work, the command Window.PinTab is used.");
                    (Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE)?.ExecuteCommand("Window.PinTab");
                }

                return IsPinned(documentPath);
            }
            catch (Exception ex)
            {
                Log.Error("Pinning the tab failed", ex);
                return false;
            }
        }

        /// <summary>
        /// The name of the tab for the history (#511): the file name of the document, or, when SSMS backs the unsaved window with a temporary file (<c>~vsr5khfdjz.sql</c>),
        /// the name in the caption of the window (<c>SQLQuery2.sql</c>, or the name the user gave it). The temporary name when the caption has none.
        /// </summary>
        public static string DisplayName(string documentPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string fileName;
            try
            {
                fileName = System.IO.Path.GetFileName(documentPath);
            }
            catch (ArgumentException)
            {
                return documentPath;
            }

            if (!SqlPrompter.Core.History.HistoryTabName.IsTemporary(fileName))
            {
                return fileName;
            }

            try
            {
                if (VsShellUtilities.IsDocumentOpen(ServiceProvider.GlobalProvider, documentPath, Guid.Empty, out _, out _, out var frame) && frame != null &&
                    frame.GetProperty((int)__VSFPROPID.VSFPROPID_Caption, out var caption) == VSConstants.S_OK &&
                    frame.GetProperty((int)__VSFPROPID.VSFPROPID_EditorCaption, out var editorCaption) == VSConstants.S_OK)
                {
                    return SqlPrompter.Core.History.HistoryTabName.FromCaption(caption as string, editorCaption as string) ?? fileName;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reading the caption of the tab failed", ex);
            }

            return fileName;
        }

        /// <summary>Whether the window of the document has the caption <paramref name="name"/> set by <see cref="Apply"/>; false when the frame is not found or has another name (SSMS gives its own back when a query runs, #515).</summary>
        public static bool HasName(string documentPath, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return VsShellUtilities.IsDocumentOpen(ServiceProvider.GlobalProvider, documentPath, Guid.Empty, out _, out _, out var frame) && frame != null &&
                    frame.GetProperty((int)__VSFPROPID.VSFPROPID_OwnerCaption, out var value) == VSConstants.S_OK && string.Equals(value as string, name, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Gives the window of the document the caption <paramref name="name"/>; an empty name gives the caption back to SSMS. Returns false when the frame was not found.</summary>
        public static bool Apply(string documentPath, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!VsShellUtilities.IsDocumentOpen(ServiceProvider.GlobalProvider, documentPath, Guid.Empty, out _, out _, out var frame) || frame == null)
                {
                    return false;
                }

                var value = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
                var result = frame.SetProperty((int)__VSFPROPID.VSFPROPID_OwnerCaption, value);
                if (result != VSConstants.S_OK)
                {
                    Log.Write("Naming the tab: setting the caption returned 0x" + result.ToString("X"));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Naming the tab failed", ex);
                return false;
            }
        }
    }

    /// <summary>A small window with one line of text to edit: the name of a query tab.</summary>
    internal sealed class TextPromptWindow : Window
    {
        private readonly TextBox box = SnippetUi.Box(false);

        public TextPromptWindow(string title, string label, string initial)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            SnippetUi.Theme(this);
            box.Text = initial ?? string.Empty;
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(SnippetUi.Label(label));
            panel.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(SnippetUi.Button("OK", Accept));
            buttons.Children.Add(SnippetUi.Button("Cancel", () => DialogResult = false));
            panel.Children.Add(buttons);
            Content = panel;
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Accept();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    e.Handled = true;
                }
            };
            Loaded += (s, e) =>
            {
                box.Focus();
                box.SelectAll();
            };
        }

        public string Value => box.Text.Trim();

        private void Accept() => DialogResult = true;
    }
}
