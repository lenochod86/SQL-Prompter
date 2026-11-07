using System;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Puts "Script As Temp Table" into the right-click menu of the results grid, next to Copy and Copy with Headers.
    /// The menu is the command bar "SQL Results Grid Tab Context" (found through DTE; its GUID and ID are not needed because
    /// <c>Command.AddControl</c> takes the bar). Controls added through DTE are not stored, so this runs at every start.
    /// </summary>
    internal static class GridContextMenu
    {
        private const string BarName = "SQL Results Grid Tab Context";
        private const string CommandName = "Script As Temp Table";

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
                    if (caption.IndexOf("Script As Temp Table", StringComparison.OrdinalIgnoreCase) >= 0 || caption.IndexOf("Script Results Grid Selection", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // The command stays in the menu from an earlier start; the submenu is added next to it all the same.
                        AddCopyAs(dte, bar, i + 1);
                        return;
                    }

                    if (caption.IndexOf("Copy with Headers", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        position = i + 1;
                    }
                }

                position = Math.Max(1, Math.Min(position, bar.Controls.Count + 1));
                command.AddControl(bar, position);
                AddCopyAs(dte, bar, position + 1);
                Log.Write("Grid menu: added \"" + CommandName + "\" at position " + position + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Adding the command to the results grid menu failed", ex);
            }
        }

        private static readonly int[] CopyIds =
        {
            SqlPrompterPackage.CopyGridAsInListId, SqlPrompterPackage.CopyGridAsValuesId, SqlPrompterPackage.CopyGridAsJsonId, SqlPrompterPackage.CopyGridAsCsvId, SqlPrompterPackage.CopyGridAsMarkdownId,
        };

        /// <summary>The submenu Copy as with the formats of #211.</summary>
        private static void AddCopyAs(EnvDTE.DTE dte, CommandBar bar, int position)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            for (var i = 1; i <= bar.Controls.Count; i++)
            {
                if (string.Equals(bar.Controls[i].Caption, "Copy as", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            position = Math.Max(1, Math.Min(position, bar.Controls.Count + 1));
            var popup = (CommandBarPopup)bar.Controls.Add(MsoControlType.msoControlPopup, Type.Missing, Type.Missing, position, true);
            popup.Caption = "Copy as";
            var index = 1;
            foreach (var id in CopyIds)
            {
                dte.Commands.Item("{" + SqlPrompterPackage.CommandSet.ToString() + "}", id).AddControl(popup.CommandBar, index++);
            }

            Log.Write("Grid menu: added the submenu Copy as at position " + position + ".");
        }
    }
}
