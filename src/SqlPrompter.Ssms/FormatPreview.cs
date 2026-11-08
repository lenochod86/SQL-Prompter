using System;
using System.Drawing;
using System.Windows.Forms;
using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Ssms
{
    /// <summary>A read-only box that shows SQL colored (keywords, strings, comments, variables), for the formatting preview of the settings window.</summary>
    internal sealed class FormatPreview : RichTextBox
    {
        public FormatPreview()
        {
            ReadOnly = true;
            WordWrap = false;
            DetectUrls = false;
            Font = new Font("Consolas", 9.5f);
            ScrollBars = RichTextBoxScrollBars.Both;
            BackColor = SystemColors.Window;
            BorderStyle = BorderStyle.FixedSingle;
            // The colors of the words follow the editor of SSMS; after a change of the theme (the window colors change) the text is colored again.
            BackColorChanged += (s, e) =>
            {
                if (last != null)
                {
                    ShowText(last);
                }
            };
        }

        private string last;

        public void ShowText(string text)
        {
            text = text ?? string.Empty;
            last = text;
            var kinds = SnippetHighlighter.Classify(text);
            var colors = Array.ConvertAll(EditorColors.ByKind(), c => Color.FromArgb(c.R, c.G, c.B));
            SuspendLayout();
            var firstVisible = GetCharIndexFromPosition(new Point(0, 0));
            Text = text.Replace("\r\n", "\n");
            if (kinds.Length >= text.Length)
            {
                // The box keeps "\n", so the positions of the classified text (which may hold "\r\n") are mapped by skipping the "\r".
                var position = 0;
                var i = 0;
                while (i < text.Length)
                {
                    if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                        continue;
                    }

                    var kind = Math.Max(0, Math.Min(colors.Length - 1, kinds[i]));
                    var j = i;
                    var length = 0;
                    while (j < text.Length && Math.Max(0, Math.Min(colors.Length - 1, kinds[j])) == kind)
                    {
                        if (!(text[j] == '\r' && j + 1 < text.Length && text[j + 1] == '\n'))
                        {
                            length++;
                        }

                        j++;
                    }

                    Select(position, length);
                    SelectionColor = colors[kind];
                    position += length;
                    i = j;
                }
            }

            Select(0, 0);
            ResumeLayout();
            if (firstVisible > 0 && firstVisible < TextLength)
            {
                Select(firstVisible, 0);
                ScrollToCaret();
                Select(0, 0);
            }
        }
    }
}
