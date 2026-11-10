using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// Puts SQL on one line: line breaks and runs of white space between tokens become one space, strings and quoted identifiers are not
    /// touched, a <c>--</c> comment becomes a block comment (it would swallow the rest of the line) and a <c>GO</c> line stays on its own
    /// line, so every batch is put on one line separately.
    /// </summary>
    public static class Unformatter
    {
        public static string ToOneLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var newLine = text.Contains("\r\n") ? "\r\n" : text.Contains("\n") ? "\n" : text.Contains("\r") ? "\r" : "\r\n";
            var lines = new List<string>();
            var batchStart = 0;
            foreach (var token in Lexer.Tokenize(text))
            {
                if (!IsGoLine(text, token, out var lineStart, out var lineEnd))
                {
                    continue;
                }

                AddBatch(lines, text.Substring(batchStart, lineStart - batchStart));
                lines.Add(text.Substring(lineStart, lineEnd - lineStart).Trim());
                batchStart = lineEnd;
            }

            AddBatch(lines, text.Substring(batchStart));
            return string.Join(newLine, lines);
        }

        private static void AddBatch(List<string> lines, string batch)
        {
            var line = OneLine(batch);
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        private static string OneLine(string batch)
        {
            var result = new StringBuilder(batch.Length);
            var position = 0;
            foreach (var token in Lexer.Tokenize(batch))
            {
                AppendGap(result, batch.Substring(position, token.Start - position));
                if (token.IsLineComment)
                {
                    var body = token.Text.Substring(2).Trim().Replace("*/", "* /").Replace("/*", "/ *");
                    result.Append("/* ").Append(body).Append(" */");
                }
                else
                {
                    result.Append(token.Text);
                }

                position = token.End;
            }

            AppendGap(result, batch.Substring(position));
            return result.ToString().Trim();
        }

        /// <summary>The text between two tokens: every run of white space becomes one space.</summary>
        private static void AppendGap(StringBuilder result, string gap)
        {
            var space = false;
            foreach (var c in gap)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = true;
                    continue;
                }

                if (space)
                {
                    result.Append(' ');
                    space = false;
                }

                result.Append(c);
            }

            if (space)
            {
                result.Append(' ');
            }
        }

        /// <summary>GO on a line of its own (optionally with a count).</summary>
        private static bool IsGoLine(string text, Token token, out int lineStart, out int lineEnd)
        {
            lineStart = lineEnd = 0;
            if (!token.IsWord("GO"))
            {
                return false;
            }

            lineStart = token.Start;
            while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
            {
                lineStart--;
            }

            if (text.Substring(lineStart, token.Start - lineStart).Trim().Length > 0)
            {
                return false;
            }

            lineEnd = token.End;
            while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r')
            {
                lineEnd++;
            }

            var rest = text.Substring(token.End, lineEnd - token.End).Trim();
            return rest.Length == 0 || rest.All(char.IsDigit);
        }
    }
}
