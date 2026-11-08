using System.Collections.Generic;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>A primary key, a unique constraint or an index of a table (#351); the columns are already written as in the script (<c>[Name] DESC</c>).</summary>
    public sealed class TableIndexScript
    {
        public string Name { get; set; }

        public bool IsPrimaryKey { get; set; }

        /// <summary>A unique constraint (as opposed to a unique index).</summary>
        public bool IsUniqueConstraint { get; set; }

        public bool IsUnique { get; set; }

        public bool IsClustered { get; set; }

        public List<string> KeyColumns { get; } = new List<string>();

        public List<string> IncludedColumns { get; } = new List<string>();

        /// <summary>The filter of a filtered index, as stored (with its parentheses); null for none.</summary>
        public string Filter { get; set; }

        /// <summary>The <c>WITH (...)</c> options that differ from the defaults, e.g. <c>FILLFACTOR = 80</c>.</summary>
        public List<string> Options { get; } = new List<string>();

        public bool IsDisabled { get; set; }
    }

    public sealed class TableCheckScript
    {
        public string Name { get; set; }

        /// <summary>The expression as stored, with its parentheses.</summary>
        public string Definition { get; set; }

        public bool IsDisabled { get; set; }

        public bool IsNotTrusted { get; set; }
    }

    public sealed class TableForeignKeyScript
    {
        public string Name { get; set; }

        public string ReferencedSchema { get; set; }

        public string ReferencedTable { get; set; }

        public List<string> Columns { get; } = new List<string>();

        public List<string> ReferencedColumns { get; } = new List<string>();

        /// <summary>NO ACTION, CASCADE, SET_NULL or SET_DEFAULT as <c>sys.foreign_keys</c> names them.</summary>
        public string DeleteAction { get; set; }

        public string UpdateAction { get; set; }

        public bool IsDisabled { get; set; }

        public bool IsNotTrusted { get; set; }

        public bool IsNotForReplication { get; set; }
    }

    /// <summary>A foreign key of another table that points to this one (shown as a comment).</summary>
    public sealed class TableReferencedBy
    {
        public string Schema { get; set; }

        public string Table { get; set; }

        public string ForeignKey { get; set; }
    }

    public sealed class TableTriggerScript
    {
        public string Name { get; set; }

        public string Definition { get; set; }
    }

    /// <summary>Everything the script of a table is made of; the loader fills it from the system views.</summary>
    public sealed class TableScriptModel
    {
        public string Schema { get; set; }

        public string Name { get; set; }

        public List<ScriptTableColumn> Columns { get; } = new List<ScriptTableColumn>();

        /// <summary>The primary key, unique constraints and indexes (the clustered one included).</summary>
        public List<TableIndexScript> Indexes { get; } = new List<TableIndexScript>();

        public List<TableCheckScript> Checks { get; } = new List<TableCheckScript>();

        public List<TableForeignKeyScript> ForeignKeys { get; } = new List<TableForeignKeyScript>();

        public List<TableReferencedBy> ReferencedBy { get; } = new List<TableReferencedBy>();

        public List<TableTriggerScript> Triggers { get; } = new List<TableTriggerScript>();
    }
}
