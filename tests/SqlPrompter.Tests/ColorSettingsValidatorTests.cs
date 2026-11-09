using System;
using System.Collections.Generic;
using SqlPrompter.Core.Coloring;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class ColorSettingsValidatorTests
    {
        private static ColorSettings Valid() => new ColorSettings
        {
            Environments = new List<ColorEnvironment> { new ColorEnvironment { Name = "Production", Color = "#E5484D" } },
            Rules = new List<ColorRule> { new ColorRule { Server = "prod*", Database = "", Environment = "Production" } },
        };

        [Fact]
        public void ValidSettingsHaveNoProblems() => Assert.Empty(ColorSettingsValidator.Validate(Valid()));

        [Fact]
        public void EmptyDefaultsAreValid() => Assert.Empty(ColorSettingsValidator.Validate(new ColorSettings()));

        [Fact]
        public void EnvironmentProblemsAreNamed()
        {
            var settings = Valid();
            settings.Environments.Add(new ColorEnvironment { Name = " ", Color = "#112233" });
            settings.Environments.Add(new ColorEnvironment { Name = "production", Color = "#112233" });
            settings.Environments.Add(new ColorEnvironment { Name = "Test", Color = "red" });

            var problems = ColorSettingsValidator.Validate(settings);

            Assert.Contains("Environment 2 has no name.", problems);
            Assert.Contains("The environment name \"production\" is used more than once.", problems);
            Assert.Contains("The environment \"Test\" has no valid color.", problems);
        }

        [Fact]
        public void RuleProblemsAreNamed()
        {
            var settings = Valid();
            settings.Rules.Add(new ColorRule { Server = "", Environment = "Production" });
            settings.Rules.Add(new ColorRule { Server = "x", Environment = "Missing" });
            settings.Rules.Add(new ColorRule { Server = "y", Environment = "" });

            var problems = ColorSettingsValidator.Validate(settings);

            Assert.DoesNotContain(problems, p => p.StartsWith("Rule 2 ", StringComparison.Ordinal));
            Assert.Contains("Rule 3 uses the environment \"Missing\", which does not exist.", problems);
            Assert.Contains("Rule 4 has no environment.", problems);
        }

        [Theory]
        [InlineData("#e5484d", "#E5484D")]
        [InlineData("E5484D", "#E5484D")]
        [InlineData("nope", null)]
        [InlineData(null, null)]
        public void ColorsAreNormalized(string input, string expected) => Assert.Equal(expected, ColorSettingsValidator.Normalize(input));
    }
}
