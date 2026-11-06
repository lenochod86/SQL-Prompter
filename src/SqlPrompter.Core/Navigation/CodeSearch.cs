using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>One line of a module that matches the search (#212).</summary>
    public sealed class CodeMatch
    {
        public CodeMatch(int line, string text)
        {
            Line = line;
            Text = text;
        }

        /// <summary>1-based line in the script of the module.</summary>
        public int Line { get; }

        /// <summary>The line, trimmed.</summary>
        public string Text { get; }
    }

    /// <summary>The way the text is looked for.</summary>
    public sealed class CodeSearchOptions
    {
        public bool CaseSensitive { get; set; }

        public bool WholeWord { get; set; }

        /// <summary>The text is a regular expression.</summary>
        public bool UseRegex { get; set; }
    }

    /// <summary>Finds the lines of a script that contain a text, a name or a regular expression; the line numbers match the script that F12 opens.</summary>
    public static class CodeSearch
    {
        /// <summary>The pattern as a regular expression that matches what the options ask for; null when the text is empty or is not a valid regular expression.</summary>
        public static Regex Compile(string text, CodeSearchOptions options)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            options = options ?? new CodeSearchOptions();
            var pattern = options.UseRegex ? text : Regex.Escape(text);
            if (options.WholeWord)
            {
                pattern = @"(?<![\w@#$])(?:" + pattern + @")(?![\w@#$])";
            }

            try
            {
                return new Regex(pattern, (options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The matching lines of the script, in order (a line is reported once however often it matches).</summary>
        public static IReadOnlyList<CodeMatch> Find(string script, Regex regex)
        {
            var result = new List<CodeMatch>();
            if (regex == null || string.IsNullOrEmpty(script))
            {
                return result;
            }

            var lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                try
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        result.Add(new CodeMatch(i + 1, lines[i].Trim()));
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    break;
                }
            }

            return result;
        }
    }
}
