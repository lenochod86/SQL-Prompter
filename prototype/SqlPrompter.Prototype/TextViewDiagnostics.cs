using System;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlPrompter.Prototype
{
    /// <summary>
    /// Question 1: for every new text view logs the content type (including base types),
    /// the view roles, the buffers in the graph and whether the editor supports modern
    /// (asynchronous) completion for it.
    /// Question 2: watches all asynchronous completion sessions and logs how many items
    /// each source provided and which texts repeat across sources.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("any")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    internal sealed class TextViewDiagnostics : IWpfTextViewCreationListener
    {
        private readonly IAsyncCompletionBroker asyncBroker;

        [ImportingConstructor]
        public TextViewDiagnostics(IAsyncCompletionBroker asyncBroker)
        {
            this.asyncBroker = asyncBroker;
            asyncBroker.CompletionTriggered += OnCompletionTriggered;
            Log.Write("q1", "MEF component TextViewDiagnostics created (the MEF extension loaded).");
        }

        public void TextViewCreated(IWpfTextView textView)
        {
            try
            {
                var buffer = textView.TextBuffer;
                var isSql = SqlContentTypes.IsSql(buffer.ContentType);
                Log.Write(
                    "q1",
                    string.Format(
                        "New view: contentType={0}; SQL={1}; asyncCompletionSupported={2}; roles=[{3}]",
                        SqlContentTypes.Describe(buffer.ContentType),
                        isSql,
                        asyncBroker.IsCompletionSupported(buffer.ContentType),
                        string.Join(",", textView.Roles)));

                var graphBuffers = textView.BufferGraph.GetTextBuffers(_ => true);
                if (graphBuffers.Count > 1)
                {
                    foreach (var b in graphBuffers)
                    {
                        Log.Write("q1", "  buffer v grafu: " + SqlContentTypes.Describe(b.ContentType));
                    }
                }

                if (isSql)
                {
                    // Buffer properties may reveal where SSMS keeps the window connection (question 3).
                    foreach (var property in buffer.Properties.PropertyList)
                    {
                        Log.Write(
                            "q3",
                            string.Format(
                                "  buffer property: key={0}; value={1}",
                                DescribeKey(property.Key),
                                property.Value?.GetType().FullName ?? "(null)"));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("q1", "TextViewCreated selhalo", ex);
            }
        }

        private static string DescribeKey(object key)
        {
            if (key is Type type)
            {
                return "typeof(" + type.FullName + ")";
            }

            return key == null ? "(null)" : key.GetType().FullName + " \"" + key + "\"";
        }

        private static void OnCompletionTriggered(object sender, CompletionTriggeredEventArgs e)
        {
            var session = e.CompletionSession;
            var contentType = e.TextView.TextBuffer.ContentType;
            Log.Write("q2", "Asynchronous completion session started, contentType=\"" + contentType.TypeName + "\"");

            string lastSignature = null;
            session.ItemsUpdated += (s, args) =>
            {
                try
                {
                    var items = args.Items.Items.ToList();
                    var bySource = items
                        .GroupBy(i => i.Source?.GetType().FullName ?? "(bez zdroje)")
                        .Select(g => g.Key + "=" + g.Count())
                        .OrderBy(x => x)
                        .ToList();
                    var signature = string.Join("; ", bySource);
                    if (signature == lastSignature)
                    {
                        return;
                    }

                    lastSignature = signature;
                    var duplicates = items
                        .GroupBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Select(i => i.Source).Distinct().Count() > 1)
                        .Select(g => g.Key)
                        .Take(20)
                        .ToList();

                    Log.Write("q2", "  items by source: " + signature);
                    Log.Write("q2", "  texts from several sources (max 20): " + (duplicates.Count == 0 ? "none" : string.Join(", ", duplicates)));
                }
                catch (Exception ex)
                {
                    Log.Error("q2", "ItemsUpdated selhalo", ex);
                }
            };

            session.Dismissed += (s, args) => Log.Write("q2", "Asynchronous completion session dismissed.");
        }
    }
}
