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

        /// <summary>object_id v databázi.</summary>
        public int Id { get; }

        public string Schema { get; }

        public string Name { get; }

        public DbObjectKind Kind { get; }

        /// <summary>Systémový objekt (sys.*, INFORMATION_SCHEMA.*).</summary>
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

        /// <summary>Typ včetně délky, např. nvarchar(50).</summary>
        public string TypeName { get; }

        public bool IsNullable { get; }
    }

    /// <summary>
    /// Neměnný snímek metadat jedné databáze. Implementace nesmí čekat na databázi:
    /// co není načtené, vrací jako null.
    /// </summary>
    public interface IDatabaseMetadata
    {
        string DatabaseName { get; }

        string DefaultSchema { get; }

        IReadOnlyList<string> Schemas { get; }

        IReadOnlyList<DbObject> Objects { get; }

        /// <summary>Najde objekt jako SQL Server: bez schématu ve výchozím schématu a pak v dbo.</summary>
        DbObject FindObject(string schema, string name);

        bool HasSchema(string schema);

        /// <summary>Sloupce objektu, nebo null, pokud ještě nejsou načtené.</summary>
        IReadOnlyList<DbColumn> GetColumns(DbObject dbObject);
    }
}
