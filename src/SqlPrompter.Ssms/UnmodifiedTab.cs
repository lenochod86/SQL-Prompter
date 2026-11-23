using System;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// A tab opened with a text the user did not write (a definition, #512) is marked as saved: no <c>*</c> in the caption and no question when it is closed
    /// unchanged. The mark is set after the editor has processed the insert (it is set again by the pending change events otherwise), so it is applied twice,
    /// shortly and a little later. The text is left alone when the user has already changed it.
    /// </summary>
    internal static class UnmodifiedTab
    {
        /// <summary>Marks the window of <paramref name="document"/>, which holds <paramref name="text"/>, as unmodified; returns at once, the work happens on the UI thread later.</summary>
        public static void Schedule(string document, string text)
        {
            if (string.IsNullOrEmpty(document))
            {
                return;
            }

#pragma warning disable VSSDK007 // fire and forget on purpose
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                var first = true;
                foreach (var delay in new[] { 300, 1500 })
                {
                    await Task.Delay(delay);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    Apply(document, text, first);
                    first = false;
                }
            }).Task.FileAndForget("sqlprompter/unmodifiedtab");
#pragma warning restore VSSDK007
        }

        private static void Apply(string document, string text, bool discardUndo)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                Document found = null;
                foreach (Document candidate in dte?.Documents.Cast<Document>().ToList() ?? new System.Collections.Generic.List<Document>())
                {
                    if (string.Equals(candidate.FullName, document, StringComparison.OrdinalIgnoreCase))
                    {
                        found = candidate;
                        break;
                    }
                }

                if (found == null)
                {
                    return;
                }

                // Only while the text is still the one we inserted: a tab the user has started to edit stays modified.
                var view = string.Equals(dte.ActiveDocument?.FullName, document, StringComparison.OrdinalIgnoreCase) ? ActiveEditor.GetView() : null;
                if (view != null && !string.Equals(view.TextBuffer.CurrentSnapshot.GetText(), text, StringComparison.Ordinal))
                {
                    return;
                }

                var model = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
                if (view != null)
                {
                    if (discardUndo)
                    {
                        // Ctrl+Z right after opening must not blank the tab: the insert is not an edit of the user.
                        var adapter = model?.GetService<IVsEditorAdaptersFactoryService>()?.GetBufferAdapter(view.TextBuffer) as IVsTextBuffer;
                        if (adapter != null && adapter.GetUndoManager(out var undo) == 0 && undo != null)
                        {
                            undo.DiscardFrom(null);
                        }
                    }

                    var textDocument = model?.GetService<ITextDocumentFactoryService>() is ITextDocumentFactoryService factory && factory.TryGetTextDocument(view.TextBuffer, out var known) ? known : null;
                    textDocument?.UpdateDirtyState(false, DateTime.UtcNow);
                }

                found.Saved = true;
                Log.Write("Opened tab " + System.IO.Path.GetFileName(document) + " marked as unmodified (Saved=" + found.Saved + ").");
            }
            catch (Exception ex)
            {
                Log.Error("Marking the opened tab as unmodified failed", ex);
            }
        }
    }
}
