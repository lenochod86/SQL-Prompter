using System;
using System.ComponentModel.Design;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The submenu Formatting Styles of the SQL Prompter menu: one item per style with a check mark on the active one (choosing an item makes that style
    /// the active one), then <i>Edit Formatting Styles...</i>. The items are made when the menu opens (a dynamic item list of the command table).
    /// </summary>
    internal static class FormattingStylesMenu
    {
        /// <summary>The id of the first dynamic item; the others follow it, one per style.</summary>
        public const int FirstItemId = 0x0400;

        /// <summary>More styles than this are not shown in the menu (they stay in the window).</summary>
        public const int MaxItems = 40;

        public static void Register(OleMenuCommandService commands, Guid commandSet)
        {
            var root = new CommandID(commandSet, FirstItemId);
            EventHandler invoke = (sender, args) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var command = (DynamicStyleCommand)sender;
                var styles = SettingsService.Current.FormattingStyles;
                var index = command.MatchedCommandId == 0 ? 0 : command.MatchedCommandId - FirstItemId;
                if (index >= 0 && index < styles.Count)
                {
                    Activate(styles[index].Name);
                }
            };
            var item = new DynamicStyleCommand(root, id => id >= FirstItemId && id < FirstItemId + Math.Min(MaxItems, SettingsService.Current.FormattingStyles.Count), invoke);
            item.BeforeQueryStatus += (sender, args) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var command = (DynamicStyleCommand)sender;
                var styles = SettingsService.Current.FormattingStyles;
                var index = command.MatchedCommandId == 0 ? 0 : command.MatchedCommandId - FirstItemId;
                command.MatchedCommandId = 0;
                if (index < 0 || index >= styles.Count)
                {
                    command.Visible = false;
                    return;
                }

                command.Visible = true;
                command.Enabled = true;
                command.Text = styles[index].Name.Replace("&", "&&");
                command.Checked = string.Equals(styles[index].Name, SettingsService.Current.ActiveStyle.Name, StringComparison.OrdinalIgnoreCase);
            };
            commands.AddCommand(item);
        }

        private static void Activate(string name)
        {
            try
            {
                var settings = SettingsService.Current.Clone();
                settings.ActiveFormattingStyle = name;
                settings.EnsureStyles();
                SettingsService.Update(settings);
                Log.Write("The formatting style \"" + name + "\" is the active one.");
            }
            catch (Exception ex)
            {
                Log.Error("Changing the active formatting style failed", ex);
            }
        }

        /// <summary>One item of the dynamic list: the command table calls <see cref="DynamicItemMatch"/> with the id of each item it asks for.</summary>
        private sealed class DynamicStyleCommand : OleMenuCommand
        {
            private readonly Predicate<int> matches;

            public DynamicStyleCommand(CommandID rootId, Predicate<int> matches, EventHandler invoke)
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
