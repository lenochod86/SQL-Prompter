using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Metadata
{
    /// <summary>Where the lazy load of a part of a linked server stands.</summary>
    public sealed class LinkedLoadStatus
    {
        public static readonly LinkedLoadStatus Done = new LinkedLoadStatus(false, null);

        public static readonly LinkedLoadStatus Loading = new LinkedLoadStatus(true, null);

        private LinkedLoadStatus(bool isLoading, string error)
        {
            IsLoading = isLoading;
            Error = error;
        }

        public static LinkedLoadStatus Failed(string error) => new LinkedLoadStatus(false, error);

        public bool IsLoading { get; }

        /// <summary>Why the load failed, or null.</summary>
        public string Error { get; }

        public bool IsDone => !IsLoading && Error == null;
    }

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

        /// <summary>Raised (on a background thread) when a load has finished, successfully or not: an open list asks again then (#357).</summary>
        public event EventHandler Loaded;

        /// <summary>The state of the databases of a linked server.</summary>
        public LinkedLoadStatus StatusOfServer(string server) => StatusOf("S|" + server);

        /// <summary>The state of the objects of a database of a linked server.</summary>
        public LinkedLoadStatus StatusOfDatabase(string server, string database) => StatusOf("D|" + server + "|" + database);

        private LinkedLoadStatus StatusOf(string key)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                return LinkedLoadStatus.Loading;
            }

            lock (entry)
            {
                if (entry.Result != null)
                {
                    return LinkedLoadStatus.Done;
                }

                var loading = entry.Loading != null && !entry.Loading.IsCompleted;
                return !loading && entry.Error != null ? LinkedLoadStatus.Failed(entry.Error) : LinkedLoadStatus.Loading;
            }
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
                                    entry.Error = null;
                                }

                                log("Linked server: loaded " + what + ".");
                            }

                            Loaded?.Invoke(this, EventArgs.Empty);
                        }
                        catch (Exception ex)
                        {
                            lock (entry)
                            {
                                entry.LastFailure = clock();
                                entry.Error = ex is OperationCanceledException ? "timed out" : ex.Message;
                            }

                            log("Linked server: loading " + what + " failed: " + ex.Message);
                        }

                        Loaded?.Invoke(this, EventArgs.Empty);
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
            public string Error;
        }
    }
}
