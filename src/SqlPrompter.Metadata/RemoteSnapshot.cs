using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Metadata
{
    /// <summary>A table or view of a database on a linked server.</summary>
    public sealed class RemoteObject
    {
        public RemoteObject(string schema, string name, DbObjectKind kind, bool isSystem)
        {
            Schema = schema;
            Name = name;
            Kind = kind;
            IsSystem = isSystem;
        }

        public string Schema { get; }

        public string Name { get; }

        public DbObjectKind Kind { get; }

        public bool IsSystem { get; }
    }

    /// <summary>
    /// What is known about a linked server or one of its databases: names only, no columns.
    /// Either the databases of the server (<see cref="Databases"/>) or the objects of one database.
    /// </summary>
    public sealed class RemoteSnapshot : IDatabaseMetadata
    {
        private readonly HashSet<string> schemaSet;

        private RemoteSnapshot(string databaseName, IReadOnlyList<string> databases, IReadOnlyList<RemoteObject> objects)
        {
            DatabaseName = databaseName ?? string.Empty;
            Databases = databases ?? Array.Empty<string>();
            var id = 0;
            Objects = (objects ?? Array.Empty<RemoteObject>())
                .Select(o => new DbObject(--id, o.Schema, o.Name, o.Kind, o.IsSystem))
                .ToList();
            Schemas = Objects.Select(o => o.Schema).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            schemaSet = new HashSet<string>(Schemas, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The databases of a linked server.</summary>
        public static RemoteSnapshot OfServer(IReadOnlyList<string> databases) => new RemoteSnapshot(null, databases, null);

        /// <summary>The objects of one database of a linked server.</summary>
        public static RemoteSnapshot OfDatabase(string database, IReadOnlyList<RemoteObject> objects) => new RemoteSnapshot(database, null, objects);

        public string DatabaseName { get; }

        public string DefaultSchema => "dbo";

        public IReadOnlyList<string> Schemas { get; }

        public IReadOnlyList<DbObject> Objects { get; }

        public IReadOnlyList<string> Databases { get; }

        public IReadOnlyList<string> LinkedServers => Array.Empty<string>();

        public IReadOnlyList<DbForeignKey> ForeignKeys => Array.Empty<DbForeignKey>();

        public IReadOnlyList<DbProcedure> Procedures => Array.Empty<DbProcedure>();

        public IReadOnlyList<DbTrigger> Triggers => Array.Empty<DbTrigger>();

        public IReadOnlyList<DbIndex> Indexes => Array.Empty<DbIndex>();

        public IReadOnlyList<DbParameter> GetFunctionParameters(DbObject function) => Array.Empty<DbParameter>();

        public IReadOnlyList<string> GetPrimaryKey(DbObject dbObject) => Array.Empty<string>();

        public DbObject FindObject(string schema, string name) =>
            Objects.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                        (string.IsNullOrEmpty(schema) || string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase)));

        public bool HasSchema(string schema) => schema != null && schemaSet.Contains(schema);

        /// <summary>Columns of a linked server's tables are not loaded.</summary>
        public IReadOnlyList<DbColumn> GetColumns(DbObject dbObject) => null;
    }
}
