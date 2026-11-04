using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlPrompter.Core.Snippets
{
    public sealed class SnippetLoadResult
    {
        public SnippetLoadResult(IReadOnlyList<Snippet> snippets, IReadOnlyList<string> errors)
        {
            Snippets = snippets;
            Errors = errors;
        }

        /// <summary>Sorted by prefix.</summary>
        public IReadOnlyList<Snippet> Snippets { get; }

        /// <summary>One line per file that could not be read (file name and reason); the rest is loaded anyway.</summary>
        public IReadOnlyList<string> Errors { get; }
    }

    public sealed class SnippetImportResult
    {
        public SnippetImportResult(int imported, int replaced, int skipped, IReadOnlyList<string> errors)
        {
            Imported = imported;
            Replaced = replaced;
            Skipped = skipped;
            Errors = errors;
        }

        /// <summary>Snippets written, including the replaced ones.</summary>
        public int Imported { get; }

        public int Replaced { get; }

        /// <summary>Snippets left alone because one with the same prefix exists and replacing was not asked for.</summary>
        public int Skipped { get; }

        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>
    /// A folder of snippet files, one JSON file per snippet named <c>prefix-id.json</c> as SQL Prompt does, so an existing SQL Prompt
    /// snippet folder can be read directly or copied. Subfolders (such as "backup") are ignored.
    /// </summary>
    public sealed class SnippetStore
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(Snippet));

        public SnippetStore(string folder)
        {
            Folder = folder ?? throw new ArgumentNullException(nameof(folder));
        }

        public string Folder { get; }

        public SnippetLoadResult Load()
        {
            var snippets = new List<Snippet>();
            var errors = new List<string>();
            if (!Directory.Exists(Folder))
            {
                return new SnippetLoadResult(snippets, errors);
            }

            foreach (var file in Directory.EnumerateFiles(Folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var snippet = Read(File.ReadAllText(file, Encoding.UTF8));
                    if (string.IsNullOrWhiteSpace(snippet.Prefix))
                    {
                        errors.Add(Path.GetFileName(file) + ": no prefix");
                        continue;
                    }

                    snippets.Add(snippet);
                }
                catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    errors.Add(Path.GetFileName(file) + ": " + ex.Message);
                }
            }

            snippets.Sort((a, b) => string.Compare(a.Prefix, b.Prefix, StringComparison.OrdinalIgnoreCase));
            return new SnippetLoadResult(snippets, errors);
        }

        /// <summary>Writes the snippet (through a temporary file) and returns its path; an existing file of the same id is replaced.</summary>
        public string Save(Snippet snippet)
        {
            Directory.CreateDirectory(Folder);
            DeleteFilesOf(snippet.Id);
            var path = Path.Combine(Folder, FileName(snippet));
            var temp = path + ".tmp";
            File.WriteAllText(temp, Write(snippet), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
            return path;
        }

        public bool Delete(string id) => DeleteFilesOf(id) > 0;

        /// <summary>
        /// Imports snippets from XML backups (<c>.sqlpromptsnippet</c>) or JSON snippet files into the folder.
        /// A snippet whose prefix exists already is replaced only with <paramref name="replaceSamePrefix"/>.
        /// </summary>
        public SnippetImportResult Import(IEnumerable<string> files, bool replaceSamePrefix) => Import(files, prefix => replaceSamePrefix);

        /// <param name="replaceSamePrefix">Asked with the prefix when an existing snippet has it; true replaces it (keeping its id), false skips the new one.</param>
        public SnippetImportResult Import(IEnumerable<string> files, Func<string, bool> replaceSamePrefix)
        {
            var existing = Load().Snippets.ToList();
            var errors = new List<string>();
            int imported = 0, replaced = 0, skipped = 0;
            foreach (var file in files)
            {
                IReadOnlyList<Snippet> parsed;
                try
                {
                    parsed = string.Equals(Path.GetExtension(file), ".json", StringComparison.OrdinalIgnoreCase)
                        ? new[] { Read(File.ReadAllText(file, Encoding.UTF8)) }
                        : SnippetXml.Parse(File.ReadAllText(file, Encoding.UTF8));
                }
                catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    errors.Add(Path.GetFileName(file) + ": " + ex.Message);
                    continue;
                }

                foreach (var snippet in parsed)
                {
                    var same = existing.FirstOrDefault(s => string.Equals(s.Prefix, snippet.Prefix, StringComparison.OrdinalIgnoreCase));
                    if (same != null && !replaceSamePrefix(snippet.Prefix))
                    {
                        skipped++;
                        continue;
                    }

                    if (same != null)
                    {
                        snippet.Id = same.Id;
                        existing.Remove(same);
                        replaced++;
                    }

                    Save(snippet);
                    existing.Add(snippet);
                    imported++;
                }
            }

            return new SnippetImportResult(imported, replaced, skipped, errors);
        }

        public static string Write(Snippet snippet)
        {
            // An empty list would be written as [], SQL Prompt leaves the member out.
            var toWrite = snippet.Placeholders != null && snippet.Placeholders.Count > 0
                ? snippet
                : new Snippet { Id = snippet.Id, Prefix = snippet.Prefix, Description = snippet.Description, Body = snippet.Body, Placeholders = null };
            return Serialize(toWrite);
        }

        private static string Serialize(Snippet snippet)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false, indent: true))
                {
                    Serializer.WriteObject(writer, snippet);
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <exception cref="FormatException">The text is not a snippet.</exception>
        public static Snippet Read(string json)
        {
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
                {
                    return (Snippet)Serializer.ReadObject(stream) ?? throw new FormatException("empty file");
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is InvalidCastException || ex is ArgumentException)
            {
                throw new FormatException("not a valid snippet file: " + ex.Message, ex);
            }
        }

        private int DeleteFilesOf(string id)
        {
            if (string.IsNullOrEmpty(id) || !Directory.Exists(Folder))
            {
                return 0;
            }

            var deleted = 0;
            foreach (var file in Directory.EnumerateFiles(Folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith("-" + id + ".json", StringComparison.OrdinalIgnoreCase) || string.Equals(name, id + ".json", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    deleted++;
                }
            }

            return deleted;
        }

        private static string FileName(Snippet snippet)
        {
            var prefix = new string((snippet.Prefix ?? string.Empty).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            return prefix + "-" + snippet.Id + ".json";
        }
    }
}
