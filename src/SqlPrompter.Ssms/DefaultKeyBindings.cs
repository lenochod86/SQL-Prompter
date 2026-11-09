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
                Set(dte, "SQLPrompter.ShowInObjectExplorer", "SQL Query Editor::Ctrl+F12");
                Set(dte, "SQLPrompter.RunStatementAtCaret", "SQL Query Editor::Shift+F5");
                Set(dte, "SQLPrompter.ParameterInfo", "SQL Query Editor::Ctrl+Shift+Space");
                Set(dte, "SQLPrompter.ShowSuggestions", "SQL Query Editor::Ctrl+Space");
                Set(dte, "SQLPrompter.PickerLeft", "SQL Query Editor::Ctrl+Left Arrow");
                Set(dte, "SQLPrompter.PickerRight", "SQL Query Editor::Ctrl+Right Arrow");
                Set(dte, "SQLPrompter.FormatDocument", "SQL Query Editor::Ctrl+K, Y");
                // Ctrl+Shift+T is Go To File in SSMS; the first of these that nothing uses becomes the key.
                Set(dte, "SQLPrompter.ReopenClosedQueryTab", "Global::Ctrl+Shift+T", "Global::Ctrl+Alt+T", "Global::Ctrl+Shift+Alt+T");
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

        /// <summary>Sets the first of the bindings that no command uses; the marker is written once one is set, or when none of them is free.</summary>
        private static void Set(EnvDTE.DTE dte, string commandName, string binding, params string[] alternatives)
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

            foreach (var candidate in new[] { binding }.Concat(alternatives))
            {
                var taken = FindOwner(dte, candidate);
                if (taken != null)
                {
                    Log.Write("Default key " + candidate + " is used by " + taken + ".");
                    continue;
                }

                command.Bindings = new object[] { candidate };
                Directory.CreateDirectory(MarkerFolder);
                File.WriteAllText(marker, candidate);
                Log.Write("Default key " + candidate + " set for " + commandName + ".");
                return;
            }

            Log.Write(commandName + " keeps no key: every default is used by another command.");
        }
    }
}