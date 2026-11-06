using System;
using System.Windows;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Commands on the results grid. The grid has no API for an extension, so the selected cells are copied with SSMS's own command
    /// (Edit.CopywithHeaders) and the clipboard text is turned into a temporary table script (CREATE, INSERT, DROP), which open in a new query window on the same connection.
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

                if (!SsmsCompatibility.Has(SqlPrompter.Core.Compatibility.SsmsFeature.ResultsGrid))
                {
                    Tell("This feature is switched off: the copy command of the results grid was not found in this SSMS version (see the log).");
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

                var insert = GridScripter.ToTemporaryTable(copied);
                if (insert == null)
                {
                    Tell("Select the rows (or cells) in the results grid first.");
                    return;
                }

                var database = SsmsConnection.GetActive()?.Database;
                if (!SsmsConnection.OpenNewWindow(database, insert))
                {
                    Clipboard.SetText(insert);
                    Tell("The script is on the clipboard (no connected window to open them in).");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Scripting the grid as a temporary table failed", ex);
            }
        }

        /// <summary>Copies the selected cells with the own copy command of SSMS, formats the text with <paramref name="format"/> and puts the result on the clipboard.</summary>
        public static void CopyAs(Func<string, string> format, string what)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (dte == null)
                {
                    return;
                }

                if (!SsmsCompatibility.Has(SqlPrompter.Core.Compatibility.SsmsFeature.ResultsGrid))
                {
                    Tell("This feature is switched off: the copy command of the results grid was not found in this SSMS version (see the log).");
                    return;
                }

                if (ActiveEditor.GetView()?.HasAggregateFocus == true)
                {
                    Tell("Click into the results grid and select the rows (or cells) first.");
                    return;
                }

                Clipboard.Clear();
                dte.ExecuteCommand("Edit.CopywithHeaders");
                var copied = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                var text = format(copied);
                if (string.IsNullOrEmpty(text))
                {
                    Tell("Select the rows (or cells) in the results grid first.");
                    return;
                }

                Clipboard.SetText(text);
                Log.Write("Grid: copied the selection as " + what + " (" + text.Length + " characters).");
                if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar)) is Microsoft.VisualStudio.Shell.Interop.IVsStatusbar statusBar)
                {
                    statusBar.SetText("SQL Prompter: the selection was copied as " + what + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Copying the grid selection as " + what + " failed", ex);
            }
        }

        private static void Tell(string message) => MessageBox.Show(message, "SQL Prompter", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
