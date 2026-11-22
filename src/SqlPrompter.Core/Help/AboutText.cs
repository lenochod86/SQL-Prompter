using System;
using System.Text;

namespace SqlPrompter.Core.Help
{
    /// <summary>The texts of the About window (#502): the version line, the update status and the diagnostic text that is copied into a bug report.</summary>
    public static class AboutText
    {
        public const string License = "MIT License";

        public const string Copyright = "Copyright (c) SQL Prompter contributors";

        /// <summary>The version as shown to the user; "unknown" when it could not be read.</summary>
        public static string VersionLine(Version version) => version == null ? "unknown" : version.ToString();

        /// <summary>What the last update check found. <paramref name="latest"/> is null when no check has finished yet, <paramref name="failed"/> when the last one got no answer.</summary>
        public static string UpdateStatus(Version current, Version latest, bool checkedYet, bool failed)
        {
            if (!checkedYet)
            {
                return "Not checked in this session.";
            }

            if (failed || latest == null)
            {
                return "The latest version could not be found.";
            }

            return current != null && latest > current ? "Version " + latest + " is available." : "You have the latest version.";
        }

        /// <summary>The text for a bug report: product version, SSMS build, runtime and system, and the paths of the log.</summary>
        public static string Diagnostics(Version version, string ssmsBuild, string runtime, string os, string logPath, string compatibility)
        {
            var text = new StringBuilder();
            text.AppendLine("SQL Prompter " + VersionLine(version));
            text.AppendLine("SSMS build: " + (string.IsNullOrWhiteSpace(ssmsBuild) ? "unknown" : ssmsBuild));
            if (!string.IsNullOrWhiteSpace(runtime))
            {
                text.AppendLine("Runtime: " + runtime);
            }

            if (!string.IsNullOrWhiteSpace(os))
            {
                text.AppendLine("OS: " + os);
            }

            if (!string.IsNullOrWhiteSpace(compatibility))
            {
                text.AppendLine("Compatibility: " + compatibility);
            }

            if (!string.IsNullOrWhiteSpace(logPath))
            {
                text.AppendLine("Log: " + logPath);
            }

            return text.ToString().TrimEnd();
        }
    }
}
