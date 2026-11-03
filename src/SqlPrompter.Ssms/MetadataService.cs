using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>Sdílená cache metadat pro všechna okna SSMS.</summary>
    [Export(typeof(MetadataService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    internal sealed class MetadataService
    {
        private const string ApplicationName = "SQL Prompter";

        public MetadataService()
        {
            Current = this;
        }

        /// <summary>Instance pro příkazy balíčku; null, dokud MEF službu nevytvoří.</summary>
        public static MetadataService Current { get; private set; }

        public MetadataCacheManager Caches { get; } = new MetadataCacheManager(Log.Write);

        /// <summary>
        /// Cache pro připojení aktivního okna; pokud ještě nemá snímek, spustí jeho načtení na pozadí.
        /// Null, když okno není připojené nebo typ ověření nepodporujeme.
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
    /// Po aktivaci SQL okna začne načítat metadata jeho databáze, aby byla připravená
    /// dřív, než uživatel poprvé vyvolá našeptávání.
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
                    Log.Error("Předběžné načtení metadat selhalo", ex);
                }
            };
        }
    }
}
