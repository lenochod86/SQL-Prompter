using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Metadata
{
    /// <summary>
    /// Metadata of one database. All methods return immediately: loading runs in the background
    /// and the result is visible in Current on the next completion.
    /// </summary>
    public sealed class DatabaseMetadataCache
    {
        private readonly object gate = new object();
        private readonly HashSet<int> loadingColumns = new HashSet<int>();
        private readonly Action<string> log;
        private readonly Func<DateTime> clock;
        private volatile DatabaseSnapshot snapshot;
        private Task loading;
        private int generation;
        private DateTime lastFailure = DateTime.MinValue;
        private string lastError;
        private DateTime lastVersionCheck = DateTime.MinValue;

        public DatabaseMetadataCache(Action<string> log = null, Func<DateTime> clock = null)
        {
            this.log = log ?? (_ => { });
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>After a failure the next load attempt is postponed.</summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>How often at most to check whether the objects in the database changed.</summary>
        public TimeSpan VersionCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Current snapshot, or null until it is loaded.</summary>
        public DatabaseSnapshot Current => snapshot;

        /// <summary>Where the load of the objects stands: done, loading, or failed with the reason (for a database other than the one of the window, #443).</summary>
        public LinkedLoadStatus Status
        {
            get
            {
                lock (gate)
                {
                    if (snapshot != null)
                    {
                        return LinkedLoadStatus.Done;
                    }

                    // The error is cleared when a new attempt starts, so it is still the state while the failing task itself is finishing (#470).
                    return lastError != null ? LinkedLoadStatus.Failed(lastError) : LinkedLoadStatus.Loading;
                }
            }
        }

        /// <summary>
        /// Raised on a background thread after the objects were loaded or reloaded because they changed (#423), so that what was computed
        /// from the old snapshot (code analysis, open panels) can be computed again. Not raised when only columns were added.
        /// </summary>
        public event EventHandler SnapshotReplaced;

        /// <summary>Raised (as well as SnapshotReplaced) only when the objects of a loaded snapshot were reloaded because they had changed (#423).</summary>
        public event EventHandler ObjectsChanged;

        /// <summary>Raised on a background thread when a load of the objects failed, so that an open list can replace its "Loading..." row by the reason (#470).</summary>
        public event EventHandler LoadFailed;

        /// <summary>Starts loading objects if not done yet. Returns the task only for tests.</summary>
        public Task EnsureLoaded(IMetadataLoader loader)
        {
            lock (gate)
            {
                if (snapshot != null)
                {
                    return Task.CompletedTask;
                }

                if (loading != null && !loading.IsCompleted)
                {
                    return loading;
                }

                if (clock() - lastFailure < RetryDelay)
                {
                    return Task.CompletedTask;
                }

                var current = generation;
                lastError = null;
                loading = Task.Run(() => LoadObjectsAsync(loader, current));
                return loading;
            }
        }

        /// <summary>Starts loading columns of objects that are neither in the snapshot nor being loaded.</summary>
        public Task RequestColumns(IEnumerable<DbObject> objects, IMetadataLoader loader)
        {
            List<int> ids;
            int current;
            lock (gate)
            {
                var known = snapshot;
                if (known == null || clock() - lastFailure < RetryDelay)
                {
                    return Task.CompletedTask;
                }

                ids = objects.Select(o => o.Id).Distinct()
                    .Where(id => !known.HasColumns(id) && !loadingColumns.Contains(id))
                    .ToList();
                if (ids.Count == 0)
                {
                    return Task.CompletedTask;
                }

                foreach (var id in ids)
                {
                    loadingColumns.Add(id);
                }

                current = generation;
            }

            return Task.Run(() => LoadColumnsAsync(loader, ids, current));
        }

        /// <summary>At most once per VersionCheckInterval checks whether the objects changed and refreshes the snapshot if so.</summary>
        /// <param name="force">Check even if the last check was less than VersionCheckInterval ago (e.g. right after DDL ran).</param>
        public Task CheckForChanges(IMetadataLoader loader, bool force = false)
        {
            lock (gate)
            {
                var now = clock();
                if (snapshot == null || (loading != null && !loading.IsCompleted) || (!force && now - lastVersionCheck < VersionCheckInterval))
                {
                    return Task.CompletedTask;
                }

                lastVersionCheck = now;
                var current = generation;
                loading = Task.Run(() => RefreshIfChangedAsync(loader, current));
                return loading;
            }
        }

        /// <summary>Drops the snapshot; the next completion loads the metadata again.</summary>
        public void Invalidate()
        {
            lock (gate)
            {
                generation++;
                snapshot = null;
                loading = null;
                lastFailure = DateTime.MinValue;
                lastError = null;
                lastVersionCheck = DateTime.MinValue;
                loadingColumns.Clear();
            }
        }

        private async Task LoadObjectsAsync(IMetadataLoader loader, int expectedGeneration)
        {
            var started = clock();
            try
            {
                var loaded = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                lock (gate)
                {
                    if (generation != expectedGeneration)
                    {
                        return;
                    }

                    snapshot = loaded;
                    lastError = null;
                    lastVersionCheck = clock();
                }

                log("Metadata of database " + loaded.DatabaseName + " loaded: " + loaded.Objects.Count + " objects in " +
                    (int)(clock() - started).TotalMilliseconds + " ms.");
                RaiseReplaced();
            }
            catch (Exception ex)
            {
                Fail("Loading metadata failed", ex);
                try { LoadFailed?.Invoke(this, EventArgs.Empty); } catch (Exception handlerError) { log("A handler of the failed load failed: " + handlerError.Message); }
            }
        }

        private async Task LoadColumnsAsync(IMetadataLoader loader, List<int> ids, int expectedGeneration)
        {
            try
            {
                var loaded = await loader.LoadColumnsAsync(ids, CancellationToken.None).ConfigureAwait(false);
                var complete = new Dictionary<int, IReadOnlyList<DbColumn>>();
                foreach (var id in ids)
                {
                    // An object that has disappeared meanwhile gets an empty list so that it is not loaded over and over.
                    complete[id] = loaded.TryGetValue(id, out var list) ? list : Array.Empty<DbColumn>();
                }

                lock (gate)
                {
                    if (generation == expectedGeneration && snapshot != null)
                    {
                        snapshot = snapshot.WithColumns(complete);
                    }
                }
            }
            catch (Exception ex)
            {
                Fail("Loading columns failed", ex);
            }
            finally
            {
                lock (gate)
                {
                    foreach (var id in ids)
                    {
                        loadingColumns.Remove(id);
                    }
                }
            }
        }

        private async Task RefreshIfChangedAsync(IMetadataLoader loader, int expectedGeneration)
        {
            try
            {
                var version = await loader.GetVersionAsync(CancellationToken.None).ConfigureAwait(false);
                var known = snapshot;
                if (known == null || version == known.Version)
                {
                    return;
                }

                var loaded = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                var replaced = false;
                lock (gate)
                {
                    if (generation == expectedGeneration)
                    {
                        // After objects change, columns are loaded again on demand.
                        snapshot = loaded;
                        replaced = true;
                    }
                }

                if (replaced)
                {
                    log("Metadata of database " + loaded.DatabaseName + " refreshed after objects changed.");
                    RaiseReplaced();
                    try { ObjectsChanged?.Invoke(this, EventArgs.Empty); } catch (Exception ex) { log("A handler of the metadata change failed: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Fail("Checking metadata for changes failed", ex);
            }
        }

        private void RaiseReplaced()
        {
            try
            {
                SnapshotReplaced?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                log("A handler of the metadata change failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void Fail(string message, Exception ex)
        {
            lock (gate)
            {
                lastFailure = clock();
                lastError = ex.Message;
            }

            log(message + ": " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Metadata cache for every combination of server, database and login.</summary>
    public sealed class MetadataCacheManager
    {
        private readonly ConcurrentDictionary<string, DatabaseMetadataCache> caches =
            new ConcurrentDictionary<string, DatabaseMetadataCache>(StringComparer.Ordinal);

        private readonly Action<string> log;

        public MetadataCacheManager(Action<string> log = null)
        {
            this.log = log;
        }

        private readonly ConcurrentDictionary<string, LinkedServerCache> linkedServers =
            new ConcurrentDictionary<string, LinkedServerCache>(StringComparer.Ordinal);

        /// <summary>Linked servers reachable from this connection; the database of the connection does not matter.</summary>
        public LinkedServerCache GetLinkedServers(ConnectionSettings settings) =>
            linkedServers.GetOrAdd(settings.WithDatabase(string.Empty).CacheKey, _ =>
            {
                var created = new LinkedServerCache(log);
                created.Loaded += (s, e) => LinkedServerLoaded?.Invoke(this, EventArgs.Empty);
                return created;
            });

        /// <summary>Raised (on a background thread) when any lazy load of a linked server has finished (#357).</summary>
        public event EventHandler LinkedServerLoaded;

        public DatabaseMetadataCache Get(ConnectionSettings settings) =>
            caches.GetOrAdd(settings.CacheKey, _ =>
            {
                var created = new DatabaseMetadataCache(log);
                created.SnapshotReplaced += (s, e) => SnapshotReplaced?.Invoke(this, EventArgs.Empty);
                created.ObjectsChanged += (s, e) => ObjectsChanged?.Invoke(this, EventArgs.Empty);
                created.LoadFailed += (s, e) => LoadFailed?.Invoke(this, EventArgs.Empty);
                return created;
            });

        /// <summary>Raised (on a background thread) when the objects of any database were loaded or reloaded after a change (#423).</summary>
        public event EventHandler SnapshotReplaced;

        /// <summary>Raised (on a background thread) when the load of the objects of any database failed (#470).</summary>
        public event EventHandler LoadFailed;

        /// <summary>As SnapshotReplaced, but only after a reload caused by a change (#423).</summary>
        public event EventHandler ObjectsChanged;

        public void InvalidateAll()
        {
            foreach (var linked in linkedServers.Values)
            {
                linked.Clear();
            }

            foreach (var cache in caches.Values)
            {
                cache.Invalidate();
            }
        }
    }
}
