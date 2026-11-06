using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Navigation;

namespace SqlPrompter.Metadata
{
    /// <summary>A module (procedure, function, view or trigger) that contains the searched text, with its matching lines.</summary>
    public sealed class CodeSearchHit
    {
        public CodeSearchHit(string schema, string name, string typeCode, IReadOnlyList<CodeMatch> matches)
        {
            Schema = schema;
            Name = name;
            TypeCode = typeCode;
            Matches = matches;
        }

        public string Schema { get; }

        public string Name { get; }

        /// <summary>P, FN, IF, TF, V or TR.</summary>
        public string TypeCode { get; }

        public IReadOnlyList<CodeMatch> Matches { get; }
    }

    public sealed class CodeSearchResult
    {
        public CodeSearchResult(IReadOnlyList<CodeSearchHit> hits, int searched, int encrypted)
        {
            Hits = hits;
            Searched = searched;
            Encrypted = encrypted;
        }

        public IReadOnlyList<CodeSearchHit> Hits { get; }

        /// <summary>How many modules were read.</summary>
        public int Searched { get; }

        /// <summary>How many modules have no readable text (encrypted) and were skipped.</summary>
        public int Encrypted { get; }
    }

    /// <summary>Searches the text of the modules of a database (<c>sys.sql_modules</c>), read-only, on a connection of its own (#212).</summary>
    public sealed class CodeSearchLoader
    {
        private readonly Func<DbConnection> connectionFactory;

        public CodeSearchLoader(Func<DbConnection> connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<CodeSearchResult> SearchAsync(string database, Regex regex, CancellationToken cancellationToken)
        {
            var hits = new List<CodeSearchHit>();
            var searched = 0;
            var encrypted = 0;
            var prefix = string.IsNullOrEmpty(database) ? string.Empty : "[" + database.Replace("]", "]]") + "].";
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT s.name, o.name, RTRIM(o.type), m.definition FROM " + prefix + "sys.sql_modules AS m "
                        + "JOIN " + prefix + "sys.objects AS o ON o.object_id = m.object_id "
                        + "JOIN " + prefix + "sys.schemas AS s ON s.schema_id = o.schema_id "
                        + "WHERE o.is_ms_shipped = 0 AND o.type IN ('P', 'FN', 'IF', 'TF', 'V', 'TR') ORDER BY s.name, o.name";
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            if (reader.IsDBNull(3))
                            {
                                encrypted++;
                                continue;
                            }

                            searched++;
                            var matches = CodeSearch.Find(reader.GetString(3), regex);
                            if (matches.Count > 0)
                            {
                                hits.Add(new CodeSearchHit(reader.GetString(0), reader.GetString(1), reader.GetString(2), matches));
                            }
                        }
                    }
                }
            }

            return new CodeSearchResult(hits, searched, encrypted);
        }
    }
}
