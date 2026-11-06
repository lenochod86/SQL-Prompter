using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Compatibility
{
    /// <summary>A feature that depends on something undocumented in SSMS and is switched off alone when that is missing.</summary>
    public enum SsmsFeature
    {
        /// <summary>The connection of the active query window (metadata, history, tabs).</summary>
        ActiveConnection,

        /// <summary>Opening a query window connected like the active one (the grid as INSERT, new window commands).</summary>
        OpenWindow,

        /// <summary>Restoring the query tabs of the last session.</summary>
        RestoreTabs,

        /// <summary>The actions of the results grid.</summary>
        ResultsGrid,

        /// <summary>Hooks on running a query (refreshing metadata after DDL, execution warnings, query history).</summary>
        ExecuteHook,

        /// <summary>Ctrl+Left / Ctrl+Right keep their meaning of moving by a word where the column picker does not take them.</summary>
        WordKeys,
    }

    /// <summary>One undocumented thing SQL Prompter relies on (a type, a member, a DTE command) and the features that need it.</summary>
    public sealed class CompatibilityRequirement
    {
        public CompatibilityRequirement(string id, string description, params SsmsFeature[] features)
        {
            Id = id;
            Description = description;
            Features = features;
        }

        public string Id { get; }

        public string Description { get; }

        public IReadOnlyList<SsmsFeature> Features { get; }
    }

    /// <summary>Everything undocumented that SQL Prompter relies on; the SSMS layer has a probe for each id.</summary>
    public static class CompatibilityRequirements
    {
        public const string ScriptFactoryType = "ScriptFactory.Type";
        public const string ScriptFactoryInstance = "ScriptFactory.Instance";
        public const string ActiveConnectionInfo = "ScriptFactory.CurrentlyActiveWndConnectionInfo";
        public const string ConnectionInfoType = "UIConnectionInfo.Type";
        public const string ConnectionInfoMembers = "UIConnectionInfo.Members";
        public const string ConnectionInfoCreate = "UIConnectionInfo.Create";
        public const string CreateNewBlankScript = "ScriptFactory.CreateNewBlankScript";
        public const string CommandCopyWithHeaders = "Command.Edit.CopywithHeaders";
        public const string CommandExecute = "Command.Query.Execute";
        public const string CommandDisconnect = "Command.Query.Disconnect";
        public const string CommandWordPrevious = "Command.Edit.WordPrevious";
        public const string CommandWordNext = "Command.Edit.WordNext";

        public static IReadOnlyList<CompatibilityRequirement> All { get; } = new[]
        {
            new CompatibilityRequirement(ScriptFactoryType, "type Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory (SQLEditors.dll)",
                SsmsFeature.ActiveConnection, SsmsFeature.OpenWindow, SsmsFeature.RestoreTabs),
            new CompatibilityRequirement(ScriptFactoryInstance, "static property ScriptFactory.Instance",
                SsmsFeature.ActiveConnection, SsmsFeature.OpenWindow, SsmsFeature.RestoreTabs),
            new CompatibilityRequirement(ActiveConnectionInfo, "property ScriptFactory.CurrentlyActiveWndConnectionInfo with a UIConnectionInfo",
                SsmsFeature.ActiveConnection, SsmsFeature.OpenWindow),
            new CompatibilityRequirement(ConnectionInfoType, "type UIConnectionInfo",
                SsmsFeature.ActiveConnection, SsmsFeature.OpenWindow, SsmsFeature.RestoreTabs),
            new CompatibilityRequirement(ConnectionInfoMembers, "properties ServerName, UserName, AuthenticationType, AdvancedOptions of UIConnectionInfo",
                SsmsFeature.ActiveConnection, SsmsFeature.OpenWindow),
            new CompatibilityRequirement(ConnectionInfoCreate, "a public parameterless constructor of UIConnectionInfo",
                SsmsFeature.OpenWindow, SsmsFeature.RestoreTabs),
            new CompatibilityRequirement(CreateNewBlankScript, "method ScriptFactory.CreateNewBlankScript(…, UIConnectionInfo, …)",
                SsmsFeature.OpenWindow, SsmsFeature.RestoreTabs),
            new CompatibilityRequirement(CommandCopyWithHeaders, "command Edit.CopywithHeaders", SsmsFeature.ResultsGrid),
            new CompatibilityRequirement(CommandExecute, "command Query.Execute", SsmsFeature.ExecuteHook),
            new CompatibilityRequirement(CommandDisconnect, "command Query.Disconnect", SsmsFeature.RestoreTabs),
            new CompatibilityRequirement(CommandWordPrevious, "command Edit.WordPrevious", SsmsFeature.WordKeys),
            new CompatibilityRequirement(CommandWordNext, "command Edit.WordNext", SsmsFeature.WordKeys),
        };
    }

    /// <summary>The result of probing one requirement.</summary>
    public sealed class CompatibilityResult
    {
        public CompatibilityResult(CompatibilityRequirement requirement, string problem)
        {
            Requirement = requirement;
            Problem = problem;
        }

        public CompatibilityRequirement Requirement { get; }

        /// <summary>Null when the requirement is met; otherwise what was not found.</summary>
        public string Problem { get; }

        public bool Found => Problem == null;
    }

    /// <summary>What was found in the SSMS that runs: which features can work and which are switched off.</summary>
    public sealed class CompatibilityReport
    {
        private readonly HashSet<SsmsFeature> unavailable;

        public CompatibilityReport(string ssmsBuild, IReadOnlyList<CompatibilityResult> results)
        {
            SsmsBuild = string.IsNullOrWhiteSpace(ssmsBuild) ? "unknown" : ssmsBuild;
            Results = results;
            unavailable = new HashSet<SsmsFeature>(results.Where(r => !r.Found).SelectMany(r => r.Requirement.Features));
        }

        /// <summary>The report of a SSMS where everything was found (used until the check has run).</summary>
        public static CompatibilityReport Unchecked { get; } = new CompatibilityReport("unchecked", Array.Empty<CompatibilityResult>());

        /// <summary>Probes every requirement; a probe that returns text (what is missing) or throws marks the requirement as missing.</summary>
        public static CompatibilityReport Run(string ssmsBuild, Func<CompatibilityRequirement, string> probe)
        {
            var results = new List<CompatibilityResult>();
            foreach (var requirement in CompatibilityRequirements.All)
            {
                string problem;
                try
                {
                    problem = probe(requirement);
                }
                catch (Exception ex)
                {
                    problem = "the probe failed: " + ex.Message;
                }

                results.Add(new CompatibilityResult(requirement, problem));
            }

            return new CompatibilityReport(ssmsBuild, results);
        }

        public string SsmsBuild { get; }

        public IReadOnlyList<CompatibilityResult> Results { get; }

        public bool AllFound => unavailable.Count == 0;

        public bool IsAvailable(SsmsFeature feature) => !unavailable.Contains(feature);

        public IReadOnlyList<SsmsFeature> UnavailableFeatures => unavailable.OrderBy(f => f).ToList();

        /// <summary>Identifies this outcome (the build and what is missing), so that the user is told once per outcome.</summary>
        public string Key => SsmsBuild + "|" + string.Join(",", Results.Where(r => !r.Found).Select(r => r.Requirement.Id));

        public static string Title(SsmsFeature feature)
        {
            switch (feature)
            {
                case SsmsFeature.ActiveConnection: return "completion from the database of the window, history and warnings";
                case SsmsFeature.OpenWindow: return "opening a new query window from a command";
                case SsmsFeature.RestoreTabs: return "restoring the query tabs";
                case SsmsFeature.ResultsGrid: return "the results grid actions";
                case SsmsFeature.ExecuteHook: return "the actions around running a query (metadata refresh, warnings, history)";
                case SsmsFeature.WordKeys: return "Ctrl+Left / Ctrl+Right as word keys";
                default: return feature.ToString();
            }
        }

        /// <summary>Lines for the log: the build and every requirement with the result.</summary>
        public IEnumerable<string> LogLines()
        {
            yield return "SSMS build " + SsmsBuild + ": " + Results.Count(r => r.Found) + " of " + Results.Count + " undocumented requirements found.";
            foreach (var result in Results.Where(r => !r.Found))
            {
                yield return "  MISSING " + result.Requirement.Id + " (" + result.Requirement.Description + "): " + result.Problem;
            }

            foreach (var feature in UnavailableFeatures)
            {
                yield return "  Switched off: " + Title(feature) + ".";
            }
        }

        /// <summary>The text for the user, or null when everything was found.</summary>
        public string UserMessage()
        {
            if (AllFound)
            {
                return null;
            }

            return "SQL Prompter found that SSMS " + SsmsBuild + " differs from the version it was tested with. Switched off: " +
                string.Join("; ", UnavailableFeatures.Select(Title)) + ". Everything else works. Details are in the log (" + "%LOCALAPPDATA%\\SqlPrompter\\sqlprompter.log).";
        }
    }
}
