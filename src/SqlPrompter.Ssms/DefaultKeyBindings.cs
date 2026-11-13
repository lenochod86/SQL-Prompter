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
                SetOverwriting(dte, "SQLPrompter.RunStatementsUpToCaret", "SQL Query Editor::Ctrl+Shift+F5");
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

        /// <summary>
        /// Like <see cref="Set"/>, but the key is taken from the command that has it in the same scope (the user's decision for Ctrl+Shift+F5, #420); the log says which
        /// command lost it. A binding in another scope is only logged: the more specific scope of the editor wins over it anyway. Once, like the other defaults.
        /// </summary>
        private static void SetOverwriting(EnvDTE.DTE dte, string commandName, string binding)
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

            foreach (EnvDTE.Command other in dte.Commands)
            {
                if (string.Equals(other.Name, commandName, StringComparison.OrdinalIgnoreCase) || !(other.Bindings is object[] keys))
                {
                    continue;
                }

                var kept = keys.Where(k => !string.Equals(Convert.ToString(k), binding, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (kept.Length != keys.Length)
                {
                    other.Bindings = kept;
                    Log.Write("Key " + binding + " was taken from " + other.Name + " for " + commandName + ".");
                }
            }

            var scopeless = binding.Substring(binding.IndexOf("::", StringComparison.Ordinal) + 2);
            var global = FindOwner(dte, "Global::" + scopeless);
            if (global != null)
            {
                Log.Write("Global::" + scopeless + " is also used by " + global + "; the key of the editor wins.");
            }

            command.Bindings = new object[] { binding };
            Directory.CreateDirectory(MarkerFolder);
            File.WriteAllText(marker, binding);
            Log.Write("Default key " + binding + " set for " + commandName + ".");
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