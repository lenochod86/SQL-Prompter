using System.ComponentModel.Composition;
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
        }

        /// <summary>Raised on a background thread when a lazy load of a linked server has finished (#357); an open list asks again.</summary>
        public event System.EventHandler LinkedServerLoaded;

        /// <summary>Instance for the package commands; null until MEF creates the service.</summary>
        public static MetadataService Current { get; private set; }

        public MetadataCacheManager Caches { get; } = new MetadataCacheManager(Log.Write);

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

            return cache;
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
            return source => source.NameParts.Count == 4
                ? linked.StatusOfColumns(source.NameParts[0], source.NameParts[1], source.NameParts[2], source.NameParts[3])
                : SqlPrompter.Metadata.LinkedLoadStatus.Done;
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
            return source => source.NameParts.Count == 4
                ? linked.GetColumns(source.NameParts[0], source.NameParts[1], source.NameParts[2], source.NameParts[3], loader)
                : null;
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
            textView.GotAggregateFocus += (sender, args) =>
            {
                try
                {
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
}
