using System.Collections.Generic;

namespace SqlPrompter.Core.Model
{
    public enum DbObjectKind
    {
        Table,
        View,
        TableFunction,

        /// <summary>Scalar function; it is used in expressions, never as a table source.</summary>
        ScalarFunction,

        /// <summary>Synonym; <see cref="DbObject.SynonymTarget"/> says what it points to.</summary>
        Synonym,
    }

    public sealed class DbObject
    {
        public DbObject(int id, string schema, string name, DbObjectKind kind, bool isSystem, IReadOnlyList<string> synonymTarget = null, int parameterCount = 0)
        {
            ParameterCount = parameterCount;
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

        /// <summary>Number of parameters of a function (the return value is not counted); 0 for other objects.</summary>
        public int ParameterCount { get; }

        public override string ToString() => Schema + "." + Name;
    }

    public sealed class DbColumn
    {
        public DbColumn(string name, string typeName, bool isNullable, bool isIdentity = false, bool isComputed = false, bool isRowVersion = false)
        {
            Name = name;
            TypeName = typeName;
            IsNullable = isNullable;
            IsIdentity = isIdentity;
            IsComputed = isComputed;
            IsRowVersion = isRowVersion;
        }

        public string Name { get; }

        /// <summary>Type including length, e.g. nvarchar(50).</summary>
        public string TypeName { get; }

        public bool IsNullable { get; }

        public bool IsIdentity { get; }

        public bool IsComputed { get; }

        /// <summary>A timestamp / rowversion column.</summary>
        public bool IsRowVersion { get; }

        /// <summary>Whether an INSERT can supply a value: not an identity, computed or rowversion column.</summary>
        public bool IsInsertable => !IsIdentity && !IsComputed && !IsRowVersion;
    }

    /// <summary>A parameter of a stored procedure.</summary>
    public sealed class DbParameter
    {
        public DbParameter(string name, string typeName, bool isOutput, bool hasDefault, string defaultText = null)
        {
            Name = name;
            TypeName = typeName;
            IsOutput = isOutput;
            HasDefault = hasDefault;
            DefaultText = defaultText;
        }

        /// <summary>With the at sign, e.g. @Id.</summary>
        public string Name { get; }

        /// <summary>Type including length, e.g. nvarchar(50).</summary>
        public string TypeName { get; }

        public bool IsOutput { get; }

        /// <summary>The parameter has a default value, so it is optional.</summary>
        public bool HasDefault { get; }

        /// <summary>The default value as written in the procedure, or null when it has none or could not be read.</summary>
        public string DefaultText { get; }
    }

    /// <summary>A stored procedure with its parameters in the order of the declaration.</summary>
    public sealed class DbProcedure
    {
        public DbProcedure(int id, string schema, string name, IReadOnlyList<DbParameter> parameters)
        {
            Id = id;
            Schema = schema;
            Name = name;
            Parameters = parameters ?? System.Array.Empty<DbParameter>();
        }

        public int Id { get; }

        public string Schema { get; }

        public string Name { get; }

        public IReadOnlyList<DbParameter> Parameters { get; }

        public override string ToString() => Schema + "." + Name;
    }

    /// <summary>A named constraint of a user table (#213), for the names after DROP CONSTRAINT, CHECK CONSTRAINT and NOCHECK CONSTRAINT.</summary>
    public sealed class DbConstraint
    {
        public DbConstraint(string schema, string table, string name, string typeText)
        {
            Schema = schema;
            Table = table;
            Name = name;
            TypeText = typeText ?? string.Empty;
        }

        public string Schema { get; }

        public string Table { get; }

        public string Name { get; }

        /// <summary>For example primary key, foreign key, unique, check, default.</summary>
        public string TypeText { get; }
    }

    /// <summary>An index of a user table or view (#200), for the names after ALTER INDEX and DROP INDEX.</summary>
    public sealed class DbIndex
    {
        public DbIndex(string schema, string table, string name, string typeText)
        {
            Schema = schema;
            Table = table;
            Name = name;
            TypeText = typeText ?? string.Empty;
        }

        /// <summary>The schema of the table or view the index is on.</summary>
        public string Schema { get; }

        public string Table { get; }

        public string Name { get; }

        /// <summary>For example clustered, nonclustered unique.</summary>
        public string TypeText { get; }

        public override string ToString() => Schema + "." + Table + "." + Name;
    }

    /// <summary>A trigger: a DML trigger on a table or view (<see cref="ParentName"/>), or a database DDL trigger (no parent).</summary>
    public sealed class DbTrigger
    {
        public DbTrigger(string schema, string name, string parentName)
        {
            Schema = schema;
            Name = name;
            ParentName = parentName;
        }

        /// <summary>The schema of the trigger (of its parent for a DML trigger); null for a database DDL trigger.</summary>
        public string Schema { get; }

        public string Name { get; }

        /// <summary>The table or view the trigger is on, as schema.name; null for a database DDL trigger.</summary>
        public string ParentName { get; }

        public override string ToString() => Schema == null ? Name : Schema + "." + Name;
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

        /// <summary>User stored procedures with their parameters; empty if unknown.</summary>
        IReadOnlyList<DbProcedure> Procedures { get; }

        /// <summary>User triggers (DML and database DDL); empty if unknown.</summary>
        IReadOnlyList<DbTrigger> Triggers { get; }

        /// <summary>Indexes of user tables and views; empty if unknown.</summary>
        IReadOnlyList<DbIndex> Indexes { get; }

        /// <summary>Named constraints of user tables (primary key, unique, foreign key, check, default); empty if unknown.</summary>
        IReadOnlyList<DbConstraint> Constraints { get; }

        /// <summary>The parameters of a user function in the order of the declaration; empty if it has none or they are not known.</summary>
        IReadOnlyList<DbParameter> GetFunctionParameters(DbObject function);

        /// <summary>The columns of the primary key of the object in key order, or an empty list.</summary>
        IReadOnlyList<string> GetPrimaryKey(DbObject dbObject);

        /// <summary>Finds an object like SQL Server does: without a schema in the default schema, then in dbo.</summary>
        DbObject FindObject(string schema, string name);

        bool HasSchema(string schema);

        /// <summary>Columns of the object, or null if they are not loaded yet.</summary>
        IReadOnlyList<DbColumn> GetColumns(DbObject dbObject);
    }
}
