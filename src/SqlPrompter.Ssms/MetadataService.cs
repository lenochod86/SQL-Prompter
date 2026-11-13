using System.ComponentModel.Composition;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>Metadata cache shared by all SSMS windows.</summary>
    [Export(typeof(MetadataService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    internal sealed class MetadataService
    {
        private const string ApplicationName = "SQL Prompter";

        public MetadataService()
        {
            Current = this;
            Caches.LinkedServerLoaded += (s, e) => LinkedServerLoaded?.Invoke(this, e);
            Caches.LoadFailed += (s, e) => LinkedServerLoaded?.Invoke(this, e);
            Caches.ObjectsChanged += (s, e) => RefreshNow.RefreshSsmsIntelliSense();
            Caches.SnapshotReplaced += (s, e) =>
            {
                DefinitionPreview.Invalidate();
                SnapshotReplaced?.Invoke(null, e);
                // A list that waits for the objects of another database (#443) is made again.
                LinkedServerLoaded?.Invoke(this, e);
            };
        }

        /// <summary>Raised on a background thread when a lazy load of a linked server has finished (#357); an open list asks again.</summary>
        public event System.EventHandler LinkedServerLoaded;

        /// <summary>Raised on a background thread when the objects of a database were loaded or reloaded after a change (#423); the code analysis runs again.</summary>
        public static event System.EventHandler SnapshotReplaced;

        /// <summary>Instance for the package commands; null until MEF creates the service.</summary>
        public static MetadataService Current { get; private set; }

        public MetadataCacheManager Caches { get; } = new MetadataCacheManager(Log.Write);

        /// <summary>The metadata that the active window had loaded when it was last seen (never loads, never waits); null when there is none. Safe from any thread.</summary>
        public SqlPrompter.Core.Model.IDatabaseMetadata CachedMetadata => Volatile.Read(ref lastMetadata);

        private SqlPrompter.Core.Model.IDatabaseMetadata lastMetadata;

        /// <summary>
        /// Cache for the active window's connection; starts loading its snapshot in the background if there is none yet.
        /// Null when the window is not connected or the authentication type is not supported.
        /// </summary>
        /// <param name="database">Database chosen by USE in the script, if it differs from the window's.</param>
        public DatabaseMetadataCache CacheForActiveWindow(out IMetadataLoader loader, string database = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            loader = null;
            var settings = SsmsConnection.GetActive();
            if (settings == null || !settings.CanConnect)
            {
                Volatile.Write(ref lastMetadata, null);
                return null;
            }

            if (!string.IsNullOrEmpty(database) && !string.Equals(database, settings.Database, System.StringComparison.OrdinalIgnoreCase))
            {
                settings = settings.WithDatabase(database);
            }

            var cache = Caches.Get(settings);
            loader = CreateLoader(settings);
            if (cache.Current == null)
            {
                _ = cache.EnsureLoaded(loader);
            }

            // Notice changes made elsewhere (another tab, another connection, outside SSMS); at most once per interval (#423).
            if (cache.Current != null)
            {
                _ = cache.CheckForChanges(loader);
            }

            // Whatever window is active now decides which metadata the analysis may use (none while it has none): never the one of another connection.
            Volatile.Write(ref lastMetadata, cache.Current);
            return cache;
        }

        /// <summary>
        /// The loaded metadata of the database a script works in (the last USE line of the text, else the database of the window), for the code analysis (#208);
        /// null while it is not loaded (loading starts, nothing is waited for). Remembered for <see cref="CachedMetadata"/>.
        /// </summary>
        public SqlPrompter.Core.Model.IDatabaseMetadata MetadataForScript(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string database = null;
            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                if (line.Length <= 300 && SqlPrompter.Core.Analysis.UseStatement.TryParse(line.TrimEnd('\r'), out var used))
                {
                    database = used;
                }
            }

            return CacheForActiveWindow(out _, database)?.Current;
        }

        /// <summary>
        /// Databases of a linked server (database null) or the objects of one of its databases, for the active window's connection.
        /// Null until loaded; the loading starts here, in the background, and never blocks.
        /// </summary>
        public RemoteSnapshot LinkedMetadata(string server, string database)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return LinkedMetadata(server, database, out _);
        }

        /// <summary>As above, with the state of the load (loading, failed with a reason, done) for the indication in the list.</summary>
        public RemoteSnapshot LinkedMetadata(string server, string database, out LinkedLoadStatus status)
        {
            status = LinkedLoadStatus.Loading;
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = SsmsConnection.GetActive();
            if (settings == null || !settings.CanConnect)
            {
                return null;
            }

            var linked = Caches.GetLinkedServers(settings);
            var loader = CreateLoader(settings);
            var loaded = database == null ? linked.GetServer(server, loader) : linked.GetDatabase(server, database, loader);
            status = loaded != null ? LinkedLoadStatus.Done : database == null ? linked.StatusOfServer(server) : linked.StatusOfDatabase(server, database);
            return loaded;
        }

        /// <summary>
        /// Columns of tables on linked servers for the active window's connection, as a function that is safe to call off the UI thread:
        /// it returns what is cached (null while loading, which it starts) and never waits.
        /// </summary>
        public System.Func<SqlPrompter.Core.Analysis.TableSource, System.Collections.Generic.IReadOnlyList<SqlPrompter.Core.Model.DbColumn>> LinkedColumns()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return LinkedColumns(out _);
        }

        /// <summary>As above, with a function that tells where the load of the columns of a four-part table stands (for the "Loading..." item, #358).</summary>
        public System.Func<SqlPrompter.Core.Analysis.TableSource, SqlPrompter.Metadata.LinkedLoadStatus> LinkedColumnStatus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = SsmsConnection.GetActive();
            if (settings == null || !settings.CanConnect)
            {
                return null;
            }

            var linked = Caches.GetLinkedServers(settings);
            var other = new OtherDatabases(Caches, settings, CreateLoader);
            return source => source.NameParts.Count == 4
                ? linked.StatusOfColumns(source.NameParts[0], source.NameParts[1], source.NameParts[2], source.NameParts[3])
                : source.NameParts.Count == 3 ? other.ColumnStatus(source) : SqlPrompter.Metadata.LinkedLoadStatus.Done;
        }

        private System.Func<SqlPrompter.Core.Analysis.TableSource, System.Collections.Generic.IReadOnlyList<SqlPrompter.Core.Model.DbColumn>> LinkedColumns(out System.Func<SqlPrompter.Core.Analysis.TableSource, SqlPrompter.Metadata.LinkedLoadStatus> status)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            status = null;
            var settings = SsmsConnection.GetActive();
            if (settings == null || !settings.CanConnect)
            {
                return null;
            }

            var linked = Caches.GetLinkedServers(settings);
            var loader = CreateLoader(settings);
            var other = new OtherDatabases(Caches, settings, CreateLoader);
            return source => source.NameParts.Count == 4
                ? linked.GetColumns(source.NameParts[0], source.NameParts[1], source.NameParts[2], source.NameParts[3], loader)
                : source.NameParts.Count == 3 ? other.Columns(source) : null;
        }

        /// <summary>
        /// The other databases of the active window's server (three-part names, #443) for the active window's connection, or null without a connection.
        /// Safe to use off the UI thread; it never waits, it starts the loading of a database in the background.
        /// </summary>
        public OtherDatabases OtherDatabases()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = SsmsConnection.GetActive();
            return settings == null || !settings.CanConnect ? null : new OtherDatabases(Caches, settings, CreateLoader);
        }

        private IMetadataLoader CreateLoader(ConnectionSettings settings) =>
            new SqlMetadataLoader(() => SqlClientConnections.Create(settings.BuildConnectionString(ApplicationName)));
    }

    /// <summary>
    /// When a SQL window is activated, starts loading the metadata of its database so that it is ready
    /// before the user first invokes completion.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class MetadataPreloader : IWpfTextViewCreationListener
    {
        [Import]
        internal MetadataService Metadata { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            EditorTabSize.Watch(textView);
            textView.GotAggregateFocus += (sender, args) =>
            {
                try
                {
                    EditorTabSize.Remember(textView);
                    ThreadHelper.ThrowIfNotOnUIThread();
                    Metadata.CacheForActiveWindow(out _);
                }
                catch (System.Exception ex)
                {
                    Log.Error("Preloading metadata failed", ex);
                }
            };
        }
    }

    /// <summary>
    /// The metadata of databases of the connection's server other than the one of the window: the snapshot of a database is loaded the first time one of its
    /// objects is used (the same way as for the window's own database) and its tables give their columns, definitions and tooltips (#443).
    /// </summary>
    internal sealed class OtherDatabases
    {
        private readonly MetadataCacheManager caches;
        private readonly ConnectionSettings settings;
        private readonly System.Func<ConnectionSettings, IMetadataLoader> createLoader;

        public OtherDatabases(MetadataCacheManager caches, ConnectionSettings settings, System.Func<ConnectionSettings, IMetadataLoader> createLoader)
        {
            this.caches = caches;
            this.settings = settings;
            this.createLoader = createLoader;
        }

        private DatabaseMetadataCache Cache(string database, out IMetadataLoader loader)
        {
            var other = settings.WithDatabase(database);
            var cache = caches.Get(other);
            loader = createLoader(other);
            if (cache.Current == null)
            {
                _ = cache.EnsureLoaded(loader);
            }

            return cache;
        }

        /// <summary>The loaded objects of the database, or null while they load (the loading starts) or after a failure.</summary>
        public DatabaseSnapshot Snapshot(string database) => string.IsNullOrEmpty(database) ? null : Cache(database, out _).Current;

        public SqlPrompter.Metadata.LinkedLoadStatus Status(string database) => string.IsNullOrEmpty(database) ? SqlPrompter.Metadata.LinkedLoadStatus.Done : Cache(database, out _).Status;

        /// <summary>The columns of a table of another database (database.schema.table), or null while they load.</summary>
        public System.Collections.Generic.IReadOnlyList<SqlPrompter.Core.Model.DbColumn> Columns(SqlPrompter.Core.Analysis.TableSource source)
        {
            var cache = Cache(source.Database, out var loader);
            var snapshot = cache.Current;
            if (snapshot == null)
            {
                return null;
            }

            var found = SqlPrompter.Core.Completion.Readiness.Resolve(source, snapshot);
            if (found == null)
            {
                return null;
            }

            var columns = snapshot.GetColumns(found);
            if (columns == null)
            {
                _ = cache.RequestColumns(new[] { found }, loader);
            }

            return columns;
        }

        /// <summary>Where the load of the columns of a table of another database stands: its database first, then (for system objects) the columns.</summary>
        public SqlPrompter.Metadata.LinkedLoadStatus ColumnStatus(SqlPrompter.Core.Analysis.TableSource source)
        {
            var cache = Cache(source.Database, out _);
            var snapshot = cache.Current;
            if (snapshot == null)
            {
                return cache.Status;
            }

            var found = SqlPrompter.Core.Completion.Readiness.Resolve(source, snapshot);
            return found == null || snapshot.GetColumns(found) != null ? SqlPrompter.Metadata.LinkedLoadStatus.Done : SqlPrompter.Metadata.LinkedLoadStatus.Loading;
        }
    }
}
