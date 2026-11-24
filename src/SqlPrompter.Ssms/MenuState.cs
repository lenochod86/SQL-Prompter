using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The state of the menu commands (#503): builds the <see cref="CommandSituation"/> from what the editor and the shell already know (no database access, nothing slow),
    /// asks the table in Core and registers a command so that it is grayed out when it cannot be used and does nothing (with a message in the status bar) when a shortcut
    /// runs it all the same.
    /// </summary>
    internal static class MenuState
    {
        private static readonly TimeSpan CacheTime = TimeSpan.FromMilliseconds(250);
        private static CommandSituation cached;
        private static DateTime cachedAt;

        /// <summary>The situation now. A menu asks for many commands in a row, so the answer is kept for a moment.</summary>
        public static CommandSituation Current()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (cached != null && DateTime.UtcNow - cachedAt < CacheTime)
            {
                return cached;
            }

            var situation = new CommandSituation();
            try
            {
                var view = ActiveEditor.GetView();
                var tabs = TabSessionService.Current;
                var editor = view != null && view.TextBuffer.ContentType.IsOfType(SqlContentType.Name) && !view.TextBuffer.IsReadOnly(0) && tabs?.HasActiveQueryDocument() != false;
                situation.Editor = editor;
                situation.Selection = editor && !view.Selection.IsEmpty;

                situation.QueryTab = editor ? true : tabs?.HasActiveQueryDocument();
                situation.ClosedTab = tabs == null ? (bool?)null : tabs.ClosedTabsList.Count > 0;

                // Unknown when this SSMS build does not give the connection (the compatibility check switched it off).
                situation.Connection = SsmsCompatibility.Has(SqlPrompter.Core.Compatibility.SsmsFeature.ActiveConnection) ? SsmsConnection.GetActiveOfWindow() != null : (bool?)null;

                // The grid is read only when it has the focus; otherwise nothing is known about it and its commands stay enabled.
                var cells = GridSelectionReader.SelectedCellCount();
                if (cells >= 0)
                {
                    situation.GridSelection = cells > 0;
                    situation.GridOneColumn = !GridSelectionReader.SelectionSpansSeveralColumns();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reading the state of the menu failed", ex);
            }

            cached = situation;
            cachedAt = DateTime.UtcNow;
            return situation;
        }

        public static bool IsEnabled(MenuCommandKind kind)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return CommandAvailability.IsEnabled(kind, Current());
        }

        /// <summary>Registers a command whose state follows <see cref="CommandAvailability"/>.</summary>
        public static OleMenuCommand Add(OleMenuCommandService commands, Guid commandSet, int id, MenuCommandKind kind, Action run)
        {
            OleMenuCommand command;
            command = new OleMenuCommand(
                (s, e) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (!IsEnabled(kind))
                    {
                        // A disabled command that a shortcut runs does nothing.
                        SetStatus("SQL Prompter: that command is not available here (" + CommandAvailability.NeedsOf(kind).ToString().ToLowerInvariant() + " needed).");
                        return;
                    }

                    run();
                },
                new CommandID(commandSet, id));
            command.BeforeQueryStatus += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                command.Enabled = IsEnabled(kind);
            };
            commands.AddCommand(command);
            return command;
        }

        private static void SetStatus(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText(text);
            }
        }
    }
}
