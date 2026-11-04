using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Metadata
{
    /// <summary>
    /// What is known about the linked servers reachable from one connection. Nothing is loaded up front: the databases of a server
    /// are requested when its name is typed, the objects of a database when the database is chosen, the columns of a table when
    /// the table is used. Every method returns at once with what is cached (null while it loads) and starts the loading in the
    /// background; a failure or a timeout is remembered for <see cref="RetryDelay"/> and never blocks or breaks the normal list.
    /// </summary>
    public sealed class LinkedServerCache
    {
        private readonly ConcurrentDictionary<string, Entry> entries = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string> log;
        private readonly Func<DateTime> clock;

        public LinkedServerCache(Action<string> log = null, Func<DateTime> clock = null)
        {
            this.log = log ?? (_ => { });
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>After a failure the next attempt for the same thing waits this long.</summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Overall limit for one load (the call itself has its own timeout).</summary>
        public TimeSpan LoadTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>The databases of the linked server, or null while they load or after a failure.</summary>
        public RemoteSnapshot GetServer(string server, IMetadataLoader loader) =>
            Get("S|" + server, "databases of " + server, loader,
                async (l, ct) => RemoteSnapshot.OfServer(await l.LoadLinkedDatabasesAsync(server, ct).ConfigureAwait(false)));

        /// <summary>The tables and views of one database of the linked server, or null while they load or after a failure.</summary>
        public RemoteSnapshot GetDatabase(string server, string database, IMetadataLoader loader) =>
            Get("D|" + server + "|" + database, "objects of " + server + "." + database, loader,
                async (l, ct) => RemoteSnapshot.OfDatabase(database, await l.LoadLinkedObjectsAsync(server, database, ct).ConfigureAwait(false)));

        /// <summary>The columns of one table of the linked server, or null while they load or after a failure.</summary>
        public IReadOnlyList<DbColumn> GetColumns(string server, string database, string schema, string table, IMetadataLoader loader) =>
            Get("C|" + server + "|" + database + "|" + schema + "|" + table, "columns of " + server + "." + database + "." + schema + "." + table, loader,
                (l, ct) => l.LoadLinkedColumnsAsync(server, database, schema, table, ct));

        public void Clear() => entries.Clear();

        private T Get<T>(string key, string what, IMetadataLoader loader, Func<IMetadataLoader, CancellationToken, Task<T>> load)
            where T : class
        {
            var entry = entries.GetOrAdd(key, _ => new Entry());
            lock (entry)
            {
                if (entry.Result != null)
                {
                    return (T)entry.Result;
                }

                var loading = entry.Loading != null && !entry.Loading.IsCompleted;
                if (!loading && clock() - entry.LastFailure >= RetryDelay)
                {
                    entry.Loading = Task.Run(async () =>
                    {
                        try
                        {
                            using (var cts = new CancellationTokenSource(LoadTimeout))
                            {
                                var result = await load(loader, cts.Token).ConfigureAwait(false);
                                lock (entry)
                                {
                                    entry.Result = result;
                                }

                                log("Linked server: loaded " + what + ".");
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (entry)
                            {
                                entry.LastFailure = clock();
                            }

                            log("Linked server: loading " + what + " failed: " + ex.Message);
                        }
                    });
                }

                return null;
            }
        }

        private sealed class Entry
        {
            public object Result;
            public Task Loading;
            public DateTime LastFailure = DateTime.MinValue;
        }
    }
}
