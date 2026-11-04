using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Settings;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class AliasTests
    {
        private static string Alias(string name, params string[] used) => AliasGenerator.Generate(name, used, new AliasSettings());

        [Theory]
        [InlineData("OrderItems", "oi")]
        [InlineData("Users", "u")]
        [InlineData("users", "u")]
        [InlineData("Order Details", "od")]
        [InlineData("order_items", "oi")]
        [InlineData("SubjektCrm", "sc")]
        [InlineData("HTTPLog", "hl")]
        [InlineData("Table2", "t")]
        [InlineData("123", "t")]
        public void AliasIsMadeOfTheInitials(string name, string expected)
        {
            Assert.Equal(expected, Alias(name));
        }

        [Fact]
        public void CollisionsGetANumber()
        {
            Assert.Equal("u1", Alias("Users", "u"));
            Assert.Equal("u2", Alias("Users", "U", "u1"));
        }

        [Fact]
        public void ReservedWordsAreNotUsedAsAliases()
        {
            // OrderNumber would give "on".
            Assert.Equal("on1", Alias("OrderNumber"));
        }

        [Fact]
        public void CustomAliasWinsAndIsInsertedAsWritten()
        {
            var settings = new AliasSettings { CapitaliseGeneratedAliases = true };
            settings.CustomAliases.Add(new AliasRule { Name = "orderitems", Alias = "oit" });

            Assert.Equal("oit", AliasGenerator.Generate("OrderItems", new string[0], settings));
            Assert.Equal("oit1", AliasGenerator.Generate("OrderItems", new[] { "oit" }, settings));
        }

        [Fact]
        public void IgnoredPrefixesAreDropped()
        {
            var settings = new AliasSettings();
            settings.IgnoredPrefixes.Add("tbl");
            settings.IgnoredPrefixes.Add("tbl_");

            Assert.Equal("oi", AliasGenerator.Generate("tbl_OrderItems", new string[0], settings));
            Assert.Equal("t", AliasGenerator.Generate("tbl", new string[0], settings));
        }

        [Fact]
        public void GeneratedAliasesCanBeCapitalised()
        {
            Assert.Equal("OI", AliasGenerator.Generate("OrderItems", new string[0], new AliasSettings { CapitaliseGeneratedAliases = true }));
        }

        [Fact]
        public void ImportsAliasOptionsFromASqlPromptExport()
        {
            // The custom aliases are escaped XML inside an element, as in a real export.
            const string engine =
                "<EngineOptions version=\"1\" type=\"EngineOptions\">" +
                "<AliasTableViewNames>false</AliasTableViewNames>" +
                "<IncludeAsInAliasDefinition>false</IncludeAsInAliasDefinition>" +
                "<CapitaliseGeneratedAliases>true</CapitaliseGeneratedAliases>" +
                "<QualifyObjectNames>false</QualifyObjectNames>" +
                "<QualifyColumnNamesWithAliases>false</QualifyColumnNamesWithAliases>" +
                "<UserDefinedAliases>&lt;UserDefinedAliases&gt;&lt;Count&gt;2&lt;/Count&gt;" +
                "&lt;UserDefinedAlias&gt;&lt;ObjectName&gt;OrderItems&lt;/ObjectName&gt;&lt;Alias&gt;oit&lt;/Alias&gt;&lt;/UserDefinedAlias&gt;" +
                "&lt;UserDefinedAlias&gt;&lt;ObjectName&gt;Users&lt;/ObjectName&gt;&lt;Alias&gt;usr&lt;/Alias&gt;&lt;/UserDefinedAlias&gt;" +
                "&lt;/UserDefinedAliases&gt;</UserDefinedAliases>" +
                "<IgnoredPrefixes>&lt;IgnoredPrefixes&gt;&lt;Count&gt;1&lt;/Count&gt;&lt;/IgnoredPrefixes&gt;</IgnoredPrefixes>" +
                "</EngineOptions>";
            byte[] zip;
            using (var stream = new System.IO.MemoryStream())
            {
                using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
                using (var writer = new System.IO.StreamWriter(archive.CreateEntry("RedGate_SqlPrompt_Engine_EngineOptions.xml").Open()))
                {
                    writer.Write(engine);
                }

                zip = stream.ToArray();
            }

            var result = SettingsImporter.ImportSqlPrompt(new System.IO.MemoryStream(zip), new PrompterSettings());

            var a = result.Settings.Aliases;
            Assert.False(a.AddAliases);
            Assert.False(a.IncludeAs);
            Assert.True(a.CapitaliseGeneratedAliases);
            Assert.False(a.QualifyObjectNames);
            Assert.False(a.QualifyColumnsWithAliases);
            Assert.Equal(new[] { "OrderItems=oit", "Users=usr" }, a.CustomAliases.Select(r => r.Name + "=" + r.Alias).ToArray());
            Assert.Contains(result.Notes, n => n.Contains("prefixes"));
        }

        [Fact]
        public void AliasSettingsSurviveTheJsonRoundTrip()
        {
            var settings = new PrompterSettings();
            settings.Aliases.IncludeAs = false;
            settings.Aliases.CustomAliases.Add(new AliasRule { Name = "Users", Alias = "usr" });
            settings.Aliases.IgnoredPrefixes.Add("tbl");

            var loaded = SettingsJson.Deserialize(SettingsJson.Serialize(settings)).Aliases;

            Assert.False(loaded.IncludeAs);
            Assert.True(loaded.AddAliases);
            Assert.Equal("usr", Assert.Single(loaded.CustomAliases).Alias);
            Assert.Equal("tbl", Assert.Single(loaded.IgnoredPrefixes));
            Assert.Empty(SettingsJson.Deserialize("{}").Aliases.CustomAliases);
        }

        private static SuggestionOptions Options(AliasSettings aliases = null, KeywordCase keywordCase = KeywordCase.MatchTyped) =>
            new SuggestionOptions(keywordCase, aliases);

        [Fact]
        public void InsertingATableAddsAnAlias()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), SampleDatabase(), null, Options());

            Assert.Equal("dbo.Users AS u", items.Single(i => i.DisplayText == "Users").InsertText);
            Assert.Equal("dbo.[Order Details] AS od", items.Single(i => i.DisplayText == "Order Details").InsertText);
            Assert.Equal("sales.Customers AS c", items.Single(i => i.DisplayText == "Customers").InsertText);
            Assert.Equal("sales", items.Single(i => i.DisplayText == "Customers").Suffix);
        }

        [Fact]
        public void AliasAvoidsAliasesAlreadyInTheQuery()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM dbo.Orders o JOIN |"), SampleDatabase(), null, Options());

            Assert.Equal("dbo.Orders AS o1", items.Single(i => i.DisplayText == "Orders").InsertText);
            Assert.Equal("dbo.Users AS u", items.Single(i => i.DisplayText == "Users").InsertText);
        }

        [Fact]
        public void AliasWithoutAsAndWithKeywordCase()
        {
            var noAs = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), SampleDatabase(), null, Options(new AliasSettings { IncludeAs = false }));
            var lower = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), SampleDatabase(), null, Options(null, KeywordCase.Lower));

            Assert.Equal("dbo.Users u", noAs.Single(i => i.DisplayText == "Users").InsertText);
            Assert.Equal("dbo.Users as u", lower.Single(i => i.DisplayText == "Users").InsertText);
        }

        [Fact]
        public void AliasCanBeTurnedOffAndSchemaOmitted()
        {
            var settings = new AliasSettings { AddAliases = false, QualifyObjectNames = false };

            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), SampleDatabase(), null, Options(settings));

            Assert.Equal("Users", items.Single(i => i.DisplayText == "Users").InsertText);
        }

        [Fact]
        public void AliasIsAlsoAddedAfterTheSchema()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM sales.|"), SampleDatabase(), null, Options());

            Assert.Equal("Customers AS c", Assert.Single(items).InsertText);
        }

        [Theory]
        [InlineData("INSERT INTO |")]
        [InlineData("UPDATE |")]
        [InlineData("DELETE FROM |")]
        [InlineData("TRUNCATE TABLE |")]
        [InlineData("SELECT * FROM |dbo.Users u")]
        [InlineData("SELECT * FROM |Users AS u")]
        public void NoAliasWhereNoneBelongs(string sql)
        {
            var items = SuggestionBuilder.Build(Analyze(sql), SampleDatabase(), null, Options());

            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Table && i.InsertText.Contains(" AS "));
        }

        [Fact]
        public void TableFunctionsGetNoAlias()
        {
            var db = SampleDatabase();
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), db, null, Options());

            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.TableFunction && i.InsertText.Contains(" AS "));
        }

        [Fact]
        public void ColumnsAreQualifiedWithTheAlias()
        {
            var qualified = SuggestionBuilder.Build(Analyze("SELECT | FROM dbo.Users u"), SampleDatabase(), null, Options());
            var plain = SuggestionBuilder.Build(Analyze("SELECT | FROM dbo.Users u"), SampleDatabase(), null,
                Options(new AliasSettings { QualifyColumnsWithAliases = false }));

            Assert.Equal("u.Email", qualified.Single(i => i.Kind == SuggestionKind.Column && i.DisplayText == "Email").InsertText);
            Assert.Equal("Email", plain.Single(i => i.Kind == SuggestionKind.Column && i.DisplayText == "Email").InsertText);
        }

        [Fact]
        public void AliasAllowedOnlyInTableSourcePositions()
        {
            Assert.True(Analyze("SELECT * FROM |").AliasAllowed);
            Assert.True(Analyze("SELECT * FROM dbo.A a JOIN |").AliasAllowed);
            Assert.True(Analyze("SELECT * FROM dbo.A a, |").AliasAllowed);
            Assert.False(Analyze("INSERT INTO |").AliasAllowed);
            Assert.False(Analyze("DELETE FROM |").AliasAllowed);
            Assert.False(Analyze("SELECT * FROM |dbo.A a").AliasAllowed);
        }
    }
}
