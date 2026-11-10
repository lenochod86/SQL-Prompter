using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>The object the definition shown next to the list belongs to.</summary>
    internal sealed class DefinitionRef
    {
        public DefinitionRef(string database, string schema, string name)
        {
            Database = database;
            Schema = schema;
            Name = name;
        }

        public string Database { get; }

        public string Schema { get; }

        public string Name { get; }
    }

    /// <summary>
    /// Loads the script of an object for the preview next to the list, in the background, and keeps what it loaded for a short time so
    /// that moving up and down the list does not ask the database again. Never blocks the list.
    /// </summary>
    internal static class DefinitionPreview
    {
        private const string ApplicationName = "SQL Prompter";
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
        private static readonly Dictionary<string, KeyValuePair<DateTime, string>> Cache = new Dictionary<string, KeyValuePair<DateTime, string>>();
        private static int generation;

        /// <summary>Forgets every loaded script (the objects changed or the user refreshed the metadata, #423); safe from any thread.</summary>
        public static void Invalidate() => System.Threading.Interlocked.Increment(ref generation);

        public static bool TryGetCached(DefinitionRef reference, out string script)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            script = null;
            var key = KeyOf(reference, out _);
            if (key == null || !Cache.TryGetValue(key, out var entry) || DateTime.UtcNow - entry.Key >= Lifetime)
            {
                return false;
            }

            script = entry.Value;
            return true;
        }

        /// <summary>Loads the script and calls <paramref name="done"/> on the UI thread with it (null when there is none or the load failed).</summary>
        public static void Request(DefinitionRef reference, Action<string> done)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var key = KeyOf(reference, out var target);
            if (key == null)
            {
                done(null);
                return;
            }

            var loader = new DefinitionLoader(() => SqlClientConnections.Create(target.BuildConnectionString(ApplicationName)));
#pragma warning disable VSSDK007 // fire and forget on purpose: the list must not wait for the database
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                string script = null;
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    {
                        var definition = await Task.Run(() => loader.LoadAsync(null, reference.Schema, reference.Name, timeout.Token)).ConfigureAwait(false);
                        script = definition?.Script;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Loading the definition of " + reference.Schema + "." + reference.Name + " for the preview failed", ex);
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Cache[key] = new KeyValuePair<DateTime, string>(DateTime.UtcNow, script);
                done(script);
            }).Task.FileAndForget("SqlPrompter/DefinitionPreview");
#pragma warning restore VSSDK007
        }

        private static string KeyOf(DefinitionRef reference, out ConnectionSettings target)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            target = null;
            var connection = SsmsConnection.GetActive();
            if (connection == null || !connection.CanConnect)
            {
                return null;
            }

            var database = string.IsNullOrEmpty(reference.Database) ? connection.Database : reference.Database;
            target = string.IsNullOrEmpty(database) ? connection : connection.WithDatabase(database);
            return System.Threading.Volatile.Read(ref generation) + "|" + target.CacheKey + "|" + reference.Schema + "." + reference.Name;
        }
    }
}
