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
        }

        /// <summary>Instance for the package commands; null until MEF creates the service.</summary>
        public static MetadataService Current { get; private set; }

        public MetadataCacheManager Caches { get; } = new MetadataCacheManager(Log.Write);

        /// <summary>
        /// Cache for the active window's connection; starts loading its snapshot in the background if there is none yet.
        /// Null when the window is not connected or the authentication type is not supported.
        /// </summary>
        public DatabaseMetadataCache CacheForActiveWindow(out IMetadataLoader loader)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            loader = null;
            var settings = SsmsConnection.GetActive();
            if (settings == null || !settings.CanConnect)
            {
                return null;
            }

            var cache = Caches.Get(settings);
            loader = CreateLoader(settings);
            if (cache.Current == null)
            {
                _ = cache.EnsureLoaded(loader);
            }

            return cache;
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
