using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Compatibility;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The compatibility gate: at start the undocumented things SQL Prompter relies on (types and members reached by reflection, DTE commands)
    /// are probed once. What is missing switches off only the features that need it, is written to the log and is told to the user once per
    /// SSMS build (see docs/architecture-review.md, #186). The list of requirements and the report are in Core.
    /// </summary>
    internal static class SsmsCompatibility
    {
        private static volatile CompatibilityReport current = CompatibilityReport.Unchecked;

        public static CompatibilityReport Current => current;

        /// <summary>True when the feature can work in this SSMS (also before the check has run).</summary>
        public static bool Has(SsmsFeature feature) => current.IsAvailable(feature);

        /// <summary>Probes every requirement on the UI thread; never throws.</summary>
        public static CompatibilityReport Run(EnvDTE.DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var report = CompatibilityReport.Run(Build(), requirement => Probe(dte, requirement.Id));
                current = report;
                foreach (var line in report.LogLines())
                {
                    Log.Write(line);
                }

                return report;
            }
            catch (Exception ex)
            {
                Log.Error("The compatibility check failed", ex);
                return current;
            }
        }

        /// <summary>Tells the user once per outcome (build and what is missing) that features are switched off.</summary>
        public static void TellOnce(IServiceProvider services, CompatibilityReport report)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var message = report.UserMessage();
            if (message == null)
            {
                return;
            }

            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPrompter", "compatibility.txt");
                if (File.Exists(path) && File.ReadAllText(path) == report.Key)
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report.Key);
                VsShellUtilities.ShowMessageBox(services, message, "SQL Prompter", Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK, Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (Exception ex)
            {
                Log.Write("Telling about the compatibility failed: " + ex.Message);
            }
        }

        private static string Build()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileVersionInfo.ProductVersion;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Null when the requirement is met, otherwise what was not found.</summary>
        private static string Probe(EnvDTE.DTE dte, string id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if ((Environment.GetEnvironmentVariable("SQLPROMPTER_SIMULATE_MISSING") ?? string.Empty).Split(',', ';').Contains(id))
            {
                return "simulated as missing (SQLPROMPTER_SIMULATE_MISSING)";
            }

            switch (id)
            {
                case CompatibilityRequirements.ScriptFactoryType:
                    return SsmsConnection.ScriptFactoryTypeOrNull() == null ? "the type was not found" : null;
                case CompatibilityRequirements.ScriptFactoryInstance:
                    return Factory()?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static) == null ? "no public static property Instance" : null;
                case CompatibilityRequirements.ActiveConnectionInfo:
                    return ConnectionInfoType() == null ? "no property CurrentlyActiveWndConnectionInfo.UIConnectionInfo" : null;
                case CompatibilityRequirements.ConnectionInfoType:
                    return ConnectionInfoType() == null ? "the type was not found" : null;
                case CompatibilityRequirements.ConnectionInfoMembers:
                    var type = ConnectionInfoType();
                    var missing = new[] { "ServerName", "UserName", "AuthenticationType", "AdvancedOptions" }
                        .Where(name => type?.GetProperty(name) == null).ToList();
                    return missing.Count == 0 ? null : "no property " + string.Join(", ", missing);
                case CompatibilityRequirements.ConnectionInfoCreate:
                    return ConnectionInfoType()?.GetConstructor(Type.EmptyTypes) == null ? "no public parameterless constructor" : null;
                case CompatibilityRequirements.CreateNewBlankScript:
                    var instanceType = Factory()?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.PropertyType ?? Factory();
                    var found = instanceType?.GetMethods().Any(m =>
                        m.Name == "CreateNewBlankScript" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.Name == "UIConnectionInfo");
                    return found == true ? null : "no method CreateNewBlankScript with 3 parameters and a UIConnectionInfo";
                case CompatibilityRequirements.CommandCopyWithHeaders:
                    return Command(dte, "Edit.CopywithHeaders");
                case CompatibilityRequirements.CommandExecute:
                    return Command(dte, "Query.Execute");
                case CompatibilityRequirements.CommandDisconnect:
                    return Command(dte, "Query.Disconnect");
                case CompatibilityRequirements.CommandWordPrevious:
                    return Command(dte, "Edit.WordPrevious");
                case CompatibilityRequirements.CommandWordNext:
                    return Command(dte, "Edit.WordNext");
                default:
                    return "no probe for this requirement";
            }
        }

        private static Type Factory() => SsmsConnection.ScriptFactoryTypeOrNull();

        /// <summary>The UIConnectionInfo type, reached through the property types so that no window or instance is needed.</summary>
        private static Type ConnectionInfoType() =>
            Factory()?.GetProperty("CurrentlyActiveWndConnectionInfo")?.PropertyType?.GetProperty("UIConnectionInfo")?.PropertyType;

        private static string Command(EnvDTE.DTE dte, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return dte?.Commands.Item(name) == null ? "the command does not exist" : null;
            }
            catch (Exception)
            {
                return "the command does not exist";
            }
        }
    }
}
