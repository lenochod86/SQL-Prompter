using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;
using SqlPrompter.Core.Navigation;

namespace SqlPrompter.Ssms
{
    /// <summary>The tooltip over a name (#220): what an alias, table, column, variable, function or procedure is, from the loaded metadata.</summary>
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("SqlPrompter.QuickInfo")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = "Default Quick Info Presenter")]
    internal sealed class HoverQuickInfoProvider : IAsyncQuickInfoSourceProvider
    {
        [Import]
        internal MetadataService Metadata { get; set; }

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) => new HoverQuickInfoSource(Metadata);
    }

    internal sealed class HoverQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly MetadataService metadata;

        public HoverQuickInfoSource(MetadataService metadata)
        {
            this.metadata = metadata;
        }

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            try
            {
                if (!SettingsService.Current.Behavior.ShowHover)
                {
                    return null;
                }

                var snapshot = session.TextView.TextSnapshot;
                var point = session.GetTriggerPoint(snapshot);
                if (point == null)
                {
                    return null;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var position = point.Value.Position;
                if (!SqlBatch.TryGet(snapshot, position, out var batchStart, out var batch))
                {
                    return null;
                }

                var cache = metadata?.CacheForActiveWindow(out _, SqlBatch.FindUsedDatabase(snapshot, position));
                var info = HoverInfo.At(batch, position - batchStart, cache?.Current);
                if (info == null)
                {
                    return null;
                }

                var span = snapshot.CreateTrackingSpan(batchStart + info.Start, info.Length, SpanTrackingMode.EdgeInclusive);
                var lines = info.Text.Split('\n').Select(l => (object)new ClassifiedTextElement(new ClassifiedTextRun("natural language", l))).ToArray();
                return new QuickInfoItem(span, new ContainerElement(ContainerElementStyle.Stacked, lines));
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Building the tooltip failed", ex);
                return null;
            }
        }

        public void Dispose()
        {
        }
    }
}
