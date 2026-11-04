using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlPrompter.Core.Snippets
{
    /// <summary>What the expansion of a snippet needs to know about the place it goes to.</summary>
    public sealed class SnippetContext
    {
        /// <summary>The text selected in the editor when the snippet was expanded; empty if none.</summary>
        public string SelectedText { get; set; } = string.Empty;

        /// <summary>Replaces <c>$USER$</c>.</summary>
        public string User { get; set; } = string.Empty;

        /// <summary>Replaces <c>$DBNAME$</c>, the database of the window.</summary>
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>Replaces <c>$DATE(format)$</c>; the clock is a parameter so that tests do not depend on the time.</summary>
        public DateTime Now { get; set; } = DateTime.Now;

        /// <summary>The line ending of the document (CRLF in SSMS).</summary>
        public string NewLine { get; set; } = "\r\n";

        /// <summary>
        /// The whitespace the lines of a multi-line snippet are indented with: the indentation of the line it is inserted into,
        /// so that the snippet lines up with its surroundings. Empty for none.
        /// </summary>
        public string Indent { get; set; } = string.Empty;
    }

    /// <summary>A custom field of an expanded snippet: every place it appears in the text.</summary>
    public sealed class SnippetField
    {
        public SnippetField(string name, string defaultValue, IReadOnlyList<int> offsets)
        {
            Name = name;
            DefaultValue = defaultValue;
            Offsets = offsets;
        }

        public string Name { get; }

        public string DefaultValue { get; }

        /// <summary>Offsets in <see cref="ExpandedSnippet.Text"/> where the default value was inserted; every occurrence has the length of the default value.</summary>
        public IReadOnlyList<int> Offsets { get; }

        public int Length => DefaultValue.Length;
    }

    public sealed class ExpandedSnippet
    {
        public ExpandedSnippet(string text, int cursorOffset, int selectionStart, int selectionEnd, IReadOnlyList<SnippetField> fields)
        {
            Text = text;
            CursorOffset = cursorOffset;
            SelectionStart = selectionStart;
            SelectionEnd = selectionEnd;
            Fields = fields;
        }

        public string Text { get; }

        /// <summary>Where the caret ends (<c>$CURSOR$</c>); the end of the text if the snippet has none.</summary>
        public int CursorOffset { get; }

        /// <summary>The text between <c>$SELECTIONSTART$</c> and <c>$SELECTIONEND$</c> is selected afterwards; -1 if the snippet has no such markers.</summary>
        public int SelectionStart { get; }

        public int SelectionEnd { get; }

        /// <summary>Custom fields in the order of their first appearance in the snippet.</summary>
        public IReadOnlyList<SnippetField> Fields { get; }
    }

    /// <summary>
    /// Turns a snippet into text: built-in placeholders (<c>$CURSOR$</c>, <c>$SELECTEDTEXT$</c>, <c>$SELECTIONSTART$</c> and
    /// <c>$SELECTIONEND$</c>, <c>$DATE(format)$</c>, <c>$USER$</c>, <c>$DBNAME$</c>) are resolved, custom fields (<c>$name$</c>) get
    /// their default values and are listed with all their positions so that they can be edited in the text. <c>$$</c> is a dollar sign.
    /// </summary>
    public static class SnippetExpander
    {
        private static readonly Regex Token = new Regex(
            @"\$\$|\$(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\((?<arg>[^()$]*)\))?\$",
            RegexOptions.CultureInvariant);

        public static ExpandedSnippet Expand(Snippet snippet, SnippetContext context)
        {
            context = context ?? new SnippetContext();
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var placeholder in snippet.Placeholders ?? new List<SnippetPlaceholder>())
            {
                // A field without a default value shows its name, otherwise it would be an invisible empty field.
                defaults[placeholder.Name] = string.IsNullOrEmpty(placeholder.DefaultValue) ? placeholder.Name : placeholder.DefaultValue;
            }

            var body = Normalize(snippet.Body ?? string.Empty, context.NewLine, context.Indent);
            var text = new StringBuilder();
            var fields = new List<SnippetField>();
            var offsets = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var cursor = -1;
            int selectionStart = -1, selectionEnd = -1;
            var position = 0;

            foreach (Match match in Token.Matches(body))
            {
                text.Append(body, position, match.Index - position);
                position = match.Index + match.Length;
                if (match.Value == "$$")
                {
                    text.Append('$');
                    continue;
                }

                var name = match.Groups["name"].Value;
                switch (name.ToUpperInvariant())
                {
                    case "CURSOR":
                        if (cursor < 0)
                        {
                            cursor = text.Length;
                        }

                        break;
                    case "SELECTEDTEXT":
                        text.Append(context.SelectedText ?? string.Empty);
                        break;
                    case "SELECTIONSTART":
                        selectionStart = text.Length;
                        break;
                    case "SELECTIONEND":
                        selectionEnd = text.Length;
                        break;
                    case "USER":
                        text.Append(context.User ?? string.Empty);
                        break;
                    case "DBNAME":
                        text.Append(context.DatabaseName ?? string.Empty);
                        break;
                    case "DATE":
                        text.Append(FormatDate(context.Now, match.Groups["arg"].Success ? match.Groups["arg"].Value : null));
                        break;
                    default:
                        if (!offsets.TryGetValue(name, out var list))
                        {
                            offsets[name] = list = new List<int>();
                            fields.Add(new SnippetField(name, defaults.TryGetValue(name, out var value) ? value : name, list));
                        }

                        list.Add(text.Length);
                        text.Append(fields.First(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).DefaultValue);
                        break;
                }
            }

            text.Append(body, position, body.Length - position);
            if (selectionStart >= 0 && selectionEnd < selectionStart)
            {
                selectionEnd = selectionStart;
            }

            if (selectionStart < 0)
            {
                selectionEnd = -1;
            }

            return new ExpandedSnippet(text.ToString(), cursor >= 0 ? cursor : text.Length, selectionStart, selectionEnd, fields);
        }

        private static string FormatDate(DateTime now, string format)
        {
            try
            {
                return string.IsNullOrEmpty(format) ? now.ToString("d", CultureInfo.CurrentCulture) : now.ToString(format, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>Line endings of the document, and the indentation of the line the snippet goes into on every line after the first.</summary>
        private static string Normalize(string body, string newLine, string indent)
        {
            var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 1)
            {
                return body;
            }

            var result = new StringBuilder(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                result.Append(newLine).Append(lines[i].Length == 0 ? string.Empty : indent).Append(lines[i]);
            }

            return result.ToString();
        }
    }
}
