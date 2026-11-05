using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Metadata
{
    /// <summary>Immutable metadata snapshot of one database. Adding columns creates a new snapshot.</summary>
    public sealed class DatabaseSnapshot : IDatabaseMetadata
    {
        private readonly Dictionary<string, DbObject> byName;
        private readonly HashSet<string> schemaSet;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<DbColumn>> columns;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> primaryKeys;

        public DatabaseSnapshot(
            string databaseName,
            string defaultSchema,
            IReadOnlyList<string> schemas,
            IReadOnlyList<DbObject> objects,
            string version,
            IReadOnlyDictionary<int, IReadOnlyList<DbColumn>> columns = null,
            IReadOnlyList<string> databases = null,
            IReadOnlyList<string> linkedServers = null,
            IReadOnlyList<DbForeignKey> foreignKeys = null,
            IReadOnlyDictionary<int, IReadOnlyList<string>> primaryKeys = null,
            IReadOnlyList<DbProcedure> procedures = null)
        {
            DatabaseName = databaseName ?? string.Empty;
            DefaultSchema = string.IsNullOrEmpty(defaultSchema) ? "dbo" : defaultSchema;
            Schemas = schemas ?? Array.Empty<string>();
            Objects = objects ?? Array.Empty<DbObject>();
            Databases = databases ?? Array.Empty<string>();
            LinkedServers = linkedServers ?? Array.Empty<string>();
            ForeignKeys = foreignKeys ?? Array.Empty<DbForeignKey>();
            Procedures = procedures ?? Array.Empty<DbProcedure>();
            this.primaryKeys = primaryKeys ?? new Dictionary<int, IReadOnlyList<string>>();
            Version = version;
            this.columns = columns ?? new Dictionary<int, IReadOnlyList<DbColumn>>();
            schemaSet = new HashSet<string>(Schemas, StringComparer.OrdinalIgnoreCase);
            byName = new Dictionary<string, DbObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var dbObject in Objects)
            {
                byName[Key(dbObject.Schema, dbObject.Name)] = dbObject;
            }
        }

        private DatabaseSnapshot(DatabaseSnapshot source, IReadOnlyDictionary<int, IReadOnlyList<DbColumn>> columns)
        {
            DatabaseName = source.DatabaseName;
            DefaultSchema = source.DefaultSchema;
            Schemas = source.Schemas;
            Objects = source.Objects;
            Databases = source.Databases;
            LinkedServers = source.LinkedServers;
            ForeignKeys = source.ForeignKeys;
            Procedures = source.Procedures;
            primaryKeys = source.primaryKeys;
            Version = source.Version;
            byName = source.byName;
            schemaSet = source.schemaSet;
            this.columns = columns;
        }

        public string DatabaseName { get; }

        public string DefaultSchema { get; }

        public IReadOnlyList<string> Schemas { get; }

        public IReadOnlyList<DbObject> Objects { get; }

        public IReadOnlyList<string> Databases { get; }

        public IReadOnlyList<string> LinkedServers { get; }

        public IReadOnlyList<DbForeignKey> ForeignKeys { get; }

        public IReadOnlyList<DbProcedure> Procedures { get; }

        public IReadOnlyList<string> GetPrimaryKey(DbObject dbObject) =>
            dbObject != null && primaryKeys.TryGetValue(dbObject.Id, out var key) ? key : Array.Empty<string>();

        /// <summary>Fingerprint of the objects in the database; when it changes, the snapshot is stale.</summary>
        public string Version { get; }

        public int LoadedColumnObjects => columns.Count;

        public DbObject FindObject(string schema, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(schema))
            {
                return byName.TryGetValue(Key(schema, name), out var exact) ? exact : null;
            }

            if (byName.TryGetValue(Key(DefaultSchema, name), out var inDefault))
            {
                return inDefault;
            }

            return byName.TryGetValue(Key("dbo", name), out var inDbo) ? inDbo : null;
        }

        public bool HasSchema(string schema) => schema != null && schemaSet.Contains(schema);

        public IReadOnlyList<DbColumn> GetColumns(DbObject dbObject)
        {
            return dbObject != null && columns.TryGetValue(dbObject.Id, out var list) ? list : null;
        }

        public bool HasColumns(int objectId) => columns.ContainsKey(objectId);

        /// <summary>New snapshot with the added columns. The original is unchanged.</summary>
        public DatabaseSnapshot WithColumns(IReadOnlyDictionary<int, IReadOnlyList<DbColumn>> added)
        {
            if (added == null || added.Count == 0)
            {
                return this;
            }

            var merged = columns.ToDictionary(p => p.Key, p => p.Value);
            foreach (var pair in added)
            {
                merged[pair.Key] = pair.Value;
            }

            return new DatabaseSnapshot(this, merged);
        }

        private static string Key(string schema, string name) => schema + "\u0001" + name;
    }
}
