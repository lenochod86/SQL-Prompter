using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SqlPrompter.Core.Settings
{
    public sealed class SettingsImportResult
    {
        public SettingsImportResult(PrompterSettings settings, IReadOnlyList<string> applied, IReadOnlyList<string> notes)
        {
            Settings = settings;
            Applied = applied;
            Notes = notes;
        }

        public PrompterSettings Settings { get; }

        /// <summary>Human-readable list of what was taken from the file.</summary>
        public IReadOnlyList<string> Applied { get; }

        /// <summary>Things in the file that were deliberately not applied.</summary>
        public IReadOnlyList<string> Notes { get; }
    }

    /// <summary>
    /// Imports a settings file: our own JSON, or a SQL Prompt export (<c>.settings</c>, a ZIP with XML files).
    /// From SQL Prompt, only what exists here is taken: whether suggestions are on, the commit keys and the alias and qualification options.
    /// The rest follows with its own features.
    /// </summary>
    public static class SettingsImporter
    {
        /// <exception cref="FormatException">The file is neither our JSON nor a SQL Prompt export.</exception>
        public static SettingsImportResult ImportFile(string path, PrompterSettings current)
        {
            var bytes = File.ReadAllBytes(path);
            return IsZip(bytes)
                ? ImportSqlPrompt(new MemoryStream(bytes), current)
                : new SettingsImportResult(SettingsJson.Deserialize(Encoding.UTF8.GetString(bytes)), new[] { "All settings from the JSON file" }, new string[0]);
        }

        /// <exception cref="FormatException">The stream is not a SQL Prompt settings export.</exception>
        public static SettingsImportResult ImportSqlPrompt(Stream zip, PrompterSettings current)
        {
            XDocument ui;
            XDocument engine;
            try
            {
                using (var archive = new ZipArchive(zip, ZipArchiveMode.Read))
                {
                    ui = LoadEntry(archive, "UIOptions.xml");
                    engine = LoadEntry(archive, "EngineOptions.xml");
                }
            }
            catch (InvalidDataException ex)
            {
                throw new FormatException("The file is not a SQL Prompt settings export.", ex);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new FormatException("An options file in the export is not valid XML.", ex);
            }

            if (ui == null && engine == null)
            {
                throw new FormatException("The file is not a SQL Prompt settings export: UIOptions.xml and EngineOptions.xml are missing.");
            }

            var settings = current.Clone();
            var behavior = settings.Behavior;
            var aliases = settings.Aliases;
            var applied = new List<string>();
            var notes = new List<string>();

            void Take(XDocument document, string element, string description, Action<bool> set)
            {
                if (TryGetBool(document, element, out var value))
                {
                    set(value);
                    applied.Add(description + ": " + (value ? "on" : "off"));
                }
            }

            Take(ui, "SuggestionsEnabledOnStartUpSSMS", "Suggestions enabled", v => behavior.SuggestionsEnabled = v);
            Take(ui, "CandidateListCompleteSpace", "Commit on Space", v => behavior.CommitOnSpace = v);
            Take(ui, "CandidateListCompleteDot", "Commit on dot", v => behavior.CommitOnDot = v);
            Take(ui, "CandidateListCompleteComma", "Commit on comma", v => behavior.CommitOnComma = v);
            Take(ui, "CandidateListCompleteParentheses", "Commit on parentheses", v => behavior.CommitOnParentheses = v);
            Take(ui, "CandidateListCompleteSemicolon", "Commit on semicolon", v => behavior.CommitOnSemicolon = v);
            Take(ui, "CandidateListCompleteSquareBrackets", "Commit on ]", v => behavior.CommitOnClosingBracket = v);

            Take(engine, "AliasTableViewNames", "Add aliases to tables and views", v => aliases.AddAliases = v);
            Take(engine, "IncludeAsInAliasDefinition", "Include AS in alias definitions", v => aliases.IncludeAs = v);
            Take(engine, "CapitaliseGeneratedAliases", "Capitalise generated aliases", v => aliases.CapitaliseGeneratedAliases = v);
            Take(engine, "QualifyObjectNames", "Qualify object names with schema", v => aliases.QualifyObjectNames = v);
            Take(engine, "QualifyColumnNamesWithAliases", "Qualify column names with aliases", v => aliases.QualifyColumnsWithAliases = v);

            var joins = settings.Joins;
            Take(engine, "IncludePartialFKJoinConditions", "Join conditions for single columns of multi-column keys", v => joins.IncludePartialKeyConditions = v);
            Take(engine, "AlwaysIncludeNameMatchedJoinConditions", "Join conditions by matching column names", v => joins.NameMatchedConditions = v);
            Take(engine, "FlipColumnsOnJoinCondition", "New table on the left in join conditions (SQL Prompt: swap order of columns, which puts it on the right)", v => joins.NewTableOnLeft = !v);

            var custom = ReadCustomAliases(engine);
            if (custom != null)
            {
                aliases.CustomAliases = custom;
                applied.Add("Custom aliases: " + custom.Count);
            }

            if (CountOf(engine, "IgnoredPrefixes") > 0)
            {
                notes.Add("Ignored prefixes are not imported; add them in the options.");
            }

            if ((TryGetBool(ui, "CandidateListCompleteEnter", out var enter) && !enter) ||
                (TryGetBool(ui, "CandidateListCompleteTab", out var tab) && !tab))
            {
                notes.Add("Enter and Tab always insert the selected suggestion; turning them off is not supported.");
            }

            if (applied.Count == 0)
            {
                notes.Add("The export contains none of the supported settings.");
            }

            return new SettingsImportResult(settings, applied, notes);
        }

        private static XDocument LoadEntry(ZipArchive archive, string suffix)
        {
            var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                return null;
            }

            using (var reader = entry.Open())
            {
                return XDocument.Load(reader);
            }
        }

        /// <summary>The element holds escaped XML: &lt;UserDefinedAliases&gt;&lt;UserDefinedAlias&gt;&lt;ObjectName&gt;…</summary>
        private static List<AliasRule> ReadCustomAliases(XDocument engine)
        {
            var text = engine?.Root?.Element("UserDefinedAliases")?.Value;
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return XDocument.Parse(text).Descendants("UserDefinedAlias")
                    .Select(e => new AliasRule { Name = e.Element("ObjectName")?.Value?.Trim(), Alias = e.Element("Alias")?.Value?.Trim() })
                    .Where(r => !string.IsNullOrEmpty(r.Name) && !string.IsNullOrEmpty(r.Alias))
                    .ToList();
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }

        private static int CountOf(XDocument engine, string element)
        {
            var text = engine?.Root?.Element(element)?.Value;
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            try
            {
                return int.TryParse(XDocument.Parse(text).Descendants("Count").FirstOrDefault()?.Value, out var count) ? count : 0;
            }
            catch (System.Xml.XmlException)
            {
                return 0;
            }
        }

        private static bool IsZip(byte[] bytes) => bytes.Length > 3 && bytes[0] == 'P' && bytes[1] == 'K';

        private static bool TryGetBool(XDocument document, string element, out bool value)
        {
            value = false;
            var text = document?.Root?.Element(element)?.Value?.Trim();
            return text != null && bool.TryParse(text, out value);
        }
    }
}
