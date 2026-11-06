using SqlPrompter.Core.Coloring;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class ColorCleanupTests
    {
        [Fact]
        public void EnvironmentsNoRuleUsesAreDroppedAndTheOriginalIsUntouched()
        {
            var settings = new ColorSettings
            {
                Environments =
                {
                    new ColorEnvironment { Name = "Production", Color = "#E5484D" },
                    new ColorEnvironment { Name = "Old", Color = "#000000" },
                },
                Rules = { new ColorRule { Server = "prod*", Environment = "production" } },
            };

            var cleaned = ColorSettingsValidator.WithoutUnusedEnvironments(settings);

            Assert.Single(cleaned.Environments);
            Assert.Equal("Production", cleaned.Environments[0].Name);
            Assert.Equal(2, settings.Environments.Count);
        }
    }
}
