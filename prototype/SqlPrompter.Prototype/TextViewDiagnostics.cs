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
    /// Otázka 1: pro každý nový textový pohled vypíše typ obsahu (i bázové typy),
    /// role pohledu, buffery v grafu a to, zda pro něj editor podporuje moderní
    /// (asynchronní) našeptávání.
    /// Otázka 2: sleduje všechny asynchronní relace našeptávání a vypisuje, kolik položek
    /// dodal který zdroj a které texty se opakují mezi zdroji.
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
            Log.Write("q1", "MEF komponenta TextViewDiagnostics vytvořena (MEF rozšíření se načetlo).");
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
                        "Nový pohled: typObsahu={0}; SQL={1}; asyncCompletionSupported={2}; role=[{3}]",
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
                    // Vlastnosti bufferu můžou prozradit, kde SSMS drží připojení okna (otázka 3).
                    foreach (var property in buffer.Properties.PropertyList)
                    {
                        Log.Write(
                            "q3",
                            string.Format(
                                "  vlastnost bufferu: klíč={0}; hodnota={1}",
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
            Log.Write("q2", "Asynchronní relace našeptávání spuštěna, typObsahu=\"" + contentType.TypeName + "\"");

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

                    Log.Write("q2", "  položky podle zdroje: " + signature);
                    Log.Write("q2", "  texty od více zdrojů (max 20): " + (duplicates.Count == 0 ? "žádné" : string.Join(", ", duplicates)));
                }
                catch (Exception ex)
                {
                    Log.Error("q2", "ItemsUpdated selhalo", ex);
                }
            };

            session.Dismissed += (s, args) => Log.Write("q2", "Asynchronní relace našeptávání zavřena.");
        }
    }
}
