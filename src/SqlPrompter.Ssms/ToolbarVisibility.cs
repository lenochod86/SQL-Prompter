using System;
using System.IO;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Shows the SQL History toolbar once after it was installed: SSMS keeps a new toolbar hidden until the user turns it on. A marker file
    /// remembers that this was done, so a toolbar the user hides afterwards stays hidden.
    /// </summary>
    internal static class ToolbarVisibility
    {
        private const string BarName = "SQL History";

        public static void ShowOnce()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlPrompter", "toolbar-sqlhistory.applied");
                if (File.Exists(marker))
                {
                    return;
                }

                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                var bars = (CommandBars)dte?.CommandBars;
                if (bars == null)
                {
                    return;
                }

                foreach (CommandBar bar in bars)
                {
                    if (string.Equals(bar.Name, BarName, StringComparison.Ordinal))
                    {
                        bar.Visible = true;
                        Directory.CreateDirectory(Path.GetDirectoryName(marker));
                        File.WriteAllText(marker, "shown");
                        Log.Write("The toolbar " + BarName + " was switched on.");
                        return;
                    }
                }

                Log.Write("The toolbar " + BarName + " was not found; it can be turned on in the toolbar list.");
            }
            catch (Exception ex)
            {
                Log.Error("Showing the SQL History toolbar failed", ex);
            }
        }
    }
}
