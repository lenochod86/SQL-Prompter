using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Navigation;

namespace SqlPrompter.Metadata
{
    /// <summary>The script of one object, as F12 opens it.</summary>
    public sealed class ObjectDefinition
    {
        public ObjectDefinition(string database, string schema, string name, string typeCode, string script)
        {
            Database = database;
            Schema = schema;
            Name = name;
            TypeCode = typeCode;
            Script = script;
        }

        public string Database { get; }

        public string Schema { get; }

        public string Name { get; }

        /// <summary>sys.objects type: U table, V view, P procedure, FN/IF/TF function, TR trigger and so on.</summary>
        public string TypeCode { get; }

        /// <summary>CREATE OR ALTER for programmable objects, CREATE TABLE for tables.</summary>
        public string Script { get; }
    }

    /// <summary>Reads the definition of one object, read-only. A new connection is opened for every call.</summary>
    public sealed class DefinitionLoader
    {
        private readonly Func<DbConnection> connectionFactory;

        /// <param name="connectionFactory">Returns a new, unopened connection; the database is chosen with a three-part name.</param>
        public DefinitionLoader(Func<DbConnection> connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>The definition, or null when the object does not exist or has no script (a synonym, a system object without text).</summary>
        /// <param name="database">The database to look in; null means the one of the connection.</param>
        /// <param name="schema">The schema; null looks in the default schema of the user and then in dbo.</param>
        public async Task<ObjectDefinition> LoadAsync(string database, string schema, string name, CancellationToken cancellationToken)
        {
            var prefix = string.IsNullOrEmpty(database) ? string.Empty : "[" + database.Replace("]", "]]") + "].";
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                bool found;
                int objectId;
                string foundSchema;
                string foundName;
                string type;
                string text;
                using (var command = connection.CreateCommand())
                {
                    // The default schema of the caller comes first, then dbo, as SQL Server resolves a one-part name.
                    command.CommandText = "SELECT TOP (1) o.object_id, s.name, o.name, RTRIM(o.type), m.definition FROM " + prefix + "sys.objects AS o "
                        + "JOIN " + prefix + "sys.schemas AS s ON s.schema_id = o.schema_id "
                        + "LEFT JOIN " + prefix + "sys.sql_modules AS m ON m.object_id = o.object_id "
                        + "WHERE o.name = @name AND o.parent_object_id = 0 AND (@schema IS NULL OR s.name = @schema) AND o.type NOT IN ('S', 'IT', 'SQ') "
                        + "ORDER BY CASE WHEN s.name = SCHEMA_NAME() THEN 0 WHEN s.name = 'dbo' THEN 1 ELSE 2 END";
                    Add(command, "@name", name);
                    Add(command, "@schema", schema);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            found = false;
                            objectId = 0;
                            foundSchema = null;
                            foundName = null;
                            type = null;
                            text = null;
                        }
                        else
                        {
                            found = true;
                            objectId = reader.GetInt32(0);
                            foundSchema = reader.GetString(1);
                            foundName = reader.GetString(2);
                            type = reader.GetString(3);
                            text = reader.IsDBNull(4) ? null : reader.GetString(4);
                        }
                    }
                }

                if (!found)
                {
                    return await LoadTriggerAsync(connection, prefix, database, schema, name, cancellationToken).ConfigureAwait(false);
                }

                if (type == "U")
                {
                    var script = await BuildTableScriptAsync(connection, prefix, objectId, foundSchema, foundName, cancellationToken).ConfigureAwait(false);
                    return new ObjectDefinition(database, foundSchema, foundName, type, script);
                }

                return string.IsNullOrEmpty(text)
                    ? null
                    : new ObjectDefinition(database, foundSchema, foundName, type, DefinitionScript.ToCreateOrAlter(text));
            }
        }

        /// <summary>DML triggers have a parent object and are therefore not found by the first query.</summary>
        private static async Task<ObjectDefinition> LoadTriggerAsync(DbConnection connection, string prefix, string database, string schema, string name, CancellationToken cancellationToken)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT TOP (1) s.name, o.name, m.definition FROM " + prefix + "sys.objects AS o "
                    + "JOIN " + prefix + "sys.schemas AS s ON s.schema_id = o.schema_id "
                    + "JOIN " + prefix + "sys.sql_modules AS m ON m.object_id = o.object_id "
                    + "WHERE o.name = @name AND o.type = 'TR' AND (@schema IS NULL OR s.name = @schema)";
                Add(command, "@name", name);
                Add(command, "@schema", schema);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(2))
                    {
                        return null;
                    }

                    return new ObjectDefinition(database, reader.GetString(0), reader.GetString(1), "TR", DefinitionScript.ToCreateOrAlter(reader.GetString(2)));
                }
            }
        }

        private static async Task<string> BuildTableScriptAsync(DbConnection connection, string prefix, int objectId, string schema, string name, CancellationToken cancellationToken)
        {
            var columns = new List<ScriptTableColumn>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable, "
                    + "CASE WHEN ic.object_id IS NOT NULL THEN 'IDENTITY(' + CONVERT(varchar(40), ic.seed_value) + ',' + CONVERT(varchar(40), ic.increment_value) + ')' END "
                    + "FROM " + prefix + "sys.columns AS c JOIN " + prefix + "sys.types AS t ON t.user_type_id = c.user_type_id "
                    + "LEFT JOIN " + prefix + "sys.identity_columns AS ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id "
                    + "WHERE c.object_id = @id ORDER BY c.column_id";
                Add(command, "@id", objectId);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        columns.Add(new ScriptTableColumn(
                            reader.GetString(0),
                            SqlMetadataLoader.FormatType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4)),
                            reader.GetBoolean(5),
                            reader.IsDBNull(6) ? null : reader.GetString(6)));
                    }
                }
            }

            string keyName = null;
            var keyColumns = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT i.name, c.name FROM " + prefix + "sys.indexes AS i "
                    + "JOIN " + prefix + "sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id "
                    + "JOIN " + prefix + "sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id "
                    + "WHERE i.object_id = @id AND i.is_primary_key = 1 AND ic.is_included_column = 0 ORDER BY ic.key_ordinal";
                Add(command, "@id", objectId);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        keyName = reader.GetString(0);
                        keyColumns.Add(reader.GetString(1));
                    }
                }
            }

            return DefinitionScript.ForTable(schema, name, columns, keyName, keyColumns);
        }

        private static void Add(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
