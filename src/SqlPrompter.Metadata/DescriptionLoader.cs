using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SqlPrompter.Metadata
{
    /// <summary>The descriptions of one object and of its columns (extended property MS_Description).</summary>
    public sealed class ObjectDescriptions
    {
        public ObjectDescriptions(string objectDescription, IReadOnlyList<KeyValuePair<string, string>> columns)
        {
            ObjectDescription = objectDescription;
            Columns = columns;
        }

        /// <summary>Null when the object has none.</summary>
        public string ObjectDescription { get; }

        /// <summary>The described columns in column order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Columns { get; }
    }

    /// <summary>Reads <c>MS_Description</c> of one object and its columns from <c>sys.extended_properties</c>, read-only, on a connection of its own (#206).</summary>
    public sealed class DescriptionLoader
    {
        private readonly Func<DbConnection> connectionFactory;

        public DescriptionLoader(Func<DbConnection> connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<ObjectDescriptions> LoadAsync(string database, string schema, string name, CancellationToken cancellationToken)
        {
            var prefix = string.IsNullOrEmpty(database) ? string.Empty : "[" + database.Replace("]", "]]") + "].";
            var fullName = (string.IsNullOrEmpty(database) ? string.Empty : "[" + database.Replace("]", "]]") + "].") + "[" + (schema ?? "dbo").Replace("]", "]]") + "].[" + name.Replace("]", "]]") + "]";
            string main = null;
            var columns = new List<KeyValuePair<string, string>>();
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT c.name, CONVERT(nvarchar(max), ep.value) FROM " + prefix + "sys.extended_properties AS ep "
                        + "LEFT JOIN " + prefix + "sys.columns AS c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id AND ep.minor_id > 0 "
                        + "WHERE ep.class = 1 AND ep.name = N'MS_Description' AND ep.major_id = OBJECT_ID(@full) ORDER BY ep.minor_id";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@full";
                    parameter.Value = fullName;
                    command.Parameters.Add(parameter);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            var value = reader.IsDBNull(1) ? null : reader.GetString(1);
                            if (reader.IsDBNull(0))
                            {
                                main = value;
                            }
                            else
                            {
                                columns.Add(new KeyValuePair<string, string>(reader.GetString(0), value));
                            }
                        }
                    }
                }
            }

            return new ObjectDescriptions(main, columns);
        }
    }
}
