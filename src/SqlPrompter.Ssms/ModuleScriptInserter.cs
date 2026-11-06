using System;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Navigation;

namespace SqlPrompter.Ssms
{
    /// <summary>The item of <c>ALTER PROCEDURE |</c> and the like: where the statement starts, and what to turn the definition into.</summary>
    internal sealed class ModuleEdit
    {
        public ModuleEdit(ITrackingPoint headerStart, ModuleScript script, string database)
        {
            HeaderStart = headerStart;
            Script = script;
            Database = database;
        }

        public ITrackingPoint HeaderStart { get; }

        public ModuleScript Script { get; }

        public string Database { get; }
    }

    /// <summary>
    /// Committing a module after ALTER, CREATE OR ALTER: the name is inserted at once, and when the definition has been read in the background
    /// the whole statement (from ALTER to the name) is replaced by it. The database is never waited for; if the text has been edited in the
    /// meantime, or the module has no script (encrypted, no permission), the name stays and a message is shown in the status bar.
    /// </summary>
    internal static class ModuleScriptInserter
    {
        public static void Insert(ITextView view, ITrackingSpan applicable, ITextBuffer buffer, CompletionItem item, ModuleEdit edit)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var span = applicable.GetSpan(buffer.CurrentSnapshot);
            buffer.Replace(span, item.InsertText);
            var snapshot = buffer.CurrentSnapshot;
            var end = span.Start.Position + item.InsertText.Length;
            view.Caret.MoveTo(new SnapshotPoint(snapshot, end));

            var start = edit.HeaderStart.GetPosition(snapshot);
            if (start > end)
            {
                return;
            }

            var expected = snapshot.GetText(start, end - start);
            var region = snapshot.CreateTrackingSpan(start, end - start, SpanTrackingMode.EdgeExclusive);
            var module = edit.Script;
            SetStatus("SQL Prompter: loading " + module.Name + "...");
            DefinitionPreview.Request(new DefinitionRef(edit.Database, module.Schema, module.Name), definition =>
            {
                try
                {
                    Replace(view, buffer, region, expected, module, definition);
                }
                catch (Exception ex)
                {
                    Log.Error("Inserting the definition of " + module.Name + " failed", ex);
                }
            });
        }

        private static void Replace(ITextView view, ITextBuffer buffer, ITrackingSpan region, string expected, ModuleScript module, string definition)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(definition))
            {
                SetStatus("SQL Prompter: " + module.Name + " has no script (encrypted, or no permission to see it).");
                return;
            }

            var current = buffer.CurrentSnapshot;
            var span = region.GetSpan(current);
            if (span.GetText() != expected)
            {
                SetStatus("SQL Prompter: the text changed while the definition of " + module.Name + " was loading; it was not inserted.");
                return;
            }

            var script = DefinitionScript.ToAlter(definition, module.CreateOrAlter);
            buffer.Replace(span, script);
            view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, span.Start.Position + DefinitionScript.BodyStart(script)));
            view.Caret.EnsureVisible();
            SetStatus("SQL Prompter: inserted the definition of " + module.Name + ".");
        }

        private static void SetStatus(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText(text);
            }
        }
    }
}
