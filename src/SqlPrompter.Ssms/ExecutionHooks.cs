using System;
using System.Linq;
using System.Windows;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Coloring;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Hooks into running a query (Query.Execute): first the execution warnings (a DELETE or UPDATE without WHERE asks for confirmation and
    /// the run is cancelled on No), then the run is recorded in the query history, and the closing of a window gives its runs the closing time.
    /// </summary>
    internal sealed class ExecutionHooks
    {
        private CommandEvents commandEvents;
        private DocumentEvents documentEvents;

        public static ExecutionHooks Start(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var hooks = new ExecutionHooks();
            try
            {
                var command = dte.Commands.Item("Query.Execute");
                hooks.commandEvents = dte.Events.CommandEvents[command.Guid, command.ID];
                hooks.commandEvents.BeforeExecute += (string guid, int id, object customIn, object customOut, ref bool cancelDefault) =>
                {
                    try
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        var text = SchemaChangeWatcher.ExecutedText(dte);
                        if (!ConfirmExecution(text))
                        {
                            cancelDefault = true;
                            Log.Write("Execution cancelled after a warning.");
                            return;
                        }

                        HistoryService.Record(text, dte.ActiveDocument?.FullName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Preparing the execution failed", ex);
                    }
                };
                hooks.documentEvents = dte.Events.DocumentEvents;
                hooks.documentEvents.DocumentClosing += document =>
                {
                    try
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        HistoryService.DocumentClosed(document?.FullName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Recording the closing of the window failed", ex);
                    }
                };
                Log.Write("Execution warnings and query history are active.");
            }
            catch (Exception ex)
            {
                Log.Error("Starting the execution hooks failed", ex);
            }

            return hooks;
        }

        /// <summary>False when the script has a dangerous statement and the user declined to run it.</summary>
        private static bool ConfirmExecution(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var behavior = SettingsService.Current.Behavior;
            var messages = ExecutionWarnings.Find(text, behavior.WarnDeleteWithoutWhere, behavior.WarnUpdateWithoutWhere).Select(w => w.Message).ToList();
            var heading = "This script contains:";
            var connection = SsmsConnection.GetActive();
            var rule = connection == null ? null : EnvironmentMatcher.FindRule(SettingsService.Current.Coloring, connection.Server, connection.Database);
            if (rule != null && rule.AskBeforeChanges)
            {
                var changes = ChangeWarnings.FindChanges(text).Select(c => c.Message).Where(m => !messages.Any(x => x.Contains(m.Substring(m.IndexOf('('), m.IndexOf(')') - m.IndexOf('(') + 1)))).ToList();
                if (changes.Count > 0)
                {
                    heading = "Environment " + rule.Environment + " (server " + connection.Server + ", database " + connection.Database + ") asks before changes. This script contains:";
                    messages.AddRange(changes);
                }
            }

            if (behavior.WarnOpenTransaction)
            {
                var open = ChangeWarnings.OpenTransactionLine(text);
                if (open > 0)
                {
                    messages.Add("The transaction begun on line " + open + " is left open (no COMMIT or ROLLBACK in the batch).");
                }
            }

            if (messages.Count == 0)
            {
                return true;
            }

            var lines = messages.Take(8).ToList();
            var more = messages.Count > lines.Count ? "\n...and " + (messages.Count - lines.Count) + " more." : string.Empty;
            var answer = MessageBox.Show(
                heading + "\n\n" + string.Join("\n", lines) + more + "\n\nRun it anyway?",
                "SQL Prompter: execution warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            return answer == MessageBoxResult.Yes;
        }
    }
}