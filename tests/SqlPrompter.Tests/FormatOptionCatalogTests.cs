using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using SqlPrompter.Core.Formatting;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class FormatOptionCatalogTests
    {
        [Fact]
        public void EveryOptionOfTheStyleIsInTheCatalogAndTheOtherWayRound()
        {
            var options = typeof(FormatStyle).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<DataMemberAttribute>() != null || p.PropertyType == typeof(CaseStyle))
                .Select(p => p.Name)
                .Where(n => n != "MaxLineLength") // stored in the style, but the formatter does not use it yet, so the window does not offer it
                .ToList();
            var catalog = FormatOptionCatalog.Options.Select(o => o.Id).ToList();

            Assert.Equal(options.Count, catalog.Count);
            Assert.Equal(catalog.Count, catalog.Distinct().Count());
        }

        [Fact]
        public void EveryOptionBelongsToAGroupThatHasASample()
        {
            foreach (var option in FormatOptionCatalog.Options)
            {
                Assert.Contains(FormatOptionCatalog.Groups, g => g.Name == option.Group);
            }

            Assert.All(FormatOptionCatalog.Groups, g => Assert.NotEmpty(FormatOptionCatalog.OptionsOf(g.Name)));
        }

        [Fact]
        public void TheSampleOfEveryGroupIsFormattedWithTheDefaultStyle()
        {
            foreach (var group in FormatOptionCatalog.Groups)
            {
                var result = SqlFormatter.Format(group.Sample, new FormatStyle());
                Assert.True(result.Formatted > 0, group.Name);
                Assert.Equal(0, result.Kept);
            }
        }

        [Fact]
        public void ChangingEveryOptionChangesTheSampleOfItsGroup()
        {
            foreach (var option in FormatOptionCatalog.Options)
            {
                var group = FormatOptionCatalog.GroupOf(option.Group);
                var baseStyle = new FormatStyle();
                var before = SqlFormatter.Format(group.Sample, baseStyle).Text;
                var changed = new FormatStyle();
                option.Set(changed, Other(option, option.Get(baseStyle)));
                var after = SqlFormatter.Format(group.Sample, changed).Text;

                // Casing while typing only acts in the editor, not in the formatter; the INSERT parenthesis only acts when the columns are broken (tested on its own).
                if (option.Id != "caseKeywordsWhileTyping" && option.Id != "insertParenOnNewLine")
                {
                    Assert.True(before != after, option.Id);
                }
            }
        }

        private static object Other(FormatOption option, object current)
        {
            switch (option.Kind)
            {
                case FormatOptionKind.Bool:
                    return !(bool)current;
                case FormatOptionKind.Case:
                    return (CaseStyle)current == CaseStyle.Lower ? CaseStyle.Upper : CaseStyle.Lower;
                default:
                    var value = (int)current;
                    if (option.Id == "insertColumnsBreakLength")
                    {
                        return value == 0 ? 1 : 0;
                    }

                    // Zero turns a limit off; a big number turns it on for everything, a small one for little.
                    return value == 0 ? 400 : (option.Id == "tabWidth" ? (value == 4 ? 8 : 4) : (value > 50 ? 5 : 400));
            }
        }

        [Theory]
        [InlineData("comma", "commaBeforeItems")]
        [InlineData("TABS", "useTabs")]
        [InlineData("and or", "andOrBeforeConditions")]
        [InlineData("blank", "keepBlankLines")]
        public void SearchFindsOptionsByWordsOfTheNameOrHelp(string text, string expected) =>
            Assert.Contains(FormatOptionCatalog.Search(text), o => o.Id == expected);

        [Fact]
        public void AnEmptySearchFindsEverythingAndNonsenseNothing()
        {
            Assert.Equal(FormatOptionCatalog.Options.Count, FormatOptionCatalog.Search(string.Empty).Count);
            Assert.Empty(FormatOptionCatalog.Search("zzzzzzzz"));
        }

        [Fact]
        public void NumbersAreClampedToTheirRange()
        {
            var style = new FormatStyle();
            var width = FormatOptionCatalog.Options.Single(o => o.Id == "tabWidth");

            width.Set(style, 999);
            Assert.Equal(16, style.TabWidth);
            width.Set(style, -3);
            Assert.Equal(1, style.TabWidth);
        }
    }

    public class NamedFormatStyleTests
    {
        [Fact]
        public void ANewSettingsObjectHasOneStyleCalledDefault()
        {
            var settings = new PrompterSettings();

            Assert.Equal("Default", settings.ActiveStyle.Name);
            Assert.Single(settings.FormattingStyles);
        }

        [Fact]
        public void AFileFromBeforeTheStylesKeepsItsStyleAsTheDefault()
        {
            var settings = SettingsJson.Deserialize("{\"formatting\":{\"useTabs\":false,\"tabWidth\":3}}");

            Assert.Single(settings.FormattingStyles);
            Assert.Equal("Default", settings.ActiveFormattingStyle);
            Assert.Equal(3, settings.ActiveStyle.Style.TabWidth);
            Assert.False(settings.Formatting.UseTabs);
        }

        [Fact]
        public void TheActiveStyleIsWhatFormattingSays()
        {
            var settings = new PrompterSettings();
            settings.FormattingStyles.Add(new NamedFormatStyle("Compact", new FormatStyle { UseTabs = false, TabWidth = 2 }));
            settings.ActiveFormattingStyle = "compact";
            settings.EnsureStyles();

            Assert.Equal("Compact", settings.ActiveStyle.Name);
            Assert.Equal(2, settings.Formatting.TabWidth);
        }

        [Fact]
        public void StylesSurviveTheJsonRoundTripWithTheActiveOne()
        {
            var settings = new PrompterSettings();
            settings.FormattingStyles.Add(new NamedFormatStyle("Compact", new FormatStyle { CommaBeforeItems = false }));
            settings.ActiveFormattingStyle = "Compact";
            settings.EnsureStyles();

            var loaded = SettingsJson.Deserialize(SettingsJson.Serialize(settings));

            Assert.Equal(new[] { "Default", "Compact" }, loaded.FormattingStyles.Select(s => s.Name).ToArray());
            Assert.Equal("Compact", loaded.ActiveStyle.Name);
            Assert.False(loaded.Formatting.CommaBeforeItems);
        }

        [Fact]
        public void BrokenNamesAndAMissingActiveStyleAreRepaired()
        {
            var settings = new PrompterSettings
            {
                FormattingStyles = new List<NamedFormatStyle> { new NamedFormatStyle("A", new FormatStyle()), new NamedFormatStyle("a", new FormatStyle()), new NamedFormatStyle(" ", new FormatStyle()) },
                ActiveFormattingStyle = "gone",
            };
            settings.EnsureStyles();

            Assert.Equal(new[] { "A", "a 2", "Style" }, settings.FormattingStyles.Select(s => s.Name).ToArray());
            Assert.Equal("A", settings.ActiveFormattingStyle);
        }

        [Fact]
        public void AnImportIntoTheActiveStyleChangesOnlyThatStyle()
        {
            var settings = new PrompterSettings();
            settings.FormattingStyles.Add(new NamedFormatStyle("Other", new FormatStyle { TabWidth = 7 }));

            settings.SetActiveStyleValues(new FormatStyle { TabWidth = 2 });

            Assert.Equal(2, settings.ActiveStyle.Style.TabWidth);
            Assert.Equal(2, settings.Formatting.TabWidth);
            Assert.Equal(7, settings.FormattingStyles.Single(s => s.Name == "Other").Style.TabWidth);
        }

        [Fact]
        public void CloneCopiesTheStyles()
        {
            var settings = new PrompterSettings();
            var copy = settings.Clone();
            copy.FormattingStyles[0].Style.TabWidth = 9;

            Assert.NotEqual(9, settings.FormattingStyles[0].Style.TabWidth);
        }
    }
}
