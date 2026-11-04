using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    public class SnippetTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "sqlprompter-snippets-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        // Sample data of our own, in the structure of SQL Prompt's files.
        private const string SampleJson = @"{
  ""id"": ""11111111-2222-3333-4444-555555555555"",
  ""prefix"": ""addcol"",
  ""description"": ""Add column"",
  ""body"": ""ALTER TABLE $schema_name$.$table_name$ ADD [$column_name$] $data_type$ NULL\r\nGO\r\n\r\n$CURSOR$"",
  ""placeholders"": [
    { ""name"": ""schema_name"", ""defaultValue"": ""[schema_name]"" },
    { ""name"": ""data_type"", ""defaultValue"": ""INT"" }
  ]
}";

        private const string SampleXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<CodeSnippets>
  <CodeSnippet Format=""1.0.0"">
    <Header>
      <Title>AddColumn</Title>
      <Shortcut>addcol</Shortcut>
      <Description>Add column</Description>
      <Author />
      <SnippetTypes><SnippetType>Expansion</SnippetType></SnippetTypes>
    </Header>
    <Snippet>
      <Declarations>
        <Literal><ID>schema_name</ID><ToolTip /><Default>[schema_name]</Default></Literal>
        <Literal><ID>column_name</ID><ToolTip /><Default /></Literal>
      </Declarations>
      <Code Language=""sql""><![CDATA[ALTER TABLE $schema_name$.t ADD [$column_name$] INT
GO
$CURSOR$]]></Code>
    </Snippet>
  </CodeSnippet>
</CodeSnippets>";

        [Fact]
        public void ReadsTheJsonOfSqlPrompt()
        {
            var snippet = SnippetStore.Read(SampleJson);

            Assert.Equal("11111111-2222-3333-4444-555555555555", snippet.Id);
            Assert.Equal("addcol", snippet.Prefix);
            Assert.Equal("Add column", snippet.Description);
            Assert.StartsWith("ALTER TABLE $schema_name$", snippet.Body);
            Assert.EndsWith("$CURSOR$", snippet.Body);
            Assert.Equal(new[] { "schema_name", "data_type" }, snippet.Placeholders.Select(p => p.Name).ToArray());
            Assert.Equal("INT", snippet.Placeholders[1].DefaultValue);
        }

        [Fact]
        public void MissingPartsGetDefaults()
        {
            var snippet = SnippetStore.Read("{ \"prefix\": \"x\", \"body\": \"SELECT 1\" }");

            Assert.False(string.IsNullOrEmpty(snippet.Id));
            Assert.Equal(string.Empty, snippet.Description);
            Assert.Empty(snippet.Placeholders);
        }

        [Fact]
        public void WriteAndReadRoundTrip()
        {
            var snippet = Snippet.Create("sel", "Select top", "SELECT TOP 10 * FROM $table$\r\n\t$CURSOR$", new[] { new SnippetPlaceholder { Name = "table", DefaultValue = "dbo.T" } });

            var read = SnippetStore.Read(SnippetStore.Write(snippet));

            Assert.Equal(snippet.Id, read.Id);
            Assert.Equal(snippet.Body, read.Body);
            Assert.Equal("dbo.T", read.Placeholders.Single().DefaultValue);
        }

        [Fact]
        public void EmptyPlaceholdersAreNotWritten()
        {
            Assert.DoesNotContain("placeholders", SnippetStore.Write(Snippet.Create("x", "", "SELECT 1")));
        }

        [Fact]
        public void InvalidJsonIsAFormatError()
        {
            Assert.Throws<FormatException>(() => SnippetStore.Read("not json"));
        }

        [Fact]
        public void XmlBackupBecomesASnippet()
        {
            var snippet = Assert.Single(SnippetXml.Parse(SampleXml));

            Assert.Equal("addcol", snippet.Prefix);
            Assert.Equal("Add column", snippet.Description);
            Assert.Equal("ALTER TABLE $schema_name$.t ADD [$column_name$] INT\nGO\n$CURSOR$".Replace("\n", Environment.NewLine == "\r\n" ? "\n" : "\n"), snippet.Body.Replace("\r\n", "\n"));
            Assert.Equal(new[] { "schema_name", "column_name" }, snippet.Placeholders.Select(p => p.Name).ToArray());
            Assert.Equal("[schema_name]", snippet.Placeholders[0].DefaultValue);
            Assert.Equal(string.Empty, snippet.Placeholders[1].DefaultValue);
            Assert.True(Guid.TryParse(snippet.Id, out _));
        }

        [Fact]
        public void XmlWithoutShortcutUsesTheTitleAndSeveralSnippetsAreRead()
        {
            const string xml = "<CodeSnippets>" +
                "<CodeSnippet><Header><Title>One</Title></Header><Snippet><Code><![CDATA[SELECT 1]]></Code></Snippet></CodeSnippet>" +
                "<CodeSnippet><Header><Title>Two</Title><Shortcut>t2</Shortcut></Header><Snippet><Code><![CDATA[SELECT 2]]></Code></Snippet></CodeSnippet>" +
                "</CodeSnippets>";

            var snippets = SnippetXml.Parse(xml);

            Assert.Equal(new[] { "One", "t2" }, snippets.Select(s => s.Prefix).ToArray());
        }

        [Theory]
        [InlineData("not xml")]
        [InlineData("<CodeSnippets></CodeSnippets>")]
        [InlineData("<CodeSnippets><CodeSnippet><Header><Title>x</Title></Header></CodeSnippet></CodeSnippets>")]
        public void NoSnippetInTheFileIsAFormatError(string xml)
        {
            Assert.Throws<FormatException>(() => SnippetXml.Parse(xml));
        }

        [Fact]
        public void FolderIsLoadedSortedAndSubfoldersAreIgnored()
        {
            var store = new SnippetStore(folder);
            store.Save(Snippet.Create("zz", "last", "SELECT 3"));
            store.Save(Snippet.Create("AA", "first", "SELECT 1"));
            Directory.CreateDirectory(Path.Combine(folder, "backup"));
            File.WriteAllText(Path.Combine(folder, "backup", "old.json"), SampleJson);

            var result = store.Load();

            Assert.Equal(new[] { "AA", "zz" }, result.Snippets.Select(s => s.Prefix).ToArray());
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void AMissingFolderIsEmptyAndABrokenFileIsReportedWhileTheRestLoads()
        {
            Assert.Empty(new SnippetStore(Path.Combine(folder, "nope")).Load().Snippets);

            var store = new SnippetStore(folder);
            store.Save(Snippet.Create("ok", "", "SELECT 1"));
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{ nope");
            File.WriteAllText(Path.Combine(folder, "noprefix.json"), "{ \"body\": \"x\" }");

            var result = store.Load();

            Assert.Equal("ok", Assert.Single(result.Snippets).Prefix);
            Assert.Equal(2, result.Errors.Count);
            Assert.Contains(result.Errors, e => e.StartsWith("broken.json"));
        }

        [Fact]
        public void FilesAreNamedLikeSqlPromptDoes()
        {
            var snippet = Snippet.Create("a/b:c", "", "SELECT 1");

            var path = new SnippetStore(folder).Save(snippet);

            Assert.Equal("a_b_c-" + snippet.Id + ".json", Path.GetFileName(path));
        }

        [Fact]
        public void SavingAgainReplacesTheFileAndDeleteRemovesIt()
        {
            var store = new SnippetStore(folder);
            var snippet = Snippet.Create("one", "", "SELECT 1");
            store.Save(snippet);
            snippet.Prefix = "renamed";
            store.Save(snippet);

            Assert.Equal("renamed", Assert.Single(store.Load().Snippets).Prefix);
            Assert.Single(Directory.GetFiles(folder));
            Assert.True(store.Delete(snippet.Id));
            Assert.False(store.Delete(snippet.Id));
            Assert.Empty(store.Load().Snippets);
        }

        [Fact]
        public void DeleteDoesNotTouchFilesWithOnlyASimilarId()
        {
            var store = new SnippetStore(folder);
            var a = Snippet.Create("a", "", "SELECT 1");
            a.Id = "1";
            var b = Snippet.Create("b", "", "SELECT 2");
            b.Id = "x1";
            store.Save(a);
            store.Save(b);

            store.Delete("1");

            Assert.Equal("b", Assert.Single(store.Load().Snippets).Prefix);
        }

        [Fact]
        public void ImportWritesTheXmlBackupsAsJsonAndSkipsExistingPrefixes()
        {
            var store = new SnippetStore(Path.Combine(folder, "store"));
            var source = Path.Combine(folder, "AddColumn.sqlpromptsnippet");
            Directory.CreateDirectory(folder);
            File.WriteAllText(source, SampleXml);

            var first = store.Import(new[] { source }, replaceSamePrefix: false);
            var second = store.Import(new[] { source }, replaceSamePrefix: false);

            Assert.Equal(1, first.Imported);
            Assert.Equal(0, second.Imported);
            Assert.Equal(1, second.Skipped);
            Assert.Equal("addcol", Assert.Single(store.Load().Snippets).Prefix);
        }

        [Fact]
        public void ImportCanReplaceAndKeepsTheIdOfTheReplacedSnippet()
        {
            var store = new SnippetStore(Path.Combine(folder, "store"));
            var original = Snippet.Create("addcol", "old", "SELECT 0");
            store.Save(original);
            var source = Path.Combine(folder, "AddColumn.sqlpromptsnippet");
            File.WriteAllText(source, SampleXml);

            var result = store.Import(new[] { source }, replaceSamePrefix: true);

            Assert.Equal(1, result.Replaced);
            var loaded = Assert.Single(store.Load().Snippets);
            Assert.Equal(original.Id, loaded.Id);
            Assert.Equal("Add column", loaded.Description);
        }

        [Fact]
        public void ImportReportsBrokenFilesAndContinues()
        {
            var store = new SnippetStore(Path.Combine(folder, "store"));
            Directory.CreateDirectory(folder);
            var bad = Path.Combine(folder, "bad.sqlpromptsnippet");
            var good = Path.Combine(folder, "good.json");
            File.WriteAllText(bad, "<nope");
            File.WriteAllText(good, SampleJson);

            var result = store.Import(new[] { bad, good }, replaceSamePrefix: false);

            Assert.Equal(1, result.Imported);
            Assert.Single(result.Errors);
            Assert.StartsWith("bad.sqlpromptsnippet", result.Errors[0]);
        }

        [Fact]
        public void SnippetFolderSettingSurvivesTheJsonRoundTrip()
        {
            var settings = new PrompterSettings();
            settings.Snippets.Folder = @"D:\Snippets";

            var loaded = SettingsJson.Deserialize(SettingsJson.Serialize(settings));

            Assert.Equal(@"D:\Snippets", loaded.Snippets.Folder);
            Assert.Equal(string.Empty, SettingsJson.Deserialize("{}").Snippets.Folder);
        }
    }
}
