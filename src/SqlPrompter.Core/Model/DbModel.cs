using System.Collections.Generic;

namespace SqlPrompter.Core.Model
{
    public enum DbObjectKind
    {
        Table,
        View,
        TableFunction,

        /// <summary>Synonym; <see cref="DbObject.SynonymTarget"/> says what it points to.</summary>
        Synonym,
    }

    public sealed class DbObject
    {
        public DbObject(int id, string schema, string name, DbObjectKind kind, bool isSystem, IReadOnlyList<string> synonymTarget = null)
        {
            Id = id;
            Schema = schema;
            Name = name;
            Kind = kind;
            IsSystem = isSystem;
            SynonymTarget = synonymTarget;
        }

        /// <summary>object_id in the database.</summary>
        public int Id { get; }

        public string Schema { get; }

        public string Name { get; }

        public DbObjectKind Kind { get; }

        /// <summary>System object (sys.*, INFORMATION_SCHEMA.*).</summary>
        public bool IsSystem { get; }

        /// <summary>For a synonym, the name parts of the base object (e.g. [dbo, Users]); otherwise null.</summary>
        public IReadOnlyList<string> SynonymTarget { get; }

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

    /// <summary>One column pair of a foreign key: the column of the referencing (child) table and the column it points to.</summary>
    public sealed class DbForeignKeyColumn
    {
        public DbForeignKeyColumn(string column, string referencedColumn)
        {
            Column = column;
            ReferencedColumn = referencedColumn;
        }

        public string Column { get; }

        public string ReferencedColumn { get; }
    }

    /// <summary>A foreign key, possibly over several columns, in the order of its columns.</summary>
    public sealed class DbForeignKey
    {
        public DbForeignKey(string name, int childObjectId, int referencedObjectId, IReadOnlyList<DbForeignKeyColumn> columns)
        {
            Name = name;
            ChildObjectId = childObjectId;
            ReferencedObjectId = referencedObjectId;
            Columns = columns;
        }

        public string Name { get; }

        /// <summary>The table that has the foreign key.</summary>
        public int ChildObjectId { get; }

        /// <summary>The table the key points to.</summary>
        public int ReferencedObjectId { get; }

        public IReadOnlyList<DbForeignKeyColumn> Columns { get; }
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

        /// <summary>Names of the databases on the server that the login can access; empty if unknown.</summary>
        IReadOnlyList<string> Databases { get; }

        /// <summary>Names of the linked servers (sys.servers); empty if unknown. Their contents are loaded lazily and elsewhere.</summary>
        IReadOnlyList<string> LinkedServers { get; }

        /// <summary>Foreign keys between the tables of the database; empty if unknown.</summary>
        IReadOnlyList<DbForeignKey> ForeignKeys { get; }

        /// <summary>The columns of the primary key of the object in key order, or an empty list.</summary>
        IReadOnlyList<string> GetPrimaryKey(DbObject dbObject);

        /// <summary>Finds an object like SQL Server does: without a schema in the default schema, then in dbo.</summary>
        DbObject FindObject(string schema, string name);

        bool HasSchema(string schema);

        /// <summary>Columns of the object, or null if they are not loaded yet.</summary>
        IReadOnlyList<DbColumn> GetColumns(DbObject dbObject);
    }
}
