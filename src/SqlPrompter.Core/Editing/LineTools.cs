using System.Collections.Generic;
using System.Text;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// Selection actions that work on whole lines: add commas, add quotes and commas. Blank lines stay as they are, the indentation is kept
    /// and the line breaks are not changed. The last line with text gets no comma, so the result is a list that can be pasted into
    /// <c>IN (…)</c> or a column list.
    /// </summary>
    public static class LineTools
    {
        /// <summary>A comma at the end of every line with text except the last one; a line that already ends with a comma is left alone.</summary>
        public static string AddCommas(string text) => Transform(text, (content, isLast) =>
            isLast || content.EndsWith(",") ? content : content + ",");

        /// <summary>
        /// Every line with text becomes a string literal (<c>'</c> doubled inside) followed by a comma, except the last one. A comma that was
        /// already at the end of the line is not part of the string.
        /// </summary>
        public static string AddQuotesAndCommas(string text) => Transform(text, (content, isLast) =>
        {
            if (content.EndsWith(","))
            {
                content = content.Substring(0, content.Length - 1).TrimEnd();
            }

            return "'" + content.Replace("'", "''") + "'" + (isLast ? string.Empty : ",");
        });

        private static string Transform(string text, System.Func<string, bool, string> change)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var lines = SplitKeepingBreaks(text);
            var last = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Content.Trim().Length > 0)
                {
                    last = i;
                }
            }

            var result = new StringBuilder(text.Length + lines.Count * 3);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var trimmed = line.Content.Trim();
                if (trimmed.Length == 0)
                {
                    result.Append(line.Content).Append(line.Break);
                    continue;
                }

                var indent = line.Content.Substring(0, line.Content.Length - line.Content.TrimStart().Length);
                result.Append(indent).Append(change(trimmed, i == last)).Append(line.Break);
            }

            return result.ToString();
        }

        private static List<Line> SplitKeepingBreaks(string text)
        {
            var lines = new List<Line>();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '\r' && c != '\n')
                {
                    continue;
                }

                var breakLength = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                lines.Add(new Line(text.Substring(start, i - start), text.Substring(i, breakLength)));
                i += breakLength - 1;
                start = i + 1;
            }

            if (start < text.Length)
            {
                lines.Add(new Line(text.Substring(start), string.Empty));
            }

            return lines;
        }

        private sealed class Line
        {
            public Line(string content, string lineBreak)
            {
                Content = content;
                Break = lineBreak;
            }

            public string Content { get; }

            public string Break { get; }
        }
    }
}
