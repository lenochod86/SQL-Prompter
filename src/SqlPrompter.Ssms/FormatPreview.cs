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
        }

        public void ShowText(string text)
        {
            text = text ?? string.Empty;
            var kinds = SnippetHighlighter.Classify(text);
            var dark = (BackColor.R * 299 + BackColor.G * 587 + BackColor.B * 114) / 1000 < 128;
            var colors = new[]
            {
                dark ? Color.White : Color.Black,
                dark ? Color.FromArgb(0x56, 0x9C, 0xD6) : Color.FromArgb(0x00, 0x00, 0xFF),
                dark ? Color.FromArgb(0xD6, 0x9D, 0x85) : Color.FromArgb(0xA3, 0x15, 0x15),
                dark ? Color.FromArgb(0x57, 0xA6, 0x4A) : Color.FromArgb(0x00, 0x80, 0x00),
                dark ? Color.FromArgb(0x9C, 0xDC, 0xFE) : Color.FromArgb(0x1F, 0x37, 0x7F),
                dark ? Color.FromArgb(0xC5, 0x86, 0xC0) : Color.FromArgb(0xAF, 0x00, 0xDB),
            };
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
