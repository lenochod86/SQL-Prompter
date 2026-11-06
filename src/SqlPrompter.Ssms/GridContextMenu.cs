using System;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Puts "Script Results Grid Selection As Temp Table" into the right-click menu of the results grid, next to Copy and Copy with Headers.
    /// The menu is the command bar "SQL Results Grid Tab Context" (found through DTE; its GUID and ID are not needed because
    /// <c>Command.AddControl</c> takes the bar). Controls added through DTE are not stored, so this runs at every start.
    /// </summary>
    internal static class GridContextMenu
    {
        private const string BarName = "SQL Results Grid Tab Context";
        private const string CommandName = "Script Results Grid Selection As Temp Table";

        public static void Install()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                var bars = (CommandBars)dte?.CommandBars;
                if (bars == null)
                {
                    return;
                }

                CommandBar bar = null;
                foreach (CommandBar candidate in bars)
                {
                    if (string.Equals(candidate.Name, BarName, StringComparison.Ordinal))
                    {
                        bar = candidate;
                        break;
                    }
                }

                if (bar == null)
                {
                    Log.Write("The results grid menu (" + BarName + ") was not found; the grid command stays in the SQL Prompter menu only.");
                    return;
                }

                var command = dte.Commands.Item("{" + SqlPrompterPackage.CommandSet.ToString() + "}", SqlPrompterPackage.ScriptGridAsInsertId);
                var position = 1;
                for (var i = 1; i <= bar.Controls.Count; i++)
                {
                    var caption = bar.Controls[i].Caption ?? string.Empty;
                    if (caption.IndexOf("Script Results Grid Selection", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return;
                    }

                    if (caption.IndexOf("Copy with Headers", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        position = i + 1;
                    }
                }

                position = Math.Max(1, Math.Min(position, bar.Controls.Count + 1));
                command.AddControl(bar, position);
                Log.Write("Grid menu: added \"" + CommandName + "\" at position " + position + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Adding the command to the results grid menu failed", ex);
            }
        }
    }
}
