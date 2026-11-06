using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Completion;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Loads the database descriptions (MS_Description) of the object that is selected in the list, in the background, and keeps them for a short
    /// time so that moving through the list does not ask the database again (#206). Never blocks the list.
    /// </summary>
    internal static class DescriptionPreview
    {
        private const string ApplicationName = "SQL Prompter";
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
        private static readonly Dictionary<string, KeyValuePair<DateTime, string>> Cache = new Dictionary<string, KeyValuePair<DateTime, string>>();

        /// <summary>Calls <paramref name="done"/> on the UI thread with the text to show (null when the object has no description or the load failed).</summary>
        public static void Request(DefinitionRef reference, Action<string> done)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var connection = SsmsConnection.GetActive();
            if (connection == null || !connection.CanConnect)
            {
                done(null);
                return;
            }

            var database = string.IsNullOrEmpty(reference.Database) ? connection.Database : reference.Database;
            var target = string.IsNullOrEmpty(database) ? connection : connection.WithDatabase(database);
            var key = target.CacheKey + "|" + reference.Schema + "." + reference.Name;
            if (Cache.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.Key < Lifetime)
            {
                done(entry.Value);
                return;
            }

            var loader = new DescriptionLoader(() => SqlClientConnections.Create(target.BuildConnectionString(ApplicationName)));
#pragma warning disable VSSDK007 // fire and forget on purpose: the list must not wait for the database
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                string text = null;
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    {
                        var descriptions = await Task.Run(() => loader.LoadAsync(null, reference.Schema, reference.Name, timeout.Token)).ConfigureAwait(false);
                        text = DescriptionText.Format(descriptions.ObjectDescription, descriptions.Columns);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Loading the descriptions of " + reference.Schema + "." + reference.Name + " failed", ex);
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Cache[key] = new KeyValuePair<DateTime, string>(DateTime.UtcNow, text);
                done(text);
            }).Task.FileAndForget("SqlPrompter/DescriptionPreview");
#pragma warning restore VSSDK007
        }
    }
}
