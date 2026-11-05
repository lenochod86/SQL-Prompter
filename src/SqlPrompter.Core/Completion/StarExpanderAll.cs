using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Completion
{
    /// <summary>The refactoring "expand all <c>*</c>": every select-list star of a script (or of a part of it) becomes its column list.</summary>
    public static class StarExpanderAll
    {
        private static readonly Regex GoLine = new Regex(@"^[ \t]*GO([ \t]+\d+)?[ \t]*(--.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

        /// <summary>
        /// Expands the stars that lie within <paramref name="from"/>…<paramref name="to"/>; a star whose sources are not known (or that is
        /// not in a select list, as in <c>COUNT(*)</c>) stays. <paramref name="skipped"/> counts the select-list stars that stayed.
        /// </summary>
        public static string Expand(string text, int from, int to, IDatabaseMetadata metadata, SuggestionOptions options, out int expanded, out int skipped)
        {
            expanded = 0;
            skipped = 0;
            if (string.IsNullOrEmpty(text) || metadata == null)
            {
                return text;
            }

            options = options ?? SuggestionOptions.Default;
            var batches = new List<int[]>();
            var start = 0;
            foreach (Match go in GoLine.Matches(text))
            {
                batches.Add(new[] { start, go.Index });
                start = go.Index + go.Length;
            }

            batches.Add(new[] { start, text.Length });
            for (var b = batches.Count - 1; b >= 0; b--)
            {
                var batchStart = batches[b][0];
                var batchEnd = batches[b][1];
                if (batchEnd < from || batchStart > to)
                {
                    continue;
                }

                var batch = text.Substring(batchStart, batchEnd - batchStart);
                var stars = Lexer.Tokenize(batch)
                    .Where(t => t.Kind == TokenKind.Operator && t.Text == "*" && batchStart + t.Start >= from && batchStart + t.End <= to)
                    .Select(t => t.End)
                    .OrderByDescending(e => e)
                    .ToList();
                foreach (var caret in stars)
                {
                    var changed = ExpandAt(batch, caret, text, metadata, options);
                    if (changed == null)
                    {
                        if (StarExpander.IsSelectListStar(batch, caret))
                        {
                            skipped++;
                        }

                        continue;
                    }

                    batch = changed;
                    expanded++;
                }

                text = text.Substring(0, batchStart) + batch + text.Substring(batchEnd);
            }

            return text;
        }

        private static string ExpandAt(string batch, int caret, string whole, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            var newLine = whole.Contains("\r\n") ? "\r\n" : whole.Contains("\n") ? "\n" : "\r\n";
            var script = ScriptModel.Build(batch, caret);
            var expansion = StarExpander.TryExpand(batch, caret, metadata, script, options.WithEditor(Layout(batch, caret, newLine, options.Editor.Style)));
            if (expansion == null)
            {
                return null;
            }

            // The text is aligned with the first column, so the layout is read where the replaced text starts (alias.* starts earlier).
            expansion = StarExpander.TryExpand(batch, caret, metadata, script, options.WithEditor(Layout(batch, expansion.Start, newLine, options.Editor.Style))) ?? expansion;
            return batch.Remove(expansion.Start, expansion.Length).Insert(expansion.Start, expansion.Text);
        }

        private static EditorLayout Layout(string batch, int position, string newLine, SqlPrompter.Core.Formatting.FormatStyle style)
        {
            var lineStart = position;
            while (lineStart > 0 && batch[lineStart - 1] != '\n' && batch[lineStart - 1] != '\r')
            {
                lineStart--;
            }

            var prefix = batch.Substring(lineStart, position - lineStart);
            return new EditorLayout
            {
                NewLine = newLine,
                LinePrefix = prefix,
                LineIndent = new string(prefix.TakeWhile(c => c == ' ' || c == '\t').ToArray()),
                Style = style,
            };
        }
    }
}
