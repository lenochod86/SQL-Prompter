using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlPrompter.Core.History
{
    /// <summary>One executed query. The login is the SQL login name (null for Windows authentication); a password is never stored.</summary>
    public sealed class HistoryEntry
    {
        public HistoryEntry(string id, string text, string server, string database, string login, DateTime ranAtUtc, DateTime? closedAtUtc = null, string tab = null)
        {
            Id = id;
            Text = text ?? string.Empty;
            Server = server ?? string.Empty;
            Database = database ?? string.Empty;
            Login = login;
            RanAtUtc = ranAtUtc;
            ClosedAtUtc = closedAtUtc;
            Tab = tab ?? string.Empty;
        }

        public string Id { get; }

        public string Text { get; }

        public string Server { get; }

        public string Database { get; }

        public string Login { get; }

        public DateTime RanAtUtc { get; }

        /// <summary>Name of the query window the query was run in (empty for entries recorded before it was stored).</summary>
        public string Tab { get; }

        /// <summary>When the query tab was closed; null while it is open (or when SSMS ended without telling).</summary>
        public DateTime? ClosedAtUtc { get; internal set; }

        /// <summary>A favourite: it is never removed by the retention and is listed first (#225).</summary>
        public bool Pinned { get; internal set; }
    }

    /// <summary>
    /// The history of executed queries in a local file, one JSON object per line: a line with the text is a run, a line with only an id and
    /// a closing time says when the tab of that run was closed. Appending a line is cheap, so recording does not slow down the editor;
    /// the file is rewritten only when old entries are removed. Kept on the user's machine; nothing is sent anywhere.
    /// </summary>
    public sealed class HistoryStore
    {
        private readonly object sync = new object();
        private readonly string path;
        private readonly List<HistoryEntry> entries = new List<HistoryEntry>();

        public HistoryStore(string path)
        {
            this.path = path;
        }

        /// <summary>The entries, oldest first (a copy).</summary>
        public IReadOnlyList<HistoryEntry> Entries
        {
            get
            {
                lock (sync)
                {
                    return entries.ToList();
                }
            }
        }

        /// <summary>Reads the file; lines that cannot be read are skipped. Entries added before are kept.</summary>
        public void Load()
        {
            var loaded = new List<HistoryEntry>();
            var byId = new Dictionary<string, HistoryEntry>(StringComparer.Ordinal);
            if (File.Exists(path))
            {
                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                {
                    var record = Parse(line);
                    if (record == null || string.IsNullOrEmpty(record.Id))
                    {
                        continue;
                    }

                    if (record.Text == null)
                    {
                        if (byId.TryGetValue(record.Id, out var known))
                        {
                            if (TryParseTime(record.Closed, out var closed))
                            {
                                known.ClosedAtUtc = closed;
                            }

                            if (record.Pinned != null)
                            {
                                known.Pinned = record.Pinned == "1";
                            }
                        }

                        continue;
                    }

                    if (TryParseTime(record.Ran, out var ran) && !byId.ContainsKey(record.Id))
                    {
                        var entry = new HistoryEntry(record.Id, record.Text, record.Server, record.Database, record.Login, ran,
                            TryParseTime(record.Closed, out var closedAt) ? closedAt : (DateTime?)null, record.Tab) { Pinned = record.Pinned == "1" };
                        byId[record.Id] = entry;
                        loaded.Add(entry);
                    }
                }
            }

            lock (sync)
            {
                var added = entries.Where(e => !byId.ContainsKey(e.Id)).ToList();
                entries.Clear();
                entries.AddRange(loaded);
                entries.AddRange(added);
            }
        }

        /// <summary>Records a run; a blank text is not recorded (null is returned).</summary>
        public HistoryEntry Add(string text, string server, string database, string login, DateTime nowUtc, string tab = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var entry = new HistoryEntry(Guid.NewGuid().ToString("N"), text, server, database, login, nowUtc, null, tab);
            lock (sync)
            {
                entries.Add(entry);
                Append(ToLine(entry));
            }

            return entry;
        }

        /// <summary>Records that the tab the given runs came from was closed.</summary>
        public void MarkClosed(IEnumerable<string> ids, DateTime nowUtc)
        {
            lock (sync)
            {
                foreach (var id in ids)
                {
                    var entry = entries.FirstOrDefault(e => e.Id == id);
                    if (entry == null)
                    {
                        continue;
                    }

                    entry.ClosedAtUtc = nowUtc;
                    Append(Serialize(new Record { Id = id, Closed = FormatTime(nowUtc) }));
                }
            }
        }

        /// <summary>Pins or unpins runs; a pinned run is kept by <see cref="Prune"/> and comes first in <see cref="Filter"/>.</summary>
        public void SetPinned(IEnumerable<string> ids, bool pinned)
        {
            lock (sync)
            {
                foreach (var id in ids)
                {
                    var entry = entries.FirstOrDefault(e => e.Id == id);
                    if (entry == null || entry.Pinned == pinned)
                    {
                        continue;
                    }

                    entry.Pinned = pinned;
                    Append(Serialize(new Record { Id = id, Pinned = pinned ? "1" : "0" }));
                }
            }
        }

        /// <summary>Removes the runs older than <paramref name="keepDays"/> days (0 or less keeps everything). Returns how many were removed.</summary>
        public int Prune(int keepDays, DateTime nowUtc)
        {
            if (keepDays <= 0)
            {
                return 0;
            }

            lock (sync)
            {
                var limit = nowUtc.AddDays(-keepDays);
                var removed = entries.RemoveAll(e => !e.Pinned && e.RanAtUtc < limit);
                if (removed > 0)
                {
                    Rewrite();
                }

                return removed;
            }
        }

        public void Delete(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids, StringComparer.Ordinal);
            lock (sync)
            {
                if (entries.RemoveAll(e => set.Contains(e.Id)) > 0)
                {
                    Rewrite();
                }
            }
        }

        /// <summary>
        /// The entries that contain every word of <paramref name="filter"/> (ignoring case) in the text, the server or the database,
        /// newest first. An empty filter gives everything.
        /// </summary>
        public static IReadOnlyList<HistoryEntry> Filter(IEnumerable<HistoryEntry> source, string filter)
        {
            var words = (filter ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return source
                .Where(e => words.All(w =>
                    e.Text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.Server.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.Tab.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.Database.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(e => e.Pinned)
                .ThenByDescending(e => e.RanAtUtc)
                .ToList();
        }

        private void Append(string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // The history is a convenience: a file that is locked for a moment must not break running a query.
            }
        }

        private void Rewrite()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + ".tmp";
                File.WriteAllLines(temp, entries.Select(ToLine), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
            }
            catch (IOException)
            {
            }
        }

        private static string ToLine(HistoryEntry e) => Serialize(new Record
        {
            Id = e.Id,
            Text = e.Text,
            Server = e.Server,
            Database = e.Database,
            Login = e.Login,
            Ran = FormatTime(e.RanAtUtc),
            Closed = e.ClosedAtUtc.HasValue ? FormatTime(e.ClosedAtUtc.Value) : null,
            Tab = e.Tab.Length > 0 ? e.Tab : null,
            Pinned = e.Pinned ? "1" : null,
        });

        private static string FormatTime(DateTime utc) => utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        private static bool TryParseTime(string text, out DateTime utc) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);

        private static string Serialize(Record record)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Record)).WriteObject(stream, record);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static Record Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                {
                    return (Record)new DataContractJsonSerializer(typeof(Record)).ReadObject(stream);
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is InvalidCastException || ex is ArgumentException)
            {
                return null;
            }
        }

        [DataContract]
        private sealed class Record
        {
            [DataMember(Name = "id", EmitDefaultValue = false)]
            public string Id { get; set; }

            [DataMember(Name = "text", EmitDefaultValue = false)]
            public string Text { get; set; }

            [DataMember(Name = "server", EmitDefaultValue = false)]
            public string Server { get; set; }

            [DataMember(Name = "database", EmitDefaultValue = false)]
            public string Database { get; set; }

            [DataMember(Name = "login", EmitDefaultValue = false)]
            public string Login { get; set; }

            [DataMember(Name = "ran", EmitDefaultValue = false)]
            public string Ran { get; set; }

            [DataMember(Name = "closed", EmitDefaultValue = false)]
            public string Closed { get; set; }

            [DataMember(Name = "tab", EmitDefaultValue = false)]
            public string Tab { get; set; }

            [DataMember(Name = "pinned", EmitDefaultValue = false)]
            public string Pinned { get; set; }
        }
    }
}
