using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// How often each suggestion was chosen, for ordering the list. Kept on the user's machine only; nothing is sent anywhere.
    /// A key stands for one item: its kind, its text and where it belongs (<see cref="Suggestion.UsageKey"/>), prefixed with the
    /// database by the caller so that the same table name in two databases is counted apart.
    /// </summary>
    public sealed class UsageStatistics
    {
        /// <summary>Most items kept; when more are chosen, the least used are forgotten.</summary>
        public const int MaxEntries = 5000;

        private readonly object sync = new object();
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Increased every time something changes, so that a caller knows when there is something to save.</summary>
        public int Version { get; private set; }

        public int EntryCount
        {
            get
            {
                lock (sync)
                {
                    return counts.Count;
                }
            }
        }

        public int CountOf(string key)
        {
            lock (sync)
            {
                return key != null && counts.TryGetValue(key, out var count) ? count : 0;
            }
        }

        public void Record(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (sync)
            {
                counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
                Version++;
                if (counts.Count > MaxEntries)
                {
                    // Forget the least used (and, among those, the arbitrary ones) down to 90 %.
                    foreach (var forgotten in counts.OrderBy(p => p.Value).Take(counts.Count - MaxEntries * 9 / 10).Select(p => p.Key).ToList())
                    {
                        counts.Remove(forgotten);
                    }
                }
            }
        }

        /// <summary>Adds the counts of another set to this one.</summary>
        public void MergeFrom(UsageStatistics other)
        {
            Dictionary<string, int> added;
            lock (other.sync)
            {
                added = new Dictionary<string, int>(other.counts);
            }

            lock (sync)
            {
                foreach (var pair in added)
                {
                    counts[pair.Key] = counts.TryGetValue(pair.Key, out var count) ? count + pair.Value : pair.Value;
                }

                Version++;
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                counts.Clear();
                Version++;
            }
        }

        /// <summary>
        /// The text that orders items of the same group: a more used item gets a smaller number and so comes first;
        /// items never chosen keep the plain order (999). Counts above 998 are equal.
        /// </summary>
        public static string SortPrefix(int count) => (999 - Math.Min(Math.Max(count, 0), 998)).ToString("D3");

        public string ToJson()
        {
            List<Entry> entries;
            lock (sync)
            {
                entries = counts.Select(p => new Entry { Key = p.Key, Count = p.Value }).OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
            }

            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false, indent: true))
                {
                    new DataContractJsonSerializer(typeof(List<Entry>)).WriteObject(writer, entries);
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>The statistics from the text; a text that cannot be read gives empty statistics (nothing important is lost).</summary>
        public static UsageStatistics FromJson(string json)
        {
            var result = new UsageStatistics();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var entries = (List<Entry>)new DataContractJsonSerializer(typeof(List<Entry>)).ReadObject(stream);
                    foreach (var entry in entries ?? new List<Entry>())
                    {
                        if (!string.IsNullOrEmpty(entry.Key) && entry.Count > 0)
                        {
                            result.counts[entry.Key] = entry.Count;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is InvalidCastException || ex is ArgumentException)
            {
                result.counts.Clear();
            }

            return result;
        }

        [DataContract]
        private sealed class Entry
        {
            [DataMember(Name = "key")]
            public string Key { get; set; }

            [DataMember(Name = "count")]
            public int Count { get; set; }
        }
    }
}
