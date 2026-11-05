using System;
using System.Windows;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Commands on the results grid. The grid has no API for an extension, so the selected cells are copied with SSMS's own command
    /// (Edit.CopywithHeaders) and the clipboard text is turned into INSERT statements, which open in a new query window on the same connection.
    /// </summary>
    internal static class GridCommands
    {
        public static void ScriptSelectionAsInsert()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (dte == null)
                {
                    return;
                }

                // In the editor the same copy command would take the SQL text, not rows.
                if (ActiveEditor.GetView()?.HasAggregateFocus == true)
                {
                    Tell("Click into the results grid and select the rows (or cells) first.");
                    return;
                }

                string original = null;
                try
                {
                    original = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                }
                catch (Exception ex)
                {
                    Log.Write("Reading the clipboard failed: " + ex.Message);
                }

                Clipboard.Clear();
                dte.ExecuteCommand("Edit.CopywithHeaders");
                var copied = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                if (original != null && string.IsNullOrEmpty(copied))
                {
                    Clipboard.SetText(original);
                }

                var query = SchemaChangeWatcher.ExecutedText(dte);
                var insert = GridScripter.ToInsert(copied, GridScripter.GuessTable(query));
                if (insert == null)
                {
                    Tell("Select the rows (or cells) in the results grid first.");
                    return;
                }

                var database = SsmsConnection.GetActive()?.Database;
                if (!SsmsConnection.OpenNewWindow(database, insert))
                {
                    Clipboard.SetText(insert);
                    Tell("The INSERT statements are on the clipboard (no connected window to open them in).");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Scripting the grid as INSERT failed", ex);
            }
        }

        private static void Tell(string message) => MessageBox.Show(message, "SQL Prompter", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
