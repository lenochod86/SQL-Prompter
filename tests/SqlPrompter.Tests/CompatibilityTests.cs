using System;
using System.Linq;
using SqlPrompter.Core.Compatibility;
using Xunit;

namespace SqlPrompter.Tests
{
    public class CompatibilityTests
    {
        private static CompatibilityReport Run(params string[] missing) =>
            CompatibilityReport.Run("22.11.0", r => missing.Contains(r.Id) ? "not found" : null);

        [Fact]
        public void EverythingFoundMeansNoMessageAndAllFeatures()
        {
            var report = Run();

            Assert.True(report.AllFound);
            Assert.Null(report.UserMessage());
            Assert.Empty(report.UnavailableFeatures);
            foreach (SsmsFeature feature in Enum.GetValues(typeof(SsmsFeature)))
            {
                Assert.True(report.IsAvailable(feature));
            }
        }

        [Fact]
        public void AMissingRequirementSwitchesOffOnlyItsFeatures()
        {
            var report = Run(CompatibilityRequirements.CommandCopyWithHeaders);

            Assert.False(report.IsAvailable(SsmsFeature.ResultsGrid));
            Assert.True(report.IsAvailable(SsmsFeature.ActiveConnection));
            Assert.True(report.IsAvailable(SsmsFeature.RestoreTabs));
            Assert.Equal(new[] { SsmsFeature.ResultsGrid }, report.UnavailableFeatures);
        }

        [Fact]
        public void TheScriptFactoryIsNeededByTheConnectionFeatures()
        {
            var report = Run(CompatibilityRequirements.ScriptFactoryType);

            Assert.False(report.IsAvailable(SsmsFeature.ActiveConnection));
            Assert.False(report.IsAvailable(SsmsFeature.OpenWindow));
            Assert.False(report.IsAvailable(SsmsFeature.RestoreTabs));
            Assert.True(report.IsAvailable(SsmsFeature.ResultsGrid));
            Assert.True(report.IsAvailable(SsmsFeature.ExecuteHook));
        }

        [Fact]
        public void AFeatureWithSeveralRequirementsIsOffWhenAnyIsMissing()
        {
            Assert.False(Run(CompatibilityRequirements.CommandWordNext).IsAvailable(SsmsFeature.WordKeys));
            Assert.False(Run(CompatibilityRequirements.CommandWordPrevious).IsAvailable(SsmsFeature.WordKeys));
        }

        [Fact]
        public void AProbeThatThrowsCountsAsMissing()
        {
            var report = CompatibilityReport.Run("22.11.0", r => r.Id == CompatibilityRequirements.CommandExecute ? throw new InvalidOperationException("boom") : null);

            Assert.False(report.IsAvailable(SsmsFeature.ExecuteHook));
            Assert.Contains(report.LogLines(), l => l.Contains("boom"));
        }

        [Fact]
        public void TheMessageNamesTheBuildAndTheSwitchedOffFeatures()
        {
            var message = Run(CompatibilityRequirements.CommandCopyWithHeaders, CompatibilityRequirements.CommandExecute).UserMessage();

            Assert.Contains("22.11.0", message);
            Assert.Contains("results grid", message);
            Assert.Contains("metadata refresh", message);
            Assert.DoesNotContain("restoring", message);
        }

        [Fact]
        public void TheLogHasTheSummaryAndEveryMissingRequirement()
        {
            var lines = Run(CompatibilityRequirements.CommandCopyWithHeaders).LogLines().ToList();

            Assert.Contains("SSMS build 22.11.0: " + (CompatibilityRequirements.All.Count - 1) + " of " + CompatibilityRequirements.All.Count + " undocumented requirements found.", lines);
            Assert.Contains(lines, l => l.Contains("MISSING " + CompatibilityRequirements.CommandCopyWithHeaders));
            Assert.Contains(lines, l => l.Contains("Switched off"));
        }

        [Fact]
        public void TheKeyDiffersByBuildAndByWhatIsMissing()
        {
            Assert.Equal(Run().Key, Run().Key);
            Assert.NotEqual(Run().Key, Run(CompatibilityRequirements.CommandExecute).Key);
            Assert.NotEqual(Run().Key, CompatibilityReport.Run("22.12.0", r => null).Key);
        }

        [Fact]
        public void EveryRequirementHasAnIdAFeatureAndADescription()
        {
            Assert.All(CompatibilityRequirements.All, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Id));
                Assert.False(string.IsNullOrWhiteSpace(r.Description));
                Assert.NotEmpty(r.Features);
            });
            Assert.Equal(CompatibilityRequirements.All.Count, CompatibilityRequirements.All.Select(r => r.Id).Distinct().Count());
        }

        [Fact]
        public void EveryFeatureIsNeededBySomeRequirement()
        {
            foreach (SsmsFeature feature in Enum.GetValues(typeof(SsmsFeature)))
            {
                Assert.Contains(CompatibilityRequirements.All, r => r.Features.Contains(feature));
            }
        }

        [Fact]
        public void UncheckedReportAllowsEverything()
        {
            Assert.True(CompatibilityReport.Unchecked.AllFound);
            Assert.True(CompatibilityReport.Unchecked.IsAvailable(SsmsFeature.ActiveConnection));
        }
    }
}
