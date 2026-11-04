using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Shell;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Gives the commands of the menu their default keys once. A key binding in the .vsct did not take effect in the SQL query editor
    /// (verified in SSMS 22.11), so the default is set through DTE: "SQL Query Editor::F12". It is set only when the command has no binding
    /// yet and only once, so a key the user removes or changes stays as the user left it.
    /// </summary>
    internal static class DefaultKeyBindings
    {
        private static readonly string MarkerFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SqlPrompter");

        public static void Apply()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null)
                {
                    return;
                }

                Set(dte, "SQLPrompter.GoToDefinition", "SQL Query Editor::F12");
                Set(dte, "SQLPrompter.RunStatementAtCaret", "SQL Query Editor::Shift+F5");
            }
            catch (Exception ex)
            {
                Log.Error("Setting the default keys failed", ex);
            }
        }

        private static string FindOwner(EnvDTE.DTE dte, string binding)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (EnvDTE.Command other in dte.Commands)
            {
                if (other.Bindings is object[] keys && keys.Any(k => string.Equals(Convert.ToString(k), binding, StringComparison.OrdinalIgnoreCase)))
                {
                    return other.Name;
                }
            }

            return null;
        }

        private static void Set(EnvDTE.DTE dte, string commandName, string binding)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var marker = Path.Combine(MarkerFolder, "key-" + commandName + ".applied");
            if (File.Exists(marker))
            {
                return;
            }

            var command = dte.Commands.Item(commandName);
            if (command.Bindings is object[] existing && existing.Length > 0)
            {
                return;
            }

            var taken = FindOwner(dte, binding);
            if (taken != null)
            {
                Log.Write("Default key " + binding + " is used by " + taken + "; " + commandName + " keeps no key.");
                return;
            }

            command.Bindings = new object[] { binding };
            Directory.CreateDirectory(MarkerFolder);
            File.WriteAllText(marker, binding);
            Log.Write("Default key " + binding + " set for " + commandName + ".");
        }
    }
}
