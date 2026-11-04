using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Settings;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void DefaultsMatchTheUsersCommitKeys()
        {
            var behavior = new PrompterSettings().Behavior;

            Assert.True(behavior.SuggestionsEnabled);
            Assert.True(behavior.CommitOnSpace);
            Assert.False(behavior.CommitOnDot);
            Assert.False(behavior.CommitOnComma);
            Assert.False(behavior.CommitOnParentheses);
            Assert.False(behavior.CommitOnSemicolon);
            Assert.False(behavior.CommitOnClosingBracket);
            Assert.Equal(KeywordCase.MatchTyped, behavior.KeywordCase);
            Assert.True(behavior.ToCommitKeys().Commits(' '));
            Assert.False(behavior.ToCommitKeys().Commits('.'));
        }

        [Fact]
        public void JsonRoundTripKeepsEverything()
        {
            var settings = new PrompterSettings();
            settings.Behavior.SuggestionsEnabled = false;
            settings.Behavior.CommitOnDot = true;
            settings.Behavior.CommitOnSpace = false;
            settings.Behavior.KeywordCase = KeywordCase.Lower;

            var json = SettingsJson.Serialize(settings);
            var loaded = SettingsJson.Deserialize(json);

            Assert.Matches(@"""keywordCase""\s*:\s*""Lower""", json);
            Assert.False(loaded.Behavior.SuggestionsEnabled);
            Assert.True(loaded.Behavior.CommitOnDot);
            Assert.False(loaded.Behavior.CommitOnSpace);
            Assert.Equal(KeywordCase.Lower, loaded.Behavior.KeywordCase);
        }

        [Fact]
        public void MissingMembersKeepTheirDefaults()
        {
            var loaded = SettingsJson.Deserialize("{ \"behavior\": { \"commitOnDot\": true } }");

            Assert.True(loaded.Behavior.CommitOnDot);
            Assert.True(loaded.Behavior.CommitOnSpace);
            Assert.True(loaded.Behavior.SuggestionsEnabled);
        }

        [Fact]
        public void NullBehaviorGivesDefaults()
        {
            Assert.True(SettingsJson.Deserialize("{ \"behavior\": null }").Behavior.CommitOnSpace);
        }

        [Fact]
        public void InvalidJsonIsAFormatError()
        {
            Assert.Throws<FormatException>(() => SettingsJson.Deserialize("not json"));
        }

        [Fact]
        public void SaveAndLoadThroughAFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "sqlprompter-test-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, "settings.json");
            try
            {
                Assert.True(SettingsJson.Load(path).Behavior.CommitOnSpace);
                var settings = new PrompterSettings();
                settings.Behavior.CommitOnComma = true;

                SettingsJson.Save(path, settings);
                SettingsJson.Save(path, settings);

                Assert.True(SettingsJson.Load(path).Behavior.CommitOnComma);
                Assert.False(File.Exists(path + ".tmp"));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Fact]
        public void ImportsCommitKeysFromASqlPromptExport()
        {
            var export = Export(
                ("CandidateListCompleteEnter", "true"),
                ("CandidateListCompleteSpace", "false"),
                ("CandidateListCompleteTab", "true"),
                ("CandidateListCompleteDot", "true"),
                ("CandidateListCompleteComma", "false"),
                ("CandidateListCompleteSquareBrackets", "true"),
                ("CandidateListCompleteParentheses", "false"),
                ("CandidateListCompleteSemicolon", "true"),
                ("SuggestionsEnabledOnStartUpSSMS", "false"));

            var result = SettingsImporter.ImportSqlPrompt(new MemoryStream(export), new PrompterSettings());

            var b = result.Settings.Behavior;
            Assert.False(b.CommitOnSpace);
            Assert.True(b.CommitOnDot);
            Assert.False(b.CommitOnComma);
            Assert.True(b.CommitOnClosingBracket);
            Assert.False(b.CommitOnParentheses);
            Assert.True(b.CommitOnSemicolon);
            Assert.False(b.SuggestionsEnabled);
            Assert.Equal(7, result.Applied.Count);
            Assert.Empty(result.Notes);
        }

        [Fact]
        public void ImportKeepsSettingsThatTheExportLacks()
        {
            var current = new PrompterSettings();
            current.Behavior.KeywordCase = KeywordCase.Upper;

            var result = SettingsImporter.ImportSqlPrompt(new MemoryStream(Export(("CandidateListCompleteDot", "true"))), current);

            Assert.Equal(KeywordCase.Upper, result.Settings.Behavior.KeywordCase);
            Assert.False(current.Behavior.CommitOnDot);
        }

        [Fact]
        public void DisabledEnterAndTabAreReportedNotApplied()
        {
            var result = SettingsImporter.ImportSqlPrompt(new MemoryStream(Export(("CandidateListCompleteEnter", "false"))), new PrompterSettings());

            Assert.Contains(result.Notes, n => n.Contains("Enter and Tab"));
        }

        [Fact]
        public void ImportFileDetectsTheFormat()
        {
            var dir = Path.Combine(Path.GetTempPath(), "sqlprompter-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var zip = Path.Combine(dir, "Prompt.settings");
                File.WriteAllBytes(zip, Export(("CandidateListCompleteComma", "true")));
                var json = Path.Combine(dir, "settings.json");
                File.WriteAllText(json, "{ \"behavior\": { \"commitOnDot\": true } }");

                Assert.True(SettingsImporter.ImportFile(zip, new PrompterSettings()).Settings.Behavior.CommitOnComma);
                Assert.True(SettingsImporter.ImportFile(json, new PrompterSettings()).Settings.Behavior.CommitOnDot);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ZipWithoutUiOptionsIsRejected()
        {
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    archive.CreateEntry("Other.xml");
                }

                Assert.Throws<FormatException>(() => SettingsImporter.ImportSqlPrompt(new MemoryStream(stream.ToArray()), new PrompterSettings()));
            }
        }

        [Fact]
        public void KeywordCaseCanBeForced()
        {
            var context = Analyze("SELECT * FROM dbo.T t |");
            var typed = SuggestionBuilder.Build(Analyze("sel|"), null);

            var upper = SuggestionBuilder.Build(Analyze("sel|"), null, null, KeywordCase.Upper);
            var lower = SuggestionBuilder.Build(Analyze("SEL|"), null, null, KeywordCase.Lower);

            Assert.Contains(typed, i => i.InsertText == "select");
            Assert.Contains(upper, i => i.InsertText == "SELECT");
            Assert.DoesNotContain(upper, i => i.InsertText == "select");
            Assert.Contains(lower, i => i.InsertText == "select");
            Assert.NotNull(context);
        }

        private static byte[] Export(params (string Name, string Value)[] options)
        {
            var xml = new StringBuilder("<UIOptions version=\"1\" type=\"UIOptions\">");
            foreach (var (name, value) in options)
            {
                xml.Append('<').Append(name).Append('>').Append(value).Append("</").Append(name).Append('>');
            }

            xml.Append("</UIOptions>");
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry("RedGate_SQLPrompt_CommonUI_Options_UIOptions.xml").Open()))
                    {
                        writer.Write(xml.ToString());
                    }
                }

                return stream.ToArray();
            }
        }
    }
}
