using System.Collections.Generic;

namespace SqlPrompter.Core.Model
{
    public enum DbObjectKind
    {
        Table,
        View,
        TableFunction,
    }

    public sealed class DbObject
    {
        public DbObject(int id, string schema, string name, DbObjectKind kind, bool isSystem)
        {
            Id = id;
            Schema = schema;
            Name = name;
            Kind = kind;
            IsSystem = isSystem;
        }

        /// <summary>object_id in the database.</summary>
        public int Id { get; }

        public string Schema { get; }

        public string Name { get; }

        public DbObjectKind Kind { get; }

        /// <summary>System object (sys.*, INFORMATION_SCHEMA.*).</summary>
        public bool IsSystem { get; }

        public override string ToString() => Schema + "." + Name;
    }

    public sealed class DbColumn
    {
        public DbColumn(string name, string typeName, bool isNullable)
        {
            Name = name;
            TypeName = typeName;
            IsNullable = isNullable;
        }

        public string Name { get; }

        /// <summary>Type including length, e.g. nvarchar(50).</summary>
        public string TypeName { get; }

        public bool IsNullable { get; }
    }

    /// <summary>
    /// Immutable metadata snapshot of one database. Implementations must not wait for the database:
    /// anything not loaded yet is returned as null.
    /// </summary>
    public interface IDatabaseMetadata
    {
        string DatabaseName { get; }

        string DefaultSchema { get; }

        IReadOnlyList<string> Schemas { get; }

        IReadOnlyList<DbObject> Objects { get; }

        /// <summary>Finds an object like SQL Server does: without a schema in the default schema, then in dbo.</summary>
        DbObject FindObject(string schema, string name);

        bool HasSchema(string schema);

        /// <summary>Columns of the object, or null if they are not loaded yet.</summary>
        IReadOnlyList<DbColumn> GetColumns(DbObject dbObject);
    }
}
