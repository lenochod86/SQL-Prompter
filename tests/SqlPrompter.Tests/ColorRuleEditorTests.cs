using System.Linq;
using SqlPrompter.Core.Coloring;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class ColorRuleEditorTests
    {
        private static ColorSettings Settings(params ColorRule[] rules)
        {
            var settings = new ColorSettings();
            settings.Environments.Add(new ColorEnvironment { Name = "Production", Color = "#FF0000" });
            settings.Environments.Add(new ColorEnvironment { Name = "Testing", Color = "#00FF00" });
            settings.Rules.AddRange(rules);
            return settings;
        }

        [Fact]
        public void AddsARuleForTheServerAndDatabaseWhenThereIsNone()
        {
            var result = ColorRuleEditor.SetEnvironment(Settings(), "srv", "Sales", "Production");

            var rule = Assert.Single(result.Rules);
            Assert.Equal("srv", rule.Server);
            Assert.Equal("Sales", rule.Database);
            Assert.Equal("Production", rule.Environment);
        }

        [Fact]
        public void ChangesTheEnvironmentOfTheExactRuleAndKeepsItsOtherFields()
        {
            var settings = Settings(new ColorRule { Server = "SRV", Database = "sales", Environment = "Testing", AskBeforeChanges = true });

            var result = ColorRuleEditor.SetEnvironment(settings, "srv", "Sales", "Production");

            var rule = Assert.Single(result.Rules);
            Assert.Equal("Production", rule.Environment);
            Assert.True(rule.AskBeforeChanges);
            Assert.Equal("SRV", rule.Server);
        }

        [Fact]
        public void ChoosingTheSameEnvironmentRepeatedlyNeverDuplicatesTheRule()
        {
            var settings = Settings();
            for (var i = 0; i < 3; i++)
            {
                settings = ColorRuleEditor.SetEnvironment(settings, "srv", "Sales", "Production");
            }

            Assert.Single(settings.Rules);
        }

        [Fact]
        public void ABroaderRuleIsLeftAloneAndTheExactRuleIsAdded()
        {
            var settings = Settings(new ColorRule { Server = "srv*", Database = "", Environment = "Testing" });

            var result = ColorRuleEditor.SetEnvironment(settings, "srv", "Sales", "Production");

            Assert.Equal(2, result.Rules.Count);
            Assert.Equal("Testing", result.Rules[0].Environment);
            Assert.Equal("Production", EnvironmentMatcher.Find(result, "srv", "Sales")?.Name);
            Assert.Equal("Testing", EnvironmentMatcher.Find(result, "srv", "Other")?.Name);
        }

        [Fact]
        public void TheOriginalSettingsAreNotChanged()
        {
            var settings = Settings(new ColorRule { Server = "srv", Database = "Sales", Environment = "Testing" });

            ColorRuleEditor.SetEnvironment(settings, "srv", "Sales", "Production");

            Assert.Equal("Testing", settings.Rules.Single().Environment);
        }
    }
}
