using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Formatting;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>An imported SQL Prompt style is added to the list of styles (#304).</summary>
    public class StyleImportIntoTests
    {
        private const string Style = @"{ ""metadata"": { ""id"": ""x"", ""name"": ""Team style"" }, ""casing"": { ""reservedKeywords"": ""lowercase"" } }";

        private static List<NamedFormatStyle> OneStyle() => new List<NamedFormatStyle> { new NamedFormatStyle("Default", new FormatStyle()) };

        [Fact]
        public void ImportingAddsAStyleToTheListAndLeavesTheOthersAlone()
        {
            var styles = OneStyle();
            var before = styles[0].Style.KeywordCase;

            var imported = SqlPromptStyleImporter.ImportInto(styles, Style, "team.json", out var result);

            Assert.Equal(2, styles.Count);
            Assert.Same(imported, styles[1]);
            Assert.Equal("Team style", imported.Name);
            Assert.Equal(CaseStyle.Lower, imported.Style.KeywordCase);
            Assert.Equal(before, styles[0].Style.KeywordCase);
            Assert.Contains("keywordCase = Lower", result.Applied);
        }

        [Fact]
        public void TheNameComesFromTheFileWhenTheMetadataHasNone()
        {
            var styles = OneStyle();

            var imported = SqlPromptStyleImporter.ImportInto(styles, @"{ ""casing"": { ""reservedKeywords"": ""uppercase"" } }", @"C:\x\my style.json", out _);

            Assert.Equal("my style", imported.Name);
        }

        [Fact]
        public void ATakenNameGetsANumber()
        {
            var styles = OneStyle();

            SqlPromptStyleImporter.ImportInto(styles, Style, "a.json", out _);
            var second = SqlPromptStyleImporter.ImportInto(styles, Style, "a.json", out _);
            var third = SqlPromptStyleImporter.ImportInto(styles, Style, "a.json", out _);

            Assert.Equal(new[] { "Default", "Team style", "Team style 2", "Team style 3" }, styles.Select(s => s.Name).ToArray());
            Assert.Equal("Team style 3", third.Name);
            Assert.NotSame(second, third);
        }

        [Fact]
        public void TheNameComparisonIgnoresCase()
        {
            var styles = new List<NamedFormatStyle> { new NamedFormatStyle("TEAM STYLE", new FormatStyle()) };

            Assert.Equal("Team style 2", SqlPromptStyleImporter.ImportInto(styles, Style, "a.json", out _).Name);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("[1, 2, 3]")]
        [InlineData("{}")]
        [InlineData(@"{ ""name"": ""x"", ""version"": 1 }")]
        public void WhatIsNotAStyleChangesNothing(string text)
        {
            var styles = OneStyle();

            Assert.Throws<FormatException>(() => SqlPromptStyleImporter.ImportInto(styles, text, "x.json", out _));
            Assert.Single(styles);
        }

        [Fact]
        public void TheImportedStyleSurvivesSavingIntoTheSettings()
        {
            var settings = new PrompterSettings();
            var styles = settings.FormattingStyles.Select(s => s.Clone()).ToList();
            var imported = SqlPromptStyleImporter.ImportInto(styles, Style, "a.json", out _);

            settings.FormattingStyles = styles;
            settings.ActiveFormattingStyle = imported.Name;
            settings.EnsureStyles();

            Assert.Equal(new[] { "Default", "Team style" }, settings.FormattingStyles.Select(s => s.Name).ToArray());
            Assert.Equal("Team style", settings.ActiveStyle.Name);
            Assert.Equal(CaseStyle.Lower, settings.Formatting.KeywordCase);
        }
    }
}
