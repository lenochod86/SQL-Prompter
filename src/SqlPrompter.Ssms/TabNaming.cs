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
