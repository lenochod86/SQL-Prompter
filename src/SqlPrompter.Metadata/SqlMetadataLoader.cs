using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Model;
using DbColumn = SqlPrompter.Core.Model.DbColumn;

namespace SqlPrompter.Metadata
{
    public interface IMetadataLoader
    {
        /// <summary>Loads schemas, objects and the columns of user objects.</summary>
        Task<DatabaseSnapshot> LoadAsync(CancellationToken cancellationToken);

        /// <summary>Loads the columns of the given objects. An object that no longer exists is missing from the result.</summary>
        Task<IReadOnlyDictionary<int, IReadOnlyList<DbColumn>>> LoadColumnsAsync(IReadOnlyCollection<int> objectIds, CancellationToken cancellationToken);

        /// <summary>Fingerprint of the objects; a cheap query to find out whether the snapshot is stale.</summary>
        Task<string> GetVersionAsync(CancellationToken cancellationToken);
    }

    /// <summary>Reads metadata from the sys.* catalog views, read-only.</summary>
    public sealed class SqlMetadataLoader : IMetadataLoader
    {
        private const string ObjectsQuery = @"
SELECT DB_NAME(), SCHEMA_NAME();

SELECT name FROM sys.schemas WHERE schema_id < 16384 ORDER BY name;

SELECT o.object_id, s.name, o.name, o.type, o.is_ms_shipped
FROM sys.all_objects AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE o.type IN ('U', 'V', 'IF', 'TF', 'FT');

-- Columns of user objects right away, of system objects on demand.
SELECT c.object_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.columns AS c
JOIN sys.objects AS o ON o.object_id = c.object_id
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE o.type IN ('U', 'V', 'IF', 'TF', 'FT') AND o.is_ms_shipped = 0
ORDER BY c.object_id, c.column_id;";

        private const string VersionQuery = @"
SELECT CONCAT(COUNT(*), '|', CONVERT(varchar(30), MAX(modify_date), 126), '|', (SELECT COUNT(*) FROM sys.schemas))
FROM sys.objects;";

        private readonly Func<DbConnection> connectionFactory;

        /// <param name="connectionFactory">Returns a new, unopened connection to the target database.</param>
        public SqlMetadataLoader(Func<DbConnection> connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<DatabaseSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = ObjectsQuery;
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    string database = null;
                    string defaultSchema = null;
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        database = reader.IsDBNull(0) ? null : reader.GetString(0);
                        defaultSchema = reader.IsDBNull(1) ? null : reader.GetString(1);
                    }

                    var schemas = new List<string>();
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        schemas.Add(reader.GetString(0));
                    }

                    var objects = new List<DbObject>();
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        objects.Add(new DbObject(
                            reader.GetInt32(0),
                            reader.GetString(1),
                            reader.GetString(2),
                            ToKind(reader.GetString(3).Trim()),
                            reader.GetBoolean(4)));
                    }

                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    var columns = await ReadColumnsAsync(reader, cancellationToken).ConfigureAwait(false);

                    reader.Close();
                    var version = await QueryVersionAsync(connection, cancellationToken).ConfigureAwait(false);
                    return new DatabaseSnapshot(database, defaultSchema, schemas, objects, version, columns);
                }
            }
        }

        public async Task<IReadOnlyDictionary<int, IReadOnlyList<DbColumn>>> LoadColumnsAsync(IReadOnlyCollection<int> objectIds, CancellationToken cancellationToken)
        {
            var result = new Dictionary<int, IReadOnlyList<DbColumn>>();
            if (objectIds.Count == 0)
            {
                return result;
            }

            using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
            using (var command = connection.CreateCommand())
            {
                var names = new List<string>();
                foreach (var id in objectIds)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@p" + names.Count;
                    parameter.DbType = DbType.Int32;
                    parameter.Value = id;
                    command.Parameters.Add(parameter);
                    names.Add(parameter.ParameterName);
                }

                command.CommandText = @"
SELECT c.object_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.all_columns AS c
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id IN (" + string.Join(", ", names) + @")
ORDER BY c.object_id, c.column_id;";

                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    return await ReadColumnsAsync(reader, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Reads rows (object_id, name, type, max_length, precision, scale, is_nullable) ordered by object.</summary>
        private static async Task<IReadOnlyDictionary<int, IReadOnlyList<DbColumn>>> ReadColumnsAsync(DbDataReader reader, CancellationToken cancellationToken)
        {
            var lists = new Dictionary<int, List<DbColumn>>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var objectId = reader.GetInt32(0);
                if (!lists.TryGetValue(objectId, out var list))
                {
                    lists[objectId] = list = new List<DbColumn>();
                }

                list.Add(new DbColumn(
                    reader.GetString(1),
                    FormatType(reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5)),
                    reader.GetBoolean(6)));
            }

            return lists.ToDictionary(p => p.Key, p => (IReadOnlyList<DbColumn>)p.Value);
        }

        public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
        {
            using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                return await QueryVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static string FormatType(string type, short maxLength, byte precision, byte scale)
        {
            switch (type.ToLowerInvariant())
            {
                case "varchar":
                case "char":
                case "varbinary":
                case "binary":
                    return type + "(" + (maxLength < 0 ? "max" : maxLength.ToString()) + ")";
                case "nvarchar":
                case "nchar":
                    return type + "(" + (maxLength < 0 ? "max" : (maxLength / 2).ToString()) + ")";
                case "decimal":
                case "numeric":
                    return type + "(" + precision + "," + scale + ")";
                case "datetime2":
                case "datetimeoffset":
                case "time":
                    return type + "(" + scale + ")";
                default:
                    return type;
            }
        }

        private static DbObjectKind ToKind(string type)
        {
            switch (type)
            {
                case "V":
                    return DbObjectKind.View;
                case "IF":
                case "TF":
                case "FT":
                    return DbObjectKind.TableFunction;
                default:
                    return DbObjectKind.Table;
            }
        }

        private static async Task<string> QueryVersionAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = VersionQuery;
                var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return value?.ToString();
            }
        }

        private async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
        {
            var connection = connectionFactory();
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }
}
