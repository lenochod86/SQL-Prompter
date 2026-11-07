using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Completion;
using SqlPrompter.Metadata;

namespace SqlPrompter.Ssms
{
    /// <summary>A column of a table or view of the database (#206).</summary>
    internal sealed class ColumnRef
    {
        public ColumnRef(DefinitionRef owner, string column)
        {
            Owner = owner;
            Column = column;
        }

        public DefinitionRef Owner { get; }

        public string Column { get; }
    }

    /// <summary>
    /// Loads the database descriptions (MS_Description) of the object that is selected in the list, in the background, and keeps them for a short
    /// time so that moving through the list does not ask the database again (#206). Never blocks the list.
    /// </summary>
    internal static class DescriptionPreview
    {
        private const string ApplicationName = "SQL Prompter";
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
        private static readonly Dictionary<string, KeyValuePair<DateTime, ObjectDescriptions>> Cache = new Dictionary<string, KeyValuePair<DateTime, ObjectDescriptions>>();

        /// <summary>Calls <paramref name="done"/> on the UI thread with the text to show (null when the object has no description or the load failed).</summary>
        public static void Request(DefinitionRef reference, Action<string> done)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Load(reference, descriptions => done(descriptions == null ? null : DescriptionText.Format(descriptions.ObjectDescription, descriptions.Columns)));
        }

        /// <summary>Calls <paramref name="done"/> with all descriptions of the object and its columns (null when the load failed).</summary>
        public static void RequestAll(DefinitionRef reference, Action<ObjectDescriptions> done)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Load(reference, done);
        }

        /// <summary>Calls <paramref name="done"/> with the description of one column (null when it has none).</summary>
        public static void RequestColumn(ColumnRef column, Action<string> done)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Load(column.Owner, descriptions =>
            {
                var found = descriptions?.Columns.FirstOrDefault(c => string.Equals(c.Key, column.Column, StringComparison.OrdinalIgnoreCase));
                done(found == null || string.IsNullOrWhiteSpace(found.Value.Value) ? null : DescriptionText.Format(found.Value.Value, null));
            });
        }

        private static void Load(DefinitionRef reference, Action<ObjectDescriptions> done)
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
                ObjectDescriptions descriptions = null;
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    {
                        descriptions = await Task.Run(() => loader.LoadAsync(null, reference.Schema, reference.Name, timeout.Token)).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Loading the descriptions of " + reference.Schema + "." + reference.Name + " failed", ex);
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Cache[key] = new KeyValuePair<DateTime, ObjectDescriptions>(DateTime.UtcNow, descriptions);
                done(descriptions);
            }).Task.FileAndForget("SqlPrompter/DescriptionPreview");
#pragma warning restore VSSDK007
        }
    }
}