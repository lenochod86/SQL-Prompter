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
    /// From SQL Prompt, only what exists here is taken: whether suggestions are on and the commit keys.
    /// Aliases, qualification and the rest follow with their own features.
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
            try
            {
                using (var archive = new ZipArchive(zip, ZipArchiveMode.Read))
                {
                    var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith("UIOptions.xml", StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        throw new FormatException("The file is not a SQL Prompt settings export: UIOptions.xml is missing.");
                    }

                    using (var reader = entry.Open())
                    {
                        ui = XDocument.Load(reader);
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                throw new FormatException("The file is not a SQL Prompt settings export.", ex);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new FormatException("UIOptions.xml in the export is not valid XML.", ex);
            }

            var settings = current.Clone();
            var behavior = settings.Behavior;
            var applied = new List<string>();
            var notes = new List<string>();

            void Take(string element, string description, Action<bool> set)
            {
                if (TryGetBool(ui, element, out var value))
                {
                    set(value);
                    applied.Add(description + ": " + (value ? "on" : "off"));
                }
            }

            Take("SuggestionsEnabledOnStartUpSSMS", "Suggestions enabled", v => behavior.SuggestionsEnabled = v);
            Take("CandidateListCompleteSpace", "Commit on Space", v => behavior.CommitOnSpace = v);
            Take("CandidateListCompleteDot", "Commit on dot", v => behavior.CommitOnDot = v);
            Take("CandidateListCompleteComma", "Commit on comma", v => behavior.CommitOnComma = v);
            Take("CandidateListCompleteParentheses", "Commit on parentheses", v => behavior.CommitOnParentheses = v);
            Take("CandidateListCompleteSemicolon", "Commit on semicolon", v => behavior.CommitOnSemicolon = v);
            Take("CandidateListCompleteSquareBrackets", "Commit on ]", v => behavior.CommitOnClosingBracket = v);

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

        private static bool IsZip(byte[] bytes) => bytes.Length > 3 && bytes[0] == 'P' && bytes[1] == 'K';

        private static bool TryGetBool(XDocument document, string element, out bool value)
        {
            value = false;
            var text = document.Root?.Element(element)?.Value?.Trim();
            return text != null && bool.TryParse(text, out value);
        }
    }
}
