using System.ComponentModel.Composition;
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

        public IMetadataLoader CreateLoader(ConnectionSettings settings) =>
            new SqlMetadataLoader(() => SqlClientConnections.Create(settings.BuildConnectionString(ApplicationName)));
    }
}
