using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SqlPrompter.Metadata
{
    /// <summary>An object that another object uses, or that uses it (#222).</summary>
    public sealed class DependencyItem
    {
        public DependencyItem(string database, string schema, string name, string typeCode)
        {
            Database = database;
            Schema = schema;
            Name = name;
            TypeCode = typeCode;
        }

        /// <summary>Null for an object of the same database.</summary>
        public string Database { get; }

        public string Schema { get; }

        public string Name { get; }

        /// <summary>The type in sys.objects (U, V, P, FN, IF, TF, TR, SN, ...), or empty when it is not known (another database).</summary>
        public string TypeCode { get; }

        public string FullName =>
            (string.IsNullOrEmpty(Database) ? string.Empty : Database + ".") + (string.IsNullOrEmpty(Schema) ? string.Empty : Schema + ".") + Name;

        /// <summary>The kind as a word.</summary>
        public string KindText
        {
            get
            {
                switch ((TypeCode ?? string.Empty).Trim())
                {
                    case "U": return "Table";
                    case "V": return "View";
                    case "P": return "Procedure";
                    case "FN": return "Scalar function";
                    case "IF":
                    case "TF": return "Table function";
                    case "TR": return "Trigger";
                    case "SN": return "Synonym";
                    case "SO": return "Sequence";
                    default: return string.Empty;
                }
            }
        }
    }

    public sealed class DependencyResult
    {
        public DependencyResult(IReadOnlyList<DependencyItem> uses, IReadOnlyList<DependencyItem> usedBy)
        {
            Uses = uses;
            UsedBy = usedBy;
        }

        /// <summary>What the object uses.</summary>
        public IReadOnlyList<DependencyItem> Uses { get; }

        /// <summary>What uses the object.</summary>
        public IReadOnlyList<DependencyItem> UsedBy { get; }
    }

    /// <summary>Reads <c>sys.sql_expression_dependencies</c> for one object, read-only, on a connection of its own.</summary>
    public sealed class DependencyLoader
    {
        private readonly Func<DbConnection> connectionFactory;

        public DependencyLoader(Func<DbConnection> connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<DependencyResult> LoadAsync(string database, string schema, string name, CancellationToken cancellationToken)
        {
            var prefix = string.IsNullOrEmpty(database) ? string.Empty : "[" + database.Replace("]", "]]") + "].";
            var fullName = "[" + (string.IsNullOrEmpty(database) ? string.Empty : database.Replace("]", "]]") + "].[") + (schema ?? "dbo").Replace("]", "]]") + "].[" + name.Replace("]", "]]") + "]";
            var uses = new List<DependencyItem>();
            var usedBy = new List<DependencyItem>();
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT DISTINCT d.referenced_database_name, d.referenced_schema_name, d.referenced_entity_name, RTRIM(ro.type) FROM " + prefix + "sys.sql_expression_dependencies AS d "
                        + "LEFT JOIN " + prefix + "sys.objects AS ro ON ro.object_id = d.referenced_id "
                        + "WHERE d.referencing_id = OBJECT_ID(@full) AND d.referenced_entity_name IS NOT NULL ORDER BY 2, 3";
                    Add(command, "@full", fullName);
                    await Read(command, uses, cancellationToken).ConfigureAwait(false);
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT DISTINCT NULL, s.name, o.name, RTRIM(o.type) FROM " + prefix + "sys.sql_expression_dependencies AS d "
                        + "JOIN " + prefix + "sys.objects AS o ON o.object_id = d.referencing_id "
                        + "JOIN " + prefix + "sys.schemas AS s ON s.schema_id = o.schema_id "
                        + "WHERE d.referenced_id = OBJECT_ID(@full) ORDER BY 2, 3";
                    Add(command, "@full", fullName);
                    await Read(command, usedBy, cancellationToken).ConfigureAwait(false);
                }
            }

            return new DependencyResult(uses, usedBy);
        }

        private static async Task Read(DbCommand command, List<DependencyItem> into, CancellationToken cancellationToken)
        {
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    into.Add(new DependencyItem(
                        reader.IsDBNull(0) ? null : reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
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
