using SqlPrompter.Core.Coloring;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class ColoringTests
    {
        private static ColorSettings Sample() => new ColorSettings
        {
            Environments =
            {
                new ColorEnvironment { Name = "Production", Color = "#E5484D" },
                new ColorEnvironment { Name = "Test", Color = "#30A46C" },
            },
            Rules =
            {
                new ColorRule { Server = "prod-*", Environment = "Production" },
                new ColorRule { Server = "localhost*", Database = "Sales_Test?", Environment = "Test" },
                new ColorRule { Server = "localhost*", Environment = "Missing" },
            },
        };

        [Fact]
        public void WildcardServerMatchesCaseInsensitively()
        {
            Assert.Equal("Production", EnvironmentMatcher.Find(Sample(), "PROD-sql01", "master").Name);
        }

        [Fact]
        public void ServerAndDatabaseMustBothMatch()
        {
            Assert.Equal("Test", EnvironmentMatcher.Find(Sample(), "localhost,1433", "Sales_Test1").Name);
            Assert.Null(EnvironmentMatcher.Find(Sample(), "localhost,1433", "Sales_Test12"));
        }

        [Fact]
        public void RuleWithUnknownEnvironmentIsSkipped()
        {
            Assert.Null(EnvironmentMatcher.Find(Sample(), "localhost", "master"));
        }

        [Fact]
        public void DisabledColoringFindsNothing()
        {
            var settings = Sample();
            settings.Enabled = false;
            Assert.Null(EnvironmentMatcher.Find(settings, "prod-sql01", "master"));
        }

        [Fact]
        public void SpecialRegexCharactersAreLiteral()
        {
            Assert.True(EnvironmentMatcher.Matches("srv.example", "srv.example"));
            Assert.False(EnvironmentMatcher.Matches("srv.example", "srvXexample"));
            Assert.True(EnvironmentMatcher.Matches("(local)", "(local)"));
        }

        [Fact]
        public void ColorsParse()
        {
            Assert.True(EnvironmentMatcher.TryParseColor("#E5484D", out var r, out var g, out var b));
            Assert.Equal(0xE5, r);
            Assert.Equal(0x48, g);
            Assert.Equal(0x4D, b);
            Assert.False(EnvironmentMatcher.TryParseColor("red", out _, out _, out _));
        }

        [Fact]
        public void ColoringSurvivesTheSettingsFile()
        {
            var settings = new PrompterSettings { Coloring = Sample() };
            var loaded = SettingsJson.Deserialize(SettingsJson.Serialize(settings));
            Assert.Equal(2, loaded.Coloring.Environments.Count);
            Assert.Equal("Sales_Test?", loaded.Coloring.Rules[1].Database);
            Assert.True(SettingsJson.Deserialize("{}").Coloring.Enabled);
        }
    }
}
