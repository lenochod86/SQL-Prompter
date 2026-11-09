using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Navigation;

namespace SqlPrompter.Metadata
{
    /// <summary>Finds one object in the catalog of a database and the name of the server, for showing it in Object Explorer (#222). Read-only, a new connection per call.</summary>
    public sealed class ObjectLocator
    {
        private readonly Func<DbConnection> connectionFactory;

        public ObjectLocator(Func<DbConnection> connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>The object, or null when it does not exist. A name without a schema is looked up in the default schema of the user, then in dbo.</summary>
        public async Task<LocatedObject> LocateAsync(string database, string schema, string name, CancellationToken cancellationToken)
        {
            var prefix = string.IsNullOrEmpty(database) ? string.Empty : "[" + database.Replace("]", "]]") + "].";
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT TOP (1) CONVERT(nvarchar(256), SERVERPROPERTY('ServerName')), DB_NAME(), s.name, o.name, RTRIM(o.type), ps.name, po.name, RTRIM(po.type) "
                        + "FROM " + prefix + "sys.objects AS o "
                        + "JOIN " + prefix + "sys.schemas AS s ON s.schema_id = o.schema_id "
                        + "LEFT JOIN " + prefix + "sys.objects AS po ON po.object_id = o.parent_object_id "
                        + "LEFT JOIN " + prefix + "sys.schemas AS ps ON ps.schema_id = po.schema_id "
                        + "WHERE o.name = @name AND (@schema IS NULL OR s.name = @schema) AND o.type NOT IN ('S', 'IT', 'SQ') "
                        + "ORDER BY CASE WHEN s.name = SCHEMA_NAME() THEN 0 WHEN s.name = 'dbo' THEN 1 ELSE 2 END";
                    Add(command, "@name", name);
                    Add(command, "@schema", schema);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            return null;
                        }

                        return new LocatedObject
                        {
                            Server = reader.GetString(0),
                            Database = string.IsNullOrEmpty(database) ? reader.GetString(1) : database,
                            Schema = reader.GetString(2),
                            Name = reader.GetString(3),
                            TypeCode = reader.GetString(4),
                            ParentSchema = reader.IsDBNull(5) ? null : reader.GetString(5),
                            ParentName = reader.IsDBNull(6) ? null : reader.GetString(6),
                            ParentTypeCode = reader.IsDBNull(7) ? null : reader.GetString(7),
                        };
                    }
                }
            }
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
