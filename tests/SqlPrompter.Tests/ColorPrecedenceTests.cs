using System.Linq;
using SqlPrompter.Core.Coloring;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class ColorPrecedenceTests
    {
        private static ColorSettings With(params ColorRule[] rules)
        {
            var settings = new ColorSettings();
            foreach (var name in rules.Select(r => r.Environment).Distinct())
            {
                settings.Environments.Add(new ColorEnvironment { Name = name, Color = "#112233" });
            }

            foreach (var rule in rules)
            {
                settings.Rules.Add(rule);
            }

            return settings;
        }

        private static string Pick(string server, string database, params ColorRule[] rules) =>
            EnvironmentMatcher.Find(With(rules), server, database)?.Name;

        private static ColorRule Rule(string server, string database, string environment) =>
            new ColorRule { Server = server, Database = database, Environment = environment };

        [Fact]
        public void ARuleWithADatabaseBeatsARuleForTheWholeServerInAnyOrder()
        {
            var whole = Rule("srv", "", "Server");
            var db = Rule("srv", "Sales", "Database");

            Assert.Equal("Database", Pick("srv", "Sales", whole, db));
            Assert.Equal("Database", Pick("srv", "Sales", db, whole));
        }

        [Fact]
        public void AServerWithoutWildcardsBeatsOneWithWildcardsInAnyOrder()
        {
            var exact = Rule("prod-sql01", "", "Exact");
            var wild = Rule("prod-*", "", "Wild");

            Assert.Equal("Exact", Pick("prod-sql01", "x", wild, exact));
            Assert.Equal("Exact", Pick("prod-sql01", "x", exact, wild));
        }

        [Fact]
        public void TheLongerLiteralPatternBeatsTheShorterOne()
        {
            var shortPattern = Rule("prod*", "", "Short");
            var longPattern = Rule("prod-sql*", "", "Long");

            Assert.Equal("Long", Pick("prod-sql01", "x", shortPattern, longPattern));
            Assert.Equal("Long", Pick("prod-sql01", "x", longPattern, shortPattern));
        }

        [Fact]
        public void EqualRulesAreDecidedByTheNameNotByTheOrder()
        {
            var b = Rule("srv*", "", "B");
            var a = Rule("srv*", "", "A");

            Assert.Equal("A", Pick("srv1", "x", b, a));
            Assert.Equal("A", Pick("srv1", "x", a, b));
        }

        [Fact]
        public void ARuleWithAnUnknownEnvironmentDoesNotWin()
        {
            var settings = With(Rule("srv*", "", "Real"));
            settings.Rules.Add(Rule("srv1", "Db", "Missing"));

            Assert.Equal("Real", EnvironmentMatcher.Find(settings, "srv1", "Db").Name);
        }
    }
}
