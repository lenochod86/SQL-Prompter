using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Coloring;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The <i>Tab Color</i> item of the context menu of a document tab: one item per environment of the coloring settings with a check mark on the one that
    /// applies to the window now, then <i>Edit Environments...</i>. Choosing an environment writes the rule of the server and database of that window
    /// (<see cref="ColorRuleEditor"/>: the exact rule is changed, or added; never duplicated) and the colors are refreshed at once.
    /// </summary>
    internal static class TabColorMenu
    {
        /// <summary>The id of the first dynamic item; the others follow it, one per environment.</summary>
        public const int FirstItemId = 0x0600;

        /// <summary>More environments than this are not shown in the menu (they stay in the settings).</summary>
        public const int MaxItems = 40;

        public static void Register(OleMenuCommandService commands, Guid commandSet, int editEnvironmentsId)
        {
            commands.AddCommand(new MenuCommand((s, e) => SqlPrompterPackage.OpenSettings("Coloring"), new CommandID(commandSet, editEnvironmentsId)));

            var root = new CommandID(commandSet, FirstItemId);
            EventHandler invoke = (sender, args) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var command = (DynamicEnvironmentCommand)sender;
                var environments = SettingsService.Current.Coloring.Environments;
                var index = command.MatchedCommandId == 0 ? 0 : command.MatchedCommandId - FirstItemId;
                if (index >= 0 && index < environments.Count)
                {
                    Choose(environments[index].Name);
                }
            };
            var item = new DynamicEnvironmentCommand(root, id => id >= FirstItemId && id < FirstItemId + Math.Min(MaxItems, SettingsService.Current.Coloring.Environments.Count), invoke);
            item.BeforeQueryStatus += (sender, args) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var command = (DynamicEnvironmentCommand)sender;
                var coloring = SettingsService.Current.Coloring;
                var index = command.MatchedCommandId == 0 ? 0 : command.MatchedCommandId - FirstItemId;
                command.MatchedCommandId = 0;
                if (index < 0 || index >= coloring.Environments.Count)
                {
                    command.Visible = false;
                    return;
                }

                var environment = coloring.Environments[index];
                var found = TryGetWindow(out var server, out var database);
                var applies = found ? EnvironmentMatcher.Find(coloring, server, database) : null;
                command.Visible = true;
                command.Enabled = found;
                command.Text = environment.Name.Replace("&", "&&");
                command.Checked = applies != null && string.Equals(applies.Name, environment.Name, StringComparison.OrdinalIgnoreCase);
            };
            commands.AddCommand(item);
        }

        /// <summary>The server and database of the window in front; false when it has no connection (no connection, no color).</summary>
        private static bool TryGetWindow(out string server, out string database)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            server = null;
            database = null;
            var main = System.Windows.Application.Current?.MainWindow;
            return main != null
                && WindowStatusColoring.TryGetFront(new System.Windows.Interop.WindowInteropHelper(main).Handle, out var connected, out var info)
                && connected
                && SsmsConnection.TryReadServerAndDatabase(info, out server, out database);
        }

        private static void Choose(string environment)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!TryGetWindow(out var server, out var database))
                {
                    return;
                }

                var settings = SettingsService.Current.Clone();
                settings.Coloring = ColorRuleEditor.SetEnvironment(settings.Coloring, server, database, environment);
                SettingsService.Update(settings);
                Log.Write("Tab Color: the rule for the server and database of this window uses the environment \"" + environment + "\".");
            }
            catch (Exception ex)
            {
                Log.Error("Choosing the environment of a tab failed", ex);
            }
        }

        /// <summary>One item of the dynamic list: the command table calls <see cref="DynamicItemMatch"/> with the id of each item it asks for.</summary>
        private sealed class DynamicEnvironmentCommand : OleMenuCommand
        {
            private readonly Predicate<int> matches;

            public DynamicEnvironmentCommand(CommandID rootId, Predicate<int> matches, EventHandler invoke)
                : base(invoke, rootId)
            {
                this.matches = matches;
            }

            public override bool DynamicItemMatch(int cmdId)
            {
                if (matches(cmdId))
                {
                    MatchedCommandId = cmdId;
                    return true;
                }

                MatchedCommandId = 0;
                return false;
            }
        }
    }
}
