using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlPrompter.Core.History
{
    /// <summary>
    /// A query tab that was open: its text, the connection it had and, for a saved file, its path. The SQL login name is stored for a
    /// window with SQL authentication, a password never.
    /// </summary>
    [DataContract]
    public sealed class SavedTab
    {
        /// <summary>Path of the file for a saved document; empty for a new, unsaved query window.</summary>
        [DataMember(Name = "file")]
        public string File { get; set; }

        [DataMember(Name = "text")]
        public string Text { get; set; }

        [DataMember(Name = "server")]
        public string Server { get; set; }

        [DataMember(Name = "database")]
        public string Database { get; set; }

        /// <summary>SQL login name; null for Windows authentication or when the tab was not connected.</summary>
        [DataMember(Name = "login")]
        public string Login { get; set; }

        [DataMember(Name = "encrypt")]
        public string Encrypt { get; set; }

        [DataMember(Name = "trustServerCertificate")]
        public string TrustServerCertificate { get; set; }

        /// <summary>The name the user gave the tab (#226); empty for the name SSMS gives.</summary>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>The tab was the one in front.</summary>
        [DataMember(Name = "active")]
        public bool Active { get; set; }

        public bool IsConnected => !string.IsNullOrEmpty(Server);
    }

    [DataContract]
    internal sealed class SavedTabs
    {
        [DataMember(Name = "version")]
        public int Version { get; set; } = 1;

        [DataMember(Name = "tabs")]
        public List<SavedTab> Tabs { get; set; } = new List<SavedTab>();
    }

    /// <summary>
    /// The open query tabs in a local file, rewritten as a whole whenever they change (through a temporary file, so a crash while writing
    /// leaves the previous version). Kept on the user's machine.
    /// </summary>
    public sealed class TabSessionStore
    {
        private readonly string path;
        private string lastWritten;

        public TabSessionStore(string path)
        {
            this.path = path;
        }

        /// <summary>The saved tabs; empty when there is no file or it cannot be read.</summary>
        public IReadOnlyList<SavedTab> Load()
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new SavedTab[0];
                }

                using (var stream = File.OpenRead(path))
                {
                    var saved = (SavedTabs)new DataContractJsonSerializer(typeof(SavedTabs)).ReadObject(stream);
                    return (saved?.Tabs ?? new List<SavedTab>()).Where(t => t != null).ToList();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
            {
                return new SavedTab[0];
            }
        }

        /// <summary>Writes the tabs unless they are the same as the last write. Returns true when the file was written.</summary>
        public bool Save(IReadOnlyList<SavedTab> tabs)
        {
            string json;
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(SavedTabs)).WriteObject(stream, new SavedTabs { Tabs = tabs.ToList() });
                json = Encoding.UTF8.GetString(stream.ToArray());
            }

            if (json == lastWritten)
            {
                return false;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }

            lastWritten = json;
            return true;
        }
    }
}
