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
    /// Metadata jedné databáze. Všechny metody se vrací hned: načítání běží na pozadí
    /// a výsledek je vidět v Current při dalším našeptání.
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
        private DateTime lastVersionCheck = DateTime.MinValue;

        public DatabaseMetadataCache(Action<string> log = null, Func<DateTime> clock = null)
        {
            this.log = log ?? (_ => { });
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>Po chybě se další pokus o načtení odloží.</summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Jak často se nejvýš ověřuje, jestli se objekty v databázi změnily.</summary>
        public TimeSpan VersionCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Aktuální snímek, nebo null, dokud se nenačte.</summary>
        public DatabaseSnapshot Current => snapshot;

        /// <summary>Spustí načtení objektů, pokud ještě neproběhlo. Vrací úlohu jen kvůli testům.</summary>
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
                loading = Task.Run(() => LoadObjectsAsync(loader, current));
                return loading;
            }
        }

        /// <summary>Spustí načtení sloupců objektů, které ještě nejsou v snímku ani se nenačítají.</summary>
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

        /// <summary>Nejvýš jednou za VersionCheckInterval ověří, jestli se objekty změnily, a případně snímek obnoví.</summary>
        public Task CheckForChanges(IMetadataLoader loader)
        {
            lock (gate)
            {
                var now = clock();
                if (snapshot == null || (loading != null && !loading.IsCompleted) || now - lastVersionCheck < VersionCheckInterval)
                {
                    return Task.CompletedTask;
                }

                lastVersionCheck = now;
                var current = generation;
                loading = Task.Run(() => RefreshIfChangedAsync(loader, current));
                return loading;
            }
        }

        /// <summary>Zahodí snímek; další našeptání načte metadata znovu.</summary>
        public void Invalidate()
        {
            lock (gate)
            {
                generation++;
                snapshot = null;
                loading = null;
                lastFailure = DateTime.MinValue;
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
                    lastVersionCheck = clock();
                }

                log("Metadata databáze " + loaded.DatabaseName + " načtena: " + loaded.Objects.Count + " objektů za " +
                    (int)(clock() - started).TotalMilliseconds + " ms.");
            }
            catch (Exception ex)
            {
                Fail("Načtení metadat selhalo", ex);
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
                    // Objekt, který mezitím zmizel, dostane prázdný seznam, aby se nenačítal pořád dokola.
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
                Fail("Načtení sloupců selhalo", ex);
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
                lock (gate)
                {
                    if (generation == expectedGeneration)
                    {
                        // Sloupce se po změně objektů načtou znovu podle potřeby.
                        snapshot = loaded;
                    }
                }

                log("Metadata databáze " + loaded.DatabaseName + " obnovena po změně objektů.");
            }
            catch (Exception ex)
            {
                Fail("Kontrola změn metadat selhala", ex);
            }
        }

        private void Fail(string message, Exception ex)
        {
            lock (gate)
            {
                lastFailure = clock();
            }

            log(message + ": " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Cache metadat pro každou kombinaci serveru, databáze a přihlášení.</summary>
    public sealed class MetadataCacheManager
    {
        private readonly ConcurrentDictionary<string, DatabaseMetadataCache> caches =
            new ConcurrentDictionary<string, DatabaseMetadataCache>(StringComparer.Ordinal);

        private readonly Action<string> log;

        public MetadataCacheManager(Action<string> log = null)
        {
            this.log = log;
        }

        public DatabaseMetadataCache Get(ConnectionSettings settings) =>
            caches.GetOrAdd(settings.CacheKey, _ => new DatabaseMetadataCache(log));

        public void InvalidateAll()
        {
            foreach (var cache in caches.Values)
            {
                cache.Invalidate();
            }
        }
    }
}
