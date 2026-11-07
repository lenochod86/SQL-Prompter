using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Sql;
using DbColumn = SqlPrompter.Core.Model.DbColumn;
using DbParameter = SqlPrompter.Core.Model.DbParameter;

namespace SqlPrompter.Metadata
{
    public interface IMetadataLoader
    {
        /// <summary>Loads schemas, objects and the columns of user objects.</summary>
        Task<DatabaseSnapshot> LoadAsync(CancellationToken cancellationToken);

        /// <summary>Loads the columns of the given objects. An object that no longer exists is missing from the result.</summary>
        Task<IReadOnlyDictionary<int, IReadOnlyList<DbColumn>>> LoadColumnsAsync(IReadOnlyCollection<int> objectIds, CancellationToken cancellationToken);

        /// <summary>Names of the databases of a linked server. Slow and may fail: only called lazily, in the background.</summary>
        Task<IReadOnlyList<string>> LoadLinkedDatabasesAsync(string linkedServer, CancellationToken cancellationToken);

        /// <summary>Columns of one table of a linked server, loaded when the table is used.</summary>
        Task<IReadOnlyList<DbColumn>> LoadLinkedColumnsAsync(string linkedServer, string database, string schema, string table, CancellationToken cancellationToken);

        /// <summary>Tables and views of one database of a linked server.</summary>
        Task<IReadOnlyList<RemoteObject>> LoadLinkedObjectsAsync(string linkedServer, string database, CancellationToken cancellationToken);

        /// <summary>Fingerprint of the objects; a cheap query to find out whether the snapshot is stale.</summary>
        Task<string> GetVersionAsync(CancellationToken cancellationToken);
    }

    /// <summary>Reads metadata from the sys.* catalog views, read-only.</summary>
    public sealed class SqlMetadataLoader : IMetadataLoader
    {
        private const string ObjectsQuery = @"
SELECT DB_NAME(), SCHEMA_NAME();

SELECT name FROM sys.schemas WHERE schema_id < 16384 ORDER BY name;

SELECT o.object_id, s.name, o.name, o.type, o.is_ms_shipped, sn.base_object_name,
    CASE WHEN o.type IN ('IF', 'TF', 'FT', 'FN', 'FS')
        THEN (SELECT COUNT(*) FROM sys.all_parameters AS p WHERE p.object_id = o.object_id AND p.parameter_id > 0)
        ELSE 0 END
FROM sys.all_objects AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
LEFT JOIN sys.synonyms AS sn ON sn.object_id = o.object_id
WHERE o.type IN ('U', 'V', 'IF', 'TF', 'FT', 'FN', 'FS', 'SN');

-- Columns of user objects right away, of system objects on demand.
SELECT c.object_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed
FROM sys.columns AS c
JOIN sys.objects AS o ON o.object_id = c.object_id
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE o.type IN ('U', 'V', 'IF', 'TF', 'FT') AND o.is_ms_shipped = 0
ORDER BY c.object_id, c.column_id;

-- For three-part names.
SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name;

-- Only the names of the linked servers; what is in them is loaded when one is chosen.
SELECT name FROM sys.servers WHERE is_linked = 1 ORDER BY name;

-- Foreign keys with their column pairs, for JOIN suggestions.
SELECT fk.object_id, fk.name, fk.parent_object_id, fk.referenced_object_id, pc.name, rc.name
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS c ON c.constraint_object_id = fk.object_id
JOIN sys.columns AS pc ON pc.object_id = c.parent_object_id AND pc.column_id = c.parent_column_id
JOIN sys.columns AS rc ON rc.object_id = c.referenced_object_id AND rc.column_id = c.referenced_column_id
WHERE fk.is_ms_shipped = 0
ORDER BY fk.object_id, c.constraint_column_id;

-- Primary keys.
SELECT i.object_id, c.name
FROM sys.indexes AS i
JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
JOIN sys.objects AS o ON o.object_id = i.object_id
WHERE i.is_primary_key = 1 AND o.is_ms_shipped = 0
ORDER BY i.object_id, ic.key_ordinal;

-- User stored procedures and functions with their parameters, for EXEC and parameter info.
SELECT o.object_id, s.name, o.name
FROM sys.procedures AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE o.is_ms_shipped = 0
ORDER BY o.object_id;

SELECT p.object_id, p.name, t.name, p.max_length, p.precision, p.scale, p.is_output, p.has_default_value
FROM sys.parameters AS p
JOIN sys.objects AS o ON o.object_id = p.object_id
JOIN sys.types AS t ON t.user_type_id = p.user_type_id
WHERE o.is_ms_shipped = 0 AND o.type IN ('P', 'PC', 'FN', 'IF', 'TF', 'FS', 'FT') AND p.parameter_id > 0
ORDER BY p.object_id, p.parameter_id;

-- sys.parameters.has_default_value is only set for CLR procedures: the default values of T-SQL ones are in the text of the procedure,
-- and the beginning of it holds the parameters.
SELECT m.object_id, LEFT(m.definition, 4000)
FROM sys.sql_modules AS m
WHERE m.object_id IN (SELECT p.object_id FROM sys.parameters AS p JOIN sys.procedures AS o ON o.object_id = p.object_id
                      WHERE o.is_ms_shipped = 0 AND p.parameter_id > 0);

-- User triggers: DML triggers with their parent table or view, and database DDL triggers.
SELECT ps.name, t.name, ps.name, p.name
FROM sys.triggers AS t
LEFT JOIN sys.objects AS p ON p.object_id = t.parent_id AND t.parent_class = 1
LEFT JOIN sys.schemas AS ps ON ps.schema_id = p.schema_id
WHERE t.is_ms_shipped = 0
ORDER BY t.name;

-- Indexes of user tables and views (names after ALTER INDEX and DROP INDEX).
SELECT s.name, o.name, i.name, i.type_desc, i.is_unique
FROM sys.indexes AS i
JOIN sys.objects AS o ON o.object_id = i.object_id
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE i.index_id > 0 AND i.name IS NOT NULL AND o.is_ms_shipped = 0 AND o.type IN ('U', 'V')
ORDER BY i.name;

-- Named constraints of user tables (names after DROP CONSTRAINT, CHECK CONSTRAINT, NOCHECK CONSTRAINT).
SELECT s.name, p.name, o.name, o.type_desc
FROM sys.objects AS o
JOIN sys.objects AS p ON p.object_id = o.parent_object_id
JOIN sys.schemas AS s ON s.schema_id = p.schema_id
WHERE o.type IN ('PK', 'UQ', 'F', 'D', 'C') AND p.is_ms_shipped = 0 AND p.type = 'U'
ORDER BY o.name;";

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
                            reader.GetBoolean(4),
                            reader.IsDBNull(5) ? null : SqlIdentifier.SplitName(reader.GetString(5)),
                            reader.IsDBNull(6) ? 0 : reader.GetInt32(6)));
                    }

                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    var columns = await ReadColumnsAsync(reader, cancellationToken).ConfigureAwait(false);

                    var databases = new List<string>();
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        databases.Add(reader.GetString(0));
                    }

                    var linkedServers = new List<string>();
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        linkedServers.Add(reader.GetString(0));
                    }

                    var foreignKeys = new List<DbForeignKey>();
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    int currentKey = int.MinValue;
                    string keyName = null;
                    int childId = 0, referencedId = 0;
                    var keyColumns = new List<DbForeignKeyColumn>();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var keyId = reader.GetInt32(0);
                        if (keyId != currentKey)
                        {
                            if (currentKey != int.MinValue)
                            {
                                foreignKeys.Add(new DbForeignKey(keyName, childId, referencedId, keyColumns));
                            }

                            currentKey = keyId;
                            keyName = reader.GetString(1);
                            childId = reader.GetInt32(2);
                            referencedId = reader.GetInt32(3);
                            keyColumns = new List<DbForeignKeyColumn>();
                        }

                        keyColumns.Add(new DbForeignKeyColumn(reader.GetString(4), reader.GetString(5)));
                    }

                    if (currentKey != int.MinValue)
                    {
                        foreignKeys.Add(new DbForeignKey(keyName, childId, referencedId, keyColumns));
                    }

                    var primaryKeys = new Dictionary<int, List<string>>();
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var objectId = reader.GetInt32(0);
                        if (!primaryKeys.TryGetValue(objectId, out var key))
                        {
                            primaryKeys[objectId] = key = new List<string>();
                        }

                        key.Add(reader.GetString(1));
                    }

                    var (procedures, functionParameters) = await ReadProceduresAsync(reader, cancellationToken).ConfigureAwait(false);

                    var triggers = new List<DbTrigger>();
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        triggers.Add(new DbTrigger(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2) + "." + reader.GetString(3)));
                    }

                    var indexes = new List<DbIndex>();
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var typeText = (reader.GetBoolean(4) ? "unique " : string.Empty) + reader.GetString(3).Replace('_', ' ').ToLowerInvariant();
                        indexes.Add(new DbIndex(reader.GetString(0), reader.GetString(1), reader.GetString(2), typeText));
                    }

                    var constraints = new List<DbConstraint>();
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var typeText = reader.GetString(3).Replace("_CONSTRAINT", string.Empty).Replace("_", " ").ToLowerInvariant();
                        constraints.Add(new DbConstraint(reader.GetString(0), reader.GetString(1), reader.GetString(2), typeText));
                    }

                    reader.Close();
                    var version = await QueryVersionAsync(connection, cancellationToken).ConfigureAwait(false);
                    return new DatabaseSnapshot(database, defaultSchema, schemas, objects, version, columns, databases, linkedServers, foreignKeys,
                        primaryKeys.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value), procedures, functionParameters, triggers, indexes, constraints);
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
SELECT c.object_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed
FROM sys.all_columns AS c
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id IN (" + string.Join(", ", names) + @")
ORDER BY c.object_id, c.column_id;

-- For three-part names.
SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name;

-- Only the names of the linked servers; what is in them is loaded when one is chosen.
SELECT name FROM sys.servers WHERE is_linked = 1 ORDER BY name;

-- Foreign keys with their column pairs, for JOIN suggestions.
SELECT fk.object_id, fk.name, fk.parent_object_id, fk.referenced_object_id, pc.name, rc.name
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS c ON c.constraint_object_id = fk.object_id
JOIN sys.columns AS pc ON pc.object_id = c.parent_object_id AND pc.column_id = c.parent_column_id
JOIN sys.columns AS rc ON rc.object_id = c.referenced_object_id AND rc.column_id = c.referenced_column_id
WHERE fk.is_ms_shipped = 0
ORDER BY fk.object_id, c.constraint_column_id;

-- Primary keys.
SELECT i.object_id, c.name
FROM sys.indexes AS i
JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
JOIN sys.objects AS o ON o.object_id = i.object_id
WHERE i.is_primary_key = 1 AND o.is_ms_shipped = 0
ORDER BY i.object_id, ic.key_ordinal;";

                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    return await ReadColumnsAsync(reader, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Reads rows (object_id, name, type, max_length, precision, scale, is_nullable, is_identity, is_computed) ordered by object.</summary>
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
                    reader.GetBoolean(6),
                    reader.GetBoolean(7),
                    reader.GetBoolean(8),
                    string.Equals(reader.GetString(2), "timestamp", StringComparison.OrdinalIgnoreCase)));
            }

            return lists.ToDictionary(p => p.Key, p => (IReadOnlyList<DbColumn>)p.Value);
        }

        /// <summary>Reads the three result sets of the procedure part of the objects query: procedures, parameters, headers.</summary>
        private static async Task<(IReadOnlyList<DbProcedure> Procedures, IReadOnlyDictionary<int, IReadOnlyList<DbParameter>> FunctionParameters)> ReadProceduresAsync(DbDataReader reader, CancellationToken cancellationToken)
        {
            var heads = new List<(int Id, string Schema, string Name)>();
            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                heads.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            }

            var parameters = new Dictionary<int, List<(string Name, string Type, bool Output, bool HasDefault)>>();
            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt32(0);
                if (!parameters.TryGetValue(id, out var list))
                {
                    parameters[id] = list = new List<(string Name, string Type, bool Output, bool HasDefault)>();
                }

                list.Add((reader.GetString(1), FormatType(reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5)), reader.GetBoolean(6), reader.GetBoolean(7)));
            }

            var defaults = new Dictionary<int, IReadOnlyDictionary<string, string>>();
            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                defaults[reader.GetInt32(0)] = ProcedureHeader.ReadDefaults(reader.IsDBNull(1) ? null : reader.GetString(1));
            }

            var procedures = heads.Select(h =>
            {
                defaults.TryGetValue(h.Id, out var written);
                var list = parameters.TryGetValue(h.Id, out var found) ? found : new List<(string Name, string Type, bool Output, bool HasDefault)>();
                return new DbProcedure(h.Id, h.Schema, h.Name, list
                    .Select(p => new DbParameter(p.Name, p.Type, p.Output, p.HasDefault || (written != null && written.ContainsKey(p.Name)), written != null && written.TryGetValue(p.Name, out var text) ? text : null))
                    .ToList());
            }).ToList();

            var procedureIds = new HashSet<int>(heads.Select(h => h.Id));
            var functions = parameters
                .Where(p => !procedureIds.Contains(p.Key))
                .ToDictionary(
                    p => p.Key,
                    p => (IReadOnlyList<DbParameter>)p.Value.Select(x => new DbParameter(x.Name, x.Type, x.Output, x.HasDefault)).ToList());
            return (procedures, functions);
        }

        /// <summary>Seconds a call to a linked server may take before it is given up on.</summary>
        private const int LinkedServerTimeoutSeconds = 15;

        public async Task<IReadOnlyList<string>> LoadLinkedDatabasesAsync(string linkedServer, CancellationToken cancellationToken)
        {
            using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
            using (var command = connection.CreateCommand())
            {
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = "sp_catalogs";
                command.CommandTimeout = LinkedServerTimeoutSeconds;
                AddParameter(command, "@server_name", linkedServer);
                var result = new List<string>();
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        result.Add(reader.GetString(0));
                    }
                }

                return result.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public async Task<IReadOnlyList<RemoteObject>> LoadLinkedObjectsAsync(string linkedServer, string database, CancellationToken cancellationToken)
        {
            using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
            using (var command = connection.CreateCommand())
            {
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = "sp_tables_ex";
                command.CommandTimeout = LinkedServerTimeoutSeconds;
                AddParameter(command, "@table_server", linkedServer);
                AddParameter(command, "@table_catalog", database);
                var result = new List<RemoteObject>();
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    // TABLE_CAT, TABLE_SCHEM, TABLE_NAME, TABLE_TYPE, REMARKS
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (reader.IsDBNull(1) || reader.IsDBNull(2))
                        {
                            continue;
                        }

                        var type = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim().ToUpperInvariant();
                        result.Add(new RemoteObject(
                            reader.GetString(1),
                            reader.GetString(2),
                            type == "VIEW" ? DbObjectKind.View : DbObjectKind.Table,
                            type == "SYSTEM TABLE" || type == "SYSTEM VIEW"));
                    }
                }

                return result;
            }
        }

        public async Task<IReadOnlyList<DbColumn>> LoadLinkedColumnsAsync(string linkedServer, string database, string schema, string table, CancellationToken cancellationToken)
        {
            using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
            using (var command = connection.CreateCommand())
            {
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = "sp_columns_ex";
                command.CommandTimeout = LinkedServerTimeoutSeconds;
                AddParameter(command, "@table_server", linkedServer);
                AddParameter(command, "@table_name", table);
                AddParameter(command, "@table_schema", schema);
                AddParameter(command, "@table_catalog", database);
                var result = new List<DbColumn>();
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    // TABLE_CAT, TABLE_SCHEM, TABLE_NAME, COLUMN_NAME, DATA_TYPE, TYPE_NAME, COLUMN_SIZE, BUFFER_LENGTH, DECIMAL_DIGITS, NUM_PREC_RADIX, NULLABLE, …
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var type = reader.IsDBNull(5) ? string.Empty : reader.GetString(5).Replace(" identity", string.Empty);
                        var size = reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6));
                        var scale = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8));
                        var typeText = SqlMetadataLoader.FormatLinkedType(type, size, scale);
                        result.Add(new DbColumn(reader.GetString(3), typeText, !reader.IsDBNull(10) && Convert.ToInt32(reader.GetValue(10)) != 0));
                    }
                }

                return result;
            }
        }

        /// <summary>Type with its length as sp_columns_ex reports it (size in characters or digits).</summary>
        internal static string FormatLinkedType(string type, int size, int scale)
        {
            switch (type.ToLowerInvariant())
            {
                case "varchar":
                case "char":
                case "nvarchar":
                case "nchar":
                case "varbinary":
                case "binary":
                    return type + "(" + (size <= 0 || size > 8000 ? "max" : size.ToString()) + ")";
                case "decimal":
                case "numeric":
                    return type + "(" + size + "," + scale + ")";
                default:
                    return type;
            }
        }

        private static void AddParameter(DbCommand command, string name, string value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = DbType.String;
            parameter.Value = value;
            command.Parameters.Add(parameter);
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
                case "SN":
                    return DbObjectKind.Synonym;
                case "V":
                    return DbObjectKind.View;
                case "IF":
                case "TF":
                case "FT":
                    return DbObjectKind.TableFunction;
                case "FN":
                case "FS":
                    return DbObjectKind.ScalarFunction;
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
