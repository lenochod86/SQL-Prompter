using System;
using System.Windows;
using System.Windows.Controls;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// A definition pinned away from the completion list: the script of an object in a small window that stays until it is closed. The text is
    /// colored as SQL and can be selected; the Copy button copies the whole script.
    /// </summary>
    internal sealed class DefinitionWindow : Window
    {
        private readonly string script;

        private DefinitionWindow(string title, string script)
        {
            this.script = script;
            Title = title;
            Width = 720;
            Height = 520;
            ShowInTaskbar = false;
            SnippetUi.Theme(this);

            var editor = new SnippetBodyEditor { IsReadOnly = true, Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1) };
            SnippetUi.Theme(editor);
            editor.PlainText = script;
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = SnippetUi.Label(title);
            header.FontSize = 14;
            header.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(editor, 1);
            root.Children.Add(header);
            root.Children.Add(editor);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(SnippetUi.Button("Copy", Copy));
            buttons.Children.Add(SnippetUi.Button("Close", Close));
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
            Content = root;
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
        }

        public static void Open(string title, string script)
        {
            var window = new DefinitionWindow(title, script);
            ActiveEditor.SetOwner(window);
            window.Show();
        }

        private void Copy()
        {
            try
            {
                Clipboard.SetText(script);
            }
            catch (Exception ex)
            {
                Log.Error("Copying the definition failed", ex);
            }
        }
    }
}
