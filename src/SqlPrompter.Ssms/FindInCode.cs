using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Navigation;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Find in Database Code (#212): searches the text of the procedures, functions, views and triggers of the database of the window for the selected
    /// text (or the word at the caret), in the background, and lists the matching lines in the popup; Enter opens the object scrolled to the line.
    /// </summary>
    internal static class FindInCode
    {
        private const string ApplicationName = "SQL Prompter";

        public static void Run(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view == null)
            {
                return;
            }

            try
            {
                var text = SearchText(view);
                var connection = SsmsConnection.GetActive();
                if (string.IsNullOrWhiteSpace(text))
                {
                    Tell("SQL Prompter: select the text to look for, or put the caret on a name.");
                    return;
                }

                if (connection == null || !connection.CanConnect)
                {
                    Tell("SQL Prompter: the window is not connected.");
                    return;
                }

                var regex = CodeSearch.Compile(text, new CodeSearchOptions { WholeWord = true });
                var database = SqlBatch.FindUsedDatabase(view.TextBuffer.CurrentSnapshot, view.Caret.Position.BufferPosition.Position) ?? connection.Database;
                var target = string.IsNullOrEmpty(database) ? connection : connection.WithDatabase(database);
                var loader = new CodeSearchLoader(() => SqlClientConnections.Create(target.BuildConnectionString(ApplicationName)));
                Tell("SQL Prompter: searching the code of " + database + " for " + text + "...");
#pragma warning disable VSSDK007 // fire and forget on purpose
                ThreadHelper.JoinableTaskFactory.RunAsync(() => SearchAsync(view, loader, regex, text, database)).Task.FileAndForget("sqlprompter/findincode");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Log.Error("Find in database code failed", ex);
            }
        }

        private static string SearchText(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!view.Selection.IsEmpty)
            {
                return view.Selection.SelectedSpans[0].GetText().Trim();
            }

            var snapshot = view.TextSnapshot;
            var caret = view.Caret.Position.BufferPosition.Position;
            var text = snapshot.GetText();
            var start = caret;
            while (start > 0 && IsWordChar(text[start - 1]))
            {
                start--;
            }

            var end = caret;
            while (end < text.Length && IsWordChar(text[end]))
            {
                end++;
            }

            return text.Substring(start, end - start);
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';

        private static async Task SearchAsync(IWpfTextView view, CodeSearchLoader loader, System.Text.RegularExpressions.Regex regex, string text, string database)
        {
            CodeSearchResult result = null;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    result = await Task.Run(() => loader.SearchAsync(null, regex, timeout.Token)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Searching the code of " + database + " failed", ex);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (result == null)
            {
                Tell("SQL Prompter: the search in the code of " + database + " failed (see the log).");
                return;
            }

            var skipped = result.Encrypted > 0 ? "; " + result.Encrypted + " encrypted modules skipped" : string.Empty;
            if (result.Hits.Count == 0)
            {
                Tell("SQL Prompter: " + text + " is not used in the " + result.Searched + " modules of " + database + skipped + ".");
                return;
            }

            var items = new List<Tuple<string, string, Action>>();
            foreach (var hit in result.Hits)
            {
                var schema = hit.Schema;
                var name = hit.Name;
                foreach (var match in hit.Matches)
                {
                    var line = match.Line;
                    items.Add(Tuple.Create<string, string, Action>(schema + "." + name + "  (line " + line + ")", match.Text, () =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        GoToDefinitionHandler.OpenObject(view, schema, name, line);
                    }));
                }
            }

            Tell("SQL Prompter: " + items.Count + " lines in " + result.Hits.Count + " of " + result.Searched + " modules" + skipped + ".");
            QuickActionsPopup.ShowItems(view, items, "Find in code: type to filter; Enter opens the object at the line, Esc closes");
        }

        private static void Tell(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText(text);
            }
        }
    }
}
