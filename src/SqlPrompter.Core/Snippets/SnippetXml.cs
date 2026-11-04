using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SqlPrompter.Core.Snippets
{
    /// <summary>
    /// Reads the XML backup of SQL Prompt snippets (<c>.sqlpromptsnippet</c>, Visual Studio snippet XML): the shortcut becomes the prefix,
    /// the literals become placeholders with their defaults and the code (usually a CDATA section) the body.
    /// </summary>
    public static class SnippetXml
    {
        /// <exception cref="FormatException">The text is not a snippet file.</exception>
        public static IReadOnlyList<Snippet> Parse(string xml)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(xml ?? string.Empty);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new FormatException("The file is not valid XML: " + ex.Message, ex);
            }

            var result = new List<Snippet>();
            foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "CodeSnippet"))
            {
                var header = Child(element, "Header");
                var snippet = Child(element, "Snippet");
                var code = snippet == null ? null : Child(snippet, "Code");
                if (code == null)
                {
                    continue;
                }

                var title = Text(header, "Title");
                var prefix = Text(header, "Shortcut");
                prefix = string.IsNullOrWhiteSpace(prefix) ? title : prefix;
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    continue;
                }

                var placeholders = new List<SnippetPlaceholder>();
                var declarations = Child(snippet, "Declarations");
                if (declarations != null)
                {
                    foreach (var literal in declarations.Elements().Where(e => e.Name.LocalName == "Literal" || e.Name.LocalName == "Object"))
                    {
                        var id = Text(literal, "ID");
                        if (!string.IsNullOrEmpty(id) && placeholders.All(p => p.Name != id))
                        {
                            placeholders.Add(new SnippetPlaceholder { Name = id, DefaultValue = Text(literal, "Default") });
                        }
                    }
                }

                result.Add(Snippet.Create(prefix.Trim(), Text(header, "Description"), code.Value, placeholders));
            }

            if (result.Count == 0)
            {
                throw new FormatException("The file contains no snippet.");
            }

            return result;
        }

        public static IReadOnlyList<Snippet> ParseFile(string path) => Parse(File.ReadAllText(path));

        private static XElement Child(XElement parent, string name) =>
            parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

        private static string Text(XElement parent, string name) => Child(parent, name)?.Value ?? string.Empty;
    }
}
