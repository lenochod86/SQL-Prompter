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
                    var script = await BuildTableScriptAsync(connection, prefix, database, objectId, foundSchema, foundName, cancellationToken).ConfigureAwait(false);
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

        private static async Task<string> BuildTableScriptAsync(DbConnection connection, string prefix, string database, int objectId, string schema, string name, CancellationToken cancellationToken)
        {
            var model = new TableScriptModel { Schema = schema, Name = name };
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable, "
                    + "CASE WHEN ic.object_id IS NOT NULL THEN 'IDENTITY(' + CONVERT(varchar(40), ic.seed_value) + ',' + CONVERT(varchar(40), ic.increment_value) + ')' END, "
                    + "CASE WHEN c.collation_name COLLATE DATABASE_DEFAULT <> (SELECT d.collation_name COLLATE DATABASE_DEFAULT FROM " + prefix + "sys.databases AS d WHERE d.name = @db) THEN c.collation_name END, "
                    + "cc.definition, cc.is_persisted, dc.name, dc.definition "
                    + "FROM " + prefix + "sys.columns AS c JOIN " + prefix + "sys.types AS t ON t.user_type_id = c.user_type_id "
                    + "LEFT JOIN " + prefix + "sys.identity_columns AS ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id "
                    + "LEFT JOIN " + prefix + "sys.computed_columns AS cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id "
                    + "LEFT JOIN " + prefix + "sys.default_constraints AS dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id "
                    + "WHERE c.object_id = @id ORDER BY c.column_id";
                Add(command, "@id", objectId);
                Add(command, "@db", string.IsNullOrEmpty(database) ? connection.Database : database);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        model.Columns.Add(new ScriptTableColumn(
                            reader.GetString(0),
                            SqlMetadataLoader.FormatType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4)),
                            reader.GetBoolean(5),
                            reader.IsDBNull(6) ? null : reader.GetString(6))
                        {
                            Collation = reader.IsDBNull(7) ? null : reader.GetString(7),
                            ComputedText = reader.IsDBNull(8) ? null : reader.GetString(8),
                            IsPersisted = !reader.IsDBNull(9) && reader.GetBoolean(9),
                            DefaultName = reader.IsDBNull(10) ? null : reader.GetString(10),
                            DefaultText = reader.IsDBNull(11) ? null : reader.GetString(11),
                        });
                    }
                }
            }

            // Primary key, unique constraints and the clustered and nonclustered indexes; the key columns come in key order, the included ones after them.
            var indexes = new Dictionary<int, TableIndexScript>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT i.index_id, i.name, i.type, i.is_unique, i.is_primary_key, i.is_unique_constraint, i.has_filter, i.filter_definition, i.fill_factor, "
                    + "i.is_padded, i.ignore_dup_key, i.allow_row_locks, i.allow_page_locks, i.is_disabled, c.name, ic.is_descending_key, ic.is_included_column "
                    + "FROM " + prefix + "sys.indexes AS i "
                    + "JOIN " + prefix + "sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id "
                    + "JOIN " + prefix + "sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id "
                    + "WHERE i.object_id = @id AND i.type IN (1, 2) AND i.is_hypothetical = 0 "
                    + "ORDER BY i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id";
                Add(command, "@id", objectId);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var indexId = reader.GetInt32(0);
                        if (!indexes.TryGetValue(indexId, out var index))
                        {
                            index = new TableIndexScript
                            {
                                Name = reader.GetString(1),
                                IsClustered = reader.GetByte(2) == 1,
                                IsUnique = reader.GetBoolean(3),
                                IsPrimaryKey = reader.GetBoolean(4),
                                IsUniqueConstraint = reader.GetBoolean(5),
                                Filter = !reader.GetBoolean(6) || reader.IsDBNull(7) ? null : reader.GetString(7),
                                IsDisabled = reader.GetBoolean(13),
                            };
                            var fillFactor = reader.GetByte(8);
                            if (fillFactor > 0 && fillFactor < 100)
                            {
                                index.Options.Add("FILLFACTOR = " + fillFactor);
                            }

                            if (reader.GetBoolean(9))
                            {
                                index.Options.Add("PAD_INDEX = ON");
                            }

                            if (reader.GetBoolean(10))
                            {
                                index.Options.Add("IGNORE_DUP_KEY = ON");
                            }

                            if (!reader.GetBoolean(11))
                            {
                                index.Options.Add("ALLOW_ROW_LOCKS = OFF");
                            }

                            if (!reader.GetBoolean(12))
                            {
                                index.Options.Add("ALLOW_PAGE_LOCKS = OFF");
                            }

                            indexes[indexId] = index;
                            model.Indexes.Add(index);
                        }

                        var column = "[" + reader.GetString(14).Replace("]", "]]") + "]";
                        if (reader.GetBoolean(16))
                        {
                            index.IncludedColumns.Add(column);
                        }
                        else
                        {
                            index.KeyColumns.Add(column + (reader.GetBoolean(15) ? " DESC" : string.Empty));
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name, definition, is_disabled, is_not_trusted FROM " + prefix + "sys.check_constraints WHERE parent_object_id = @id ORDER BY name";
                Add(command, "@id", objectId);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        model.Checks.Add(new TableCheckScript { Name = reader.GetString(0), Definition = reader.GetString(1), IsDisabled = reader.GetBoolean(2), IsNotTrusted = reader.GetBoolean(3) });
                    }
                }
            }

            var keys = new Dictionary<int, TableForeignKeyScript>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT fk.object_id, fk.name, rs.name, ro.name, fk.delete_referential_action_desc, fk.update_referential_action_desc, fk.is_disabled, fk.is_not_trusted, "
                    + "fk.is_not_for_replication, pc.name, rc.name "
                    + "FROM " + prefix + "sys.foreign_keys AS fk "
                    + "JOIN " + prefix + "sys.objects AS ro ON ro.object_id = fk.referenced_object_id "
                    + "JOIN " + prefix + "sys.schemas AS rs ON rs.schema_id = ro.schema_id "
                    + "JOIN " + prefix + "sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id "
                    + "JOIN " + prefix + "sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id "
                    + "JOIN " + prefix + "sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id "
                    + "WHERE fk.parent_object_id = @id ORDER BY fk.name, fkc.constraint_column_id";
                Add(command, "@id", objectId);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var keyId = reader.GetInt32(0);
                        if (!keys.TryGetValue(keyId, out var key))
                        {
                            key = new TableForeignKeyScript
                            {
                                Name = reader.GetString(1),
                                ReferencedSchema = reader.GetString(2),
                                ReferencedTable = reader.GetString(3),
                                DeleteAction = reader.GetString(4),
                                UpdateAction = reader.GetString(5),
                                IsDisabled = reader.GetBoolean(6),
                                IsNotTrusted = reader.GetBoolean(7),
                                IsNotForReplication = reader.GetBoolean(8),
                            };
                            keys[keyId] = key;
                            model.ForeignKeys.Add(key);
                        }

                        key.Columns.Add(reader.GetString(9));
                        key.ReferencedColumns.Add(reader.GetString(10));
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT s.name, o.name, fk.name FROM " + prefix + "sys.foreign_keys AS fk "
                    + "JOIN " + prefix + "sys.objects AS o ON o.object_id = fk.parent_object_id JOIN " + prefix + "sys.schemas AS s ON s.schema_id = o.schema_id "
                    + "WHERE fk.referenced_object_id = @id AND fk.parent_object_id <> @id ORDER BY s.name, o.name, fk.name";
                Add(command, "@id", objectId);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        model.ReferencedBy.Add(new TableReferencedBy { Schema = reader.GetString(0), Table = reader.GetString(1), ForeignKey = reader.GetString(2) });
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT t.name, m.definition FROM " + prefix + "sys.triggers AS t JOIN " + prefix + "sys.sql_modules AS m ON m.object_id = t.object_id "
                    + "WHERE t.parent_id = @id AND m.definition IS NOT NULL ORDER BY t.name";
                Add(command, "@id", objectId);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        model.Triggers.Add(new TableTriggerScript { Name = reader.GetString(0), Definition = reader.GetString(1) });
                    }
                }
            }

            return DefinitionScript.ForTable(model);
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
