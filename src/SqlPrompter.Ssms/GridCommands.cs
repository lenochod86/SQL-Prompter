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
                if (!SsmsConnection.OpenNewWindow(database, insert, kind: SqlPrompter.Core.History.OpenedTabKind.Generated))
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

        private static string ExcelFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPrompter", "temp");

        /// <summary>Deletes the Excel files older than a day from the temporary folder, in the background (#445).</summary>
        public static void CleanTemporaryExcelFiles()
        {
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                var deleted = GridExcel.Cleanup(ExcelFolder, DateTime.Now);
                if (deleted > 0)
                {
                    Log.Write("Temporary Excel files: " + deleted + " old file(s) deleted.");
                }
            });
        }

        /// <summary>
        /// Writes the selected cells (the whole result when nothing or one cell is selected) with the names of the columns into a new .xlsx file in the
        /// temporary folder and opens it the way File Explorer does (#445). The cells are copied with the own command of SSMS; the file is written in the
        /// background.
        /// </summary>
        public static void OpenInExcel()
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
                    Tell("Click into the results grid first.");
                    return;
                }

                // Nothing or a single cell selected: the whole result, as Select All would take it.
                var selected = GridSelectionReader.SelectedCellCount();
                if (selected >= 0 && selected <= 1)
                {
                    dte.ExecuteCommand("Edit.SelectAll");
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
                if (original != null)
                {
                    // The clipboard is the user's: only the grid text was needed.
                    Clipboard.SetText(original);
                }

                if (!GridExcel.TryParse(copied, out var headers, out var rows))
                {
                    Tell("Select the rows (or cells) in the results grid first.");
                    return;
                }

                var folder = ExcelFolder;
                SetStatus("SQL Prompter: writing " + rows.Count + " rows for Excel...");
#pragma warning disable VSSDK007 // fire and forget on purpose: the file is written in the background
                ThreadHelper.JoinableTaskFactory.RunAsync(() => WriteAndOpenAsync(folder, headers, rows)).Task.FileAndForget("sqlprompter/openinexcel");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Log.Error("Opening the grid in Excel failed", ex);
            }
        }

        private static async System.Threading.Tasks.Task WriteAndOpenAsync(string folder, System.Collections.Generic.List<string> headers, System.Collections.Generic.List<string[]> rows)
        {
            string path = null;
            XlsxResult result = null;
            try
            {
                path = await System.Threading.Tasks.Task.Run(() =>
                {
                    System.IO.Directory.CreateDirectory(folder);
                    GridExcel.Cleanup(folder, DateTime.Now);
                    var target = GridExcel.NewPath(folder, DateTime.Now);
                    using (var stream = new System.IO.FileStream(target, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
                    {
                        result = XlsxWriter.Write(stream, headers, rows);
                    }

                    return target;
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error("Writing the Excel file failed", ex);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Tell("The Excel file could not be written (see the log).");
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var notes = string.Empty;
            if (result.RowsTruncated)
            {
                notes += " Only the first " + result.Rows + " rows fit on a sheet.";
            }

            if (result.ColumnsTruncated)
            {
                notes += " Only the first " + result.Columns + " columns fit on a sheet.";
            }

            if (result.CutTexts > 0)
            {
                Log.Write("Excel file: " + result.CutTexts + " text(s) longer than " + XlsxWriter.MaxTextLength + " characters were cut.");
            }

            Log.Write("Excel file written: " + path + " (" + result.Rows + " rows, " + result.Columns + " columns).");
            try
            {
                // The way File Explorer opens a file: the program associated with .xlsx, or the standard dialog of Windows when there is none.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                SetStatus("SQL Prompter: opened " + System.IO.Path.GetFileName(path) + "." + notes);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // The user cancelled the dialog of Windows.
                SetStatus("SQL Prompter: " + System.IO.Path.GetFileName(path) + " was written." + notes);
            }
            catch (Exception ex)
            {
                Log.Error("Opening " + path + " failed", ex);
                SetStatus("SQL Prompter: " + System.IO.Path.GetFileName(path) + " was written but could not be opened (see the log).");
            }

            if (notes.Length > 0)
            {
                Tell("The result does not fit on one sheet." + notes);
            }
        }

        private static void SetStatus(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar)) is Microsoft.VisualStudio.Shell.Interop.IVsStatusbar statusBar)
            {
                statusBar.SetText(text);
            }
        }

        private static void Tell(string message) => MessageBox.Show(message, "SQL Prompter", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
