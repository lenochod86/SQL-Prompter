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
    /// <summary>One stored text of a query tab and when it was seen (#302).</summary>
    public sealed class TabVersion
    {
        public TabVersion(string text, DateTime atUtc)
        {
            Text = text ?? string.Empty;
            AtUtc = atUtc;
        }

        public string Text { get; }

        public DateTime AtUtc { get; }
    }

    /// <summary>A query tab with the versions of its text: when it was opened and closed, where it was connected and what it contained.</summary>
    public sealed class TabRecord
    {
        internal TabRecord(string id, string key, string name, string server, string database, DateTime openedAtUtc)
        {
            Id = id;
            Key = key ?? string.Empty;
            Name = name ?? string.Empty;
            Server = server ?? string.Empty;
            Database = database ?? string.Empty;
            OpenedAtUtc = openedAtUtc;
        }

        public string Id { get; }

        /// <summary>The document the tab shows while it is open (its file name); empty once the tab is closed.</summary>
        public string Key { get; internal set; }

        public string Name { get; internal set; }

        public string Server { get; internal set; }

        /// <summary>The SQL login of the tab (null for Windows authentication or when unknown); never a password.</summary>
        public string Login { get; internal set; }

        public string Database { get; internal set; }

        public DateTime OpenedAtUtc { get; }

        /// <summary>When the tab was closed; null while it is open.</summary>
        public DateTime? ClosedAtUtc { get; internal set; }

        /// <summary>Pinned (#285): never removed by the retention.</summary>
        public bool Pinned { get; internal set; }

        /// <summary>A favourite: never removed by the retention.</summary>
        public bool Favourite { get; internal set; }

        /// <summary>The versions of the text, oldest first.</summary>
        public List<TabVersion> Versions { get; } = new List<TabVersion>();

        public string LatestText => Versions.Count > 0 ? Versions[Versions.Count - 1].Text : string.Empty;

        public DateTime LastChangeUtc => Versions.Count > 0 ? Versions[Versions.Count - 1].AtUtc : OpenedAtUtc;

        internal string PendingText { get; set; }

        /// <summary>Closed by the start of SSMS (the session ended without a close): a tab that is restored with the same text takes it over again.</summary>
        internal bool Adoptable { get; set; }

        /// <summary>The tab as a row of the history window: the latest text, the time of the last change and its versions.</summary>
        public HistoryEntry ToEntry() => new HistoryEntry(Id, LatestText, Server, Database, Login, LastChangeUtc, ClosedAtUtc, Name)
        {
            Kind = HistoryKind.Tab,
            Versions = Versions.ToList().AsReadOnly(),
            Pinned = Pinned,
            Favourite = Favourite,
        };
    }

    /// <summary>
    /// Which versions of a tab to keep: all of the last two hours, then one per hour for two days, then one per day, never more than the limit
    /// (the newest win). The newest version is always kept.
    /// </summary>
    public static class VersionThinning
    {
        public static List<TabVersion> Thin(IReadOnlyList<TabVersion> versions, int max, DateTime nowUtc)
        {
            var kept = new List<TabVersion>();
            var seenBuckets = new HashSet<long>();
            foreach (var version in versions.OrderByDescending(v => v.AtUtc))
            {
                var age = nowUtc - version.AtUtc;
                if (age <= TimeSpan.FromHours(2))
                {
                    kept.Add(version);
                    continue;
                }

                var bucket = age <= TimeSpan.FromDays(2)
                    ? version.AtUtc.Ticks / TimeSpan.TicksPerHour
                    : -1 - version.AtUtc.Ticks / TimeSpan.TicksPerDay;
                if (seenBuckets.Add(bucket))
                {
                    kept.Add(version);
                }
            }

            if (max > 0 && kept.Count > max)
            {
                kept.RemoveRange(max, kept.Count - max);
            }

            kept.Reverse();
            return kept;
        }
    }

    /// <summary>
    /// What the history knows about query tabs, not only about the queries that were run (#302): which tabs are open, which were closed and when, and
    /// the versions of their text over time. Kept in a local file, one JSON object per line (a tab, a version, a change of connection, a close),
    /// appended while SSMS runs; the file is rewritten only when old versions or tabs are removed. Nothing is sent anywhere.
    /// </summary>
    public sealed class TabHistoryStore
    {
        private readonly object sync = new object();
        private readonly string path;
        private readonly List<TabRecord> tabs = new List<TabRecord>();

        public TabHistoryStore(string path)
        {
            this.path = path;
            MinVersionInterval = TimeSpan.FromSeconds(5);
            MaxVersions = 50;
        }

        /// <summary>A version is stored at most this often per tab.</summary>
        public TimeSpan MinVersionInterval { get; set; }

        /// <summary>The versions kept per tab (thinned out beyond the recent ones); 0 keeps all.</summary>
        public int MaxVersions { get; set; }

        /// <summary>The tabs, oldest first (a copy of the list, not of the records).</summary>
        public IReadOnlyList<TabRecord> Tabs
        {
            get
            {
                lock (sync)
                {
                    return tabs.ToList();
                }
            }
        }

        /// <summary>The tabs as rows of the history window.</summary>
        public IReadOnlyList<HistoryEntry> Entries()
        {
            lock (sync)
            {
                return tabs.Select(t => t.ToEntry()).ToList();
            }
        }

        /// <summary>
        /// Reads the file. A tab without a closing time belonged to a session that ended without saying so; it is closed at the time of its last
        /// change and can be taken over again by a restored tab with the same text (<see cref="Track"/>).
        /// </summary>
        public void Load()
        {
            var loaded = new List<TabRecord>();
            var byId = new Dictionary<string, TabRecord>(StringComparer.Ordinal);
            if (File.Exists(path))
            {
                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                {
                    var record = Parse(line);
                    if (record == null || string.IsNullOrEmpty(record.Id))
                    {
                        continue;
                    }

                    byId.TryGetValue(record.Id, out var tab);
                    DateTime at;
                    switch (record.Kind)
                    {
                        case "tab":
                            if (tab == null && TryParseTime(record.At, out at))
                            {
                                tab = new TabRecord(record.Id, record.Key, record.Name, record.Server, record.Database, at) { Login = record.Login };
                                byId[record.Id] = tab;
                                loaded.Add(tab);
                            }

                            break;
                        case "ver":
                            if (tab != null && TryParseTime(record.At, out at))
                            {
                                tab.Versions.Add(new TabVersion(record.Text, at));
                            }

                            break;
                        case "meta":
                            if (tab != null)
                            {
                                tab.Server = record.Server ?? tab.Server;
                                tab.Login = record.Login ?? tab.Login;
                                tab.Database = record.Database ?? tab.Database;
                                tab.Name = record.Name ?? tab.Name;
                            }

                            break;
                        case "close":
                            if (tab != null && TryParseTime(record.At, out at))
                            {
                                tab.ClosedAtUtc = at;
                                tab.Key = string.Empty;
                            }

                            break;
                        case "pin":
                            if (tab != null)
                            {
                                tab.Pinned = record.Text == "1";
                            }

                            break;
                        case "fav":
                            if (tab != null)
                            {
                                tab.Favourite = record.Text == "1";
                            }

                            break;
                        case "open":
                            if (tab != null)
                            {
                                tab.ClosedAtUtc = null;
                                tab.Key = record.Key ?? string.Empty;
                            }

                            break;
                    }
                }
            }

            lock (sync)
            {
                var added = tabs.Where(t => !byId.ContainsKey(t.Id)).ToList();
                tabs.Clear();
                tabs.AddRange(loaded);
                foreach (var tab in tabs.Where(t => !t.ClosedAtUtc.HasValue))
                {
                    tab.ClosedAtUtc = tab.LastChangeUtc;
                    tab.Key = string.Empty;
                    tab.Adoptable = true;
                    Append(new Record { Kind = "close", Id = tab.Id, At = FormatTime(tab.ClosedAtUtc.Value) });
                }

                tabs.AddRange(added);
            }
        }

        /// <summary>
        /// Called for every open query tab every few seconds with its current text. A tab with text is registered (a restored one takes over the tab
        /// it was restored from); a change of text is stored as a version once it has stayed unchanged for one check and the minimum interval has
        /// passed. An empty tab is not stored. Returns true when something was written.
        /// </summary>
        public bool Track(string key, string name, string server, string database, string text, DateTime nowUtc, string login = null)
        {
            lock (sync)
            {
                var tab = FindOpen(key);
                if (tab == null)
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return false;
                    }

                    tab = tabs.LastOrDefault(t => t.Adoptable && t.ClosedAtUtc.HasValue && t.LatestText == text && t.Server == (server ?? string.Empty) && t.Database == (database ?? string.Empty));
                    if (tab != null)
                    {
                        tab.Adoptable = false;
                        tab.ClosedAtUtc = null;
                        tab.Key = key ?? string.Empty;
                        tab.Name = name ?? string.Empty;
                        Append(new Record { Kind = "open", Id = tab.Id, Key = tab.Key });
                        Append(new Record { Kind = "meta", Id = tab.Id, Name = tab.Name });
                        return true;
                    }

                    tab = new TabRecord(Guid.NewGuid().ToString("N"), key, name, server, database, nowUtc) { Login = login };
                    tabs.Add(tab);
                    Append(new Record { Kind = "tab", Id = tab.Id, Key = tab.Key, Name = tab.Name, Server = tab.Server, Login = tab.Login, Database = tab.Database, At = FormatTime(nowUtc) });
                    tab.PendingText = text;
                    return true;
                }

                var written = UpdateConnection(tab, name, server, database, login);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return written;
                }

                if (tab.Versions.Count > 0 && tab.LatestText == text)
                {
                    tab.PendingText = null;
                    return written;
                }

                var last = tab.Versions.Count > 0 ? tab.Versions[tab.Versions.Count - 1].AtUtc : (DateTime?)null;
                if (tab.PendingText == text && (last == null || nowUtc - last.Value >= MinVersionInterval))
                {
                    AddVersion(tab, text, nowUtc);
                    return true;
                }

                tab.PendingText = text;
                return written;
            }
        }

        /// <summary>Stores the text of an open tab now when it differs from the last version (the tab is closed, saved or run, or SSMS shuts down).</summary>
        public bool Flush(string key, string text, DateTime nowUtc)
        {
            lock (sync)
            {
                var tab = FindOpen(key);
                if (tab == null || string.IsNullOrWhiteSpace(text) || (tab.Versions.Count > 0 && tab.LatestText == text))
                {
                    return false;
                }

                AddVersion(tab, text, nowUtc);
                return true;
            }
        }

        /// <summary>
        /// The tab shown by the document was closed. The text the last check saw (it may not have stayed unchanged for a second check yet) becomes
        /// the last version: the text of a window that is closing is not read, so nothing is lost but the changes of the last few seconds.
        /// </summary>
        public void Close(string key, DateTime nowUtc)
        {
            lock (sync)
            {
                var tab = FindOpen(key);
                if (tab == null)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(tab.PendingText) && (tab.Versions.Count == 0 || tab.LatestText != tab.PendingText))
                {
                    AddVersion(tab, tab.PendingText, nowUtc);
                }

                tab.ClosedAtUtc = nowUtc;
                tab.Key = string.Empty;
                tab.PendingText = null;
                Append(new Record { Kind = "close", Id = tab.Id, At = FormatTime(nowUtc) });
            }
        }

        /// <summary>Pins or unpins tabs; a pinned tab is kept by <see cref="Prune"/>.</summary>
        public void SetPinned(IEnumerable<string> ids, bool pinned)
        {
            lock (sync)
            {
                foreach (var tab in tabs.Where(t => ids.Contains(t.Id) && t.Pinned != pinned))
                {
                    tab.Pinned = pinned;
                    Append(new Record { Kind = "pin", Id = tab.Id, Text = pinned ? "1" : "0" });
                }
            }
        }

        /// <summary>Marks or unmarks tabs as favourites; a favourite is kept by <see cref="Prune"/>.</summary>
        public void SetFavourite(IEnumerable<string> ids, bool favourite)
        {
            lock (sync)
            {
                foreach (var tab in tabs.Where(t => ids.Contains(t.Id) && t.Favourite != favourite))
                {
                    tab.Favourite = favourite;
                    Append(new Record { Kind = "fav", Id = tab.Id, Text = favourite ? "1" : "0" });
                }
            }
        }

        public void Delete(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids, StringComparer.Ordinal);
            lock (sync)
            {
                if (tabs.RemoveAll(t => set.Contains(t.Id)) > 0)
                {
                    Rewrite();
                }
            }
        }

        /// <summary>Removes the closed tabs whose last change is older than <paramref name="keepDays"/> days (0 or less keeps everything). Returns how many were removed.</summary>
        public int Prune(int keepDays, DateTime nowUtc)
        {
            if (keepDays <= 0)
            {
                return 0;
            }

            lock (sync)
            {
                var limit = nowUtc.AddDays(-keepDays);
                var removed = tabs.RemoveAll(t => t.ClosedAtUtc.HasValue && !t.Pinned && !t.Favourite && t.LastChangeUtc < limit);
                if (removed > 0)
                {
                    Rewrite();
                }

                return removed;
            }
        }

        private TabRecord FindOpen(string key) =>
            tabs.LastOrDefault(t => !t.ClosedAtUtc.HasValue && string.Equals(t.Key, key ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        private bool UpdateConnection(TabRecord tab, string name, string server, string database, string login)
        {
            server = server ?? string.Empty;
            database = database ?? string.Empty;
            name = name ?? string.Empty;
            if (tab.Server == server && tab.Database == database && tab.Name == name && (login == null || tab.Login == login))
            {
                return false;
            }

            tab.Server = server;
            tab.Login = login ?? tab.Login;
            tab.Database = database;
            tab.Name = name;
            Append(new Record { Kind = "meta", Id = tab.Id, Server = server, Login = tab.Login, Database = database, Name = name });
            return true;
        }

        private void AddVersion(TabRecord tab, string text, DateTime nowUtc)
        {
            tab.Versions.Add(new TabVersion(text, nowUtc));
            tab.PendingText = null;
            Append(new Record { Kind = "ver", Id = tab.Id, At = FormatTime(nowUtc), Text = text });
            if (MaxVersions > 0 && tab.Versions.Count > MaxVersions + 10)
            {
                var kept = VersionThinning.Thin(tab.Versions, MaxVersions, nowUtc);
                tab.Versions.Clear();
                tab.Versions.AddRange(kept);
                Rewrite();
            }
        }

        private void Append(Record record)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, Serialize(record) + "\n", new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // The history is a convenience: a file that is locked for a moment must not break the editor.
            }
        }

        private void Rewrite()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + ".tmp";
                var lines = new List<string>();
                foreach (var tab in tabs)
                {
                    lines.Add(Serialize(new Record { Kind = "tab", Id = tab.Id, Key = tab.Key, Name = tab.Name, Server = tab.Server, Login = tab.Login, Database = tab.Database, At = FormatTime(tab.OpenedAtUtc) }));
                    lines.AddRange(tab.Versions.Select(v => Serialize(new Record { Kind = "ver", Id = tab.Id, At = FormatTime(v.AtUtc), Text = v.Text })));
                    if (tab.ClosedAtUtc.HasValue)
                    {
                        lines.Add(Serialize(new Record { Kind = "close", Id = tab.Id, At = FormatTime(tab.ClosedAtUtc.Value) }));
                    }

                    if (tab.Pinned)
                    {
                        lines.Add(Serialize(new Record { Kind = "pin", Id = tab.Id, Text = "1" }));
                    }

                    if (tab.Favourite)
                    {
                        lines.Add(Serialize(new Record { Kind = "fav", Id = tab.Id, Text = "1" }));
                    }
                }

                File.WriteAllLines(temp, lines, new UTF8Encoding(false));
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
            [DataMember(Name = "t", EmitDefaultValue = false)]
            public string Kind { get; set; }

            [DataMember(Name = "id", EmitDefaultValue = false)]
            public string Id { get; set; }

            [DataMember(Name = "key", EmitDefaultValue = false)]
            public string Key { get; set; }

            [DataMember(Name = "name", EmitDefaultValue = false)]
            public string Name { get; set; }

            [DataMember(Name = "server", EmitDefaultValue = false)]
            public string Server { get; set; }

            [DataMember(Name = "login", EmitDefaultValue = false)]
            public string Login { get; set; }

            [DataMember(Name = "database", EmitDefaultValue = false)]
            public string Database { get; set; }

            [DataMember(Name = "at", EmitDefaultValue = false)]
            public string At { get; set; }

            [DataMember(Name = "text", EmitDefaultValue = false)]
            public string Text { get; set; }
        }
    }
}
