using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SqlPrompter.Core.Snippets
{
    /// <summary>
    /// The snippets that ship with SQL Prompter (#227), written by us. Each has a stable id derived from its prefix, so that an update can tell
    /// which ones are installed already. Bodies use fields (<c>$name$</c>) where a value is needed and <c>$CURSOR$</c> where typing goes on.
    /// </summary>
    public static class BuiltInSnippets
    {
        private static readonly Lazy<IReadOnlyList<Snippet>> Cache = new Lazy<IReadOnlyList<Snippet>>(Build);

        /// <summary>The built-in set, in the order of the prefixes.</summary>
        public static IReadOnlyList<Snippet> All => Cache.Value;

        /// <summary>The built-in snippet with this id, or null.</summary>
        public static Snippet Find(string id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>The id of the built-in snippet with this prefix; the same on every machine.</summary>
        public static string IdOf(string prefix)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("sqlprompter.builtin." + prefix));
                return new Guid(hash).ToString();
            }
        }

        private static Snippet Make(string prefix, string description, string body, params string[] fields)
        {
            var snippet = Snippet.Create(prefix, description, body.Replace("\r\n", "\n"));
            snippet.Id = IdOf(prefix);
            foreach (var field in fields)
            {
                var split = field.IndexOf('=');
                snippet.Placeholders.Add(new SnippetPlaceholder { Name = field.Substring(0, split), DefaultValue = field.Substring(split + 1) });
            }

            return snippet;
        }

        private static IReadOnlyList<Snippet> Build()
        {
            var list = new List<Snippet>
            {
                // Queries.
                Make("top100", "Look at the first 100 rows of a table", "SELECT TOP (100) *\nFROM $table$$CURSOR$", "table=dbo.Table"),
                Make("sel", "Select from a table, around the selected text", "SELECT $SELECTIONSTART$$SELECTEDTEXT$$SELECTIONEND$\nFROM $table$", "table=dbo.Table"),
                Make("cnt", "Count the rows of a table", "SELECT COUNT(*)\nFROM $table$$CURSOR$", "table=dbo.Table"),
                Make("dup", "Find duplicate values of a column", "SELECT $column$, COUNT(*) AS Occurrences\nFROM $table$\nGROUP BY $column$\nHAVING COUNT(*) > 1\nORDER BY Occurrences DESC", "column=Column", "table=dbo.Table"),
                Make("exists", "WHERE EXISTS with a correlated subquery", "WHERE EXISTS (SELECT 1\n              FROM $table$ AS $alias$\n              WHERE $alias$.$column$ = $outer$.$outercolumn$)", "table=dbo.Table", "alias=x", "column=Id", "outer=t", "outercolumn=Id"),
                Make("notexists", "WHERE NOT EXISTS with a correlated subquery", "WHERE NOT EXISTS (SELECT 1\n                  FROM $table$ AS $alias$\n                  WHERE $alias$.$column$ = $outer$.$outercolumn$)", "table=dbo.Table", "alias=x", "column=Id", "outer=t", "outercolumn=Id"),
                Make("cte", "Common table expression", "WITH $name$ AS\n(\n    SELECT $columns$\n    FROM $table$\n)\nSELECT *\nFROM $name$$CURSOR$", "name=cte", "columns=*", "table=dbo.Table"),
                Make("rn", "ROW_NUMBER() over a partition", "ROW_NUMBER() OVER (PARTITION BY $partition$ ORDER BY $order$) AS RowNumber", "partition=Column", "order=Column"),
                Make("pivot", "PIVOT a measure over fixed values", "SELECT $key$, $values$\nFROM (SELECT $key$, $pivotcolumn$, $measure$ FROM $table$) AS src\nPIVOT (SUM($measure$) FOR $pivotcolumn$ IN ($values$)) AS pvt", "key=KeyColumn", "pivotcolumn=PivotColumn", "measure=Amount", "table=dbo.Table", "values=[A], [B]"),

                // Data changes.
                Make("upd", "UPDATE with a WHERE clause", "UPDATE $table$\nSET $column$ = $value$\nWHERE $condition$", "table=dbo.Table", "column=Column", "value=NULL", "condition=Id = 0"),
                Make("del", "DELETE with a WHERE clause", "DELETE FROM $table$\nWHERE $condition$", "table=dbo.Table", "condition=Id = 0"),
                Make("ins", "INSERT with a column list and VALUES", "INSERT INTO $table$ ($columns$)\nVALUES ($values$)", "table=dbo.Table", "columns=Column1, Column2", "values=NULL, NULL"),
                Make("merge", "MERGE a source into a target", "MERGE $target$ AS t\nUSING $source$ AS s\n    ON t.$key$ = s.$key$\nWHEN MATCHED THEN\n    UPDATE SET t.$column$ = s.$column$\nWHEN NOT MATCHED BY TARGET THEN\n    INSERT ($key$, $column$) VALUES (s.$key$, s.$column$);", "target=dbo.Target", "source=dbo.Source", "key=Id", "column=Name"),

                // Structure.
                Make("ct", "CREATE TABLE with a key and audit columns", "CREATE TABLE $table$\n(\n    Id int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_$name$ PRIMARY KEY,\n    Name nvarchar(100) NOT NULL,\n    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_$name$_CreatedAt DEFAULT (SYSUTCDATETIME()),\n    CreatedBy sysname NOT NULL CONSTRAINT DF_$name$_CreatedBy DEFAULT (SUSER_SNAME()),\n    ModifiedAt datetime2(0) NULL,\n    ModifiedBy sysname NULL\n);", "table=dbo.Table", "name=Table"),
                Make("cp", "CREATE PROCEDURE with NOCOUNT and TRY/CATCH", "CREATE OR ALTER PROCEDURE $name$\n    @Parameter int = NULL\nAS\nBEGIN\n    SET NOCOUNT ON;\n\n    BEGIN TRY\n        $CURSOR$\n    END TRY\n    BEGIN CATCH\n        THROW;\n    END CATCH\nEND", "name=dbo.ProcedureName"),
                Make("cf", "CREATE inline table-valued FUNCTION", "CREATE OR ALTER FUNCTION $name$\n(\n    @Parameter int\n)\nRETURNS TABLE\nAS\nRETURN\n(\n    SELECT $CURSOR$1 AS Value\n);", "name=dbo.FunctionName"),
                Make("cv", "CREATE VIEW", "CREATE OR ALTER VIEW $name$\nAS\nSELECT $columns$\nFROM $table$;", "name=dbo.ViewName", "columns=*", "table=dbo.Table"),
                Make("idx", "CREATE INDEX with included columns", "CREATE NONCLUSTERED INDEX IX_$name$_$column$\n    ON $table$ ($column$)\n    INCLUDE ($included$);", "name=Table", "column=Column", "table=dbo.Table", "included=Column2"),

                // Control flow.
                Make("trycatch", "TRY/CATCH that rethrows", "BEGIN TRY\n    $CURSOR$\nEND TRY\nBEGIN CATCH\n    THROW;\nEND CATCH"),
                Make("tran", "Transaction that is rolled back on error", "SET XACT_ABORT ON;\nBEGIN TRY\n    BEGIN TRANSACTION;\n\n    $CURSOR$\n\n    COMMIT TRANSACTION;\nEND TRY\nBEGIN CATCH\n    IF @@TRANCOUNT > 0\n        ROLLBACK TRANSACTION;\n    THROW;\nEND CATCH"),
                Make("cursor", "Forward-only cursor loop", "DECLARE @$variable$ int;\n\nDECLARE cur CURSOR LOCAL FAST_FORWARD FOR\n    SELECT $column$ FROM $table$;\n\nOPEN cur;\nFETCH NEXT FROM cur INTO @$variable$;\nWHILE @@FETCH_STATUS = 0\nBEGIN\n    $CURSOR$\n    FETCH NEXT FROM cur INTO @$variable$;\nEND\n\nCLOSE cur;\nDEALLOCATE cur;", "variable=Id", "column=Id", "table=dbo.Table"),
                Make("while", "WHILE loop with a counter", "DECLARE @i int = 1;\n\nWHILE @i <= $count$\nBEGIN\n    $CURSOR$\n    SET @i += 1;\nEND", "count=10"),
                Make("ifexists", "IF the object exists", "IF OBJECT_ID(N'$object$', N'$type$') IS NOT NULL\nBEGIN\n    $CURSOR$\nEND", "object=dbo.Table", "type=U"),
                Make("dropif", "DROP TABLE IF EXISTS", "DROP TABLE IF EXISTS $table$;", "table=dbo.Table"),
                Make("tmp", "Temporary table that is dropped at the end", "DROP TABLE IF EXISTS #$name$;\n\nCREATE TABLE #$name$\n(\n    Id int NOT NULL,\n    Value nvarchar(100) NULL\n);\n\n$CURSOR$\n\nDROP TABLE IF EXISTS #$name$;", "name=Temp"),

                // Information.
                Make("cols", "Columns of a table", "SELECT c.column_id, c.name, TYPE_NAME(c.user_type_id) AS type, c.max_length, c.is_nullable, c.is_identity\nFROM sys.columns AS c\nWHERE c.object_id = OBJECT_ID(N'$table$')\nORDER BY c.column_id;", "table=dbo.Table"),
                Make("find", "Find tables that have a column with this name", "SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName\nFROM sys.columns AS c\nJOIN sys.tables AS t ON t.object_id = c.object_id\nJOIN sys.schemas AS s ON s.schema_id = t.schema_id\nWHERE c.name LIKE N'%$column$%'\nORDER BY s.name, t.name;", "column=Name"),
                Make("sizes", "Size of the tables of the database", "SELECT s.name AS SchemaName, t.name AS TableName, SUM(ps.row_count) AS Rows, SUM(ps.reserved_page_count) * 8 / 1024 AS ReservedMB\nFROM sys.dm_db_partition_stats AS ps\nJOIN sys.tables AS t ON t.object_id = ps.object_id\nJOIN sys.schemas AS s ON s.schema_id = t.schema_id\nWHERE ps.index_id IN (0, 1)\nGROUP BY s.name, t.name\nORDER BY ReservedMB DESC;"),
                Make("locks", "Locks that are held now", "SELECT request_session_id, resource_type, resource_database_id, resource_description, request_mode, request_status\nFROM sys.dm_tran_locks\nORDER BY request_session_id;"),
                Make("blocking", "Sessions that are blocked and who blocks them", "SELECT r.session_id, r.blocking_session_id, r.wait_type, r.wait_time, DB_NAME(r.database_id) AS DatabaseName, t.text AS SqlText\nFROM sys.dm_exec_requests AS r\nCROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t\nWHERE r.blocking_session_id <> 0;"),
            };
            list.Sort((a, b) => string.Compare(a.Prefix, b.Prefix, StringComparison.OrdinalIgnoreCase));
            return list;
        }
    }

    /// <summary>Puts the built-in snippets into the snippet folder, once each, and restores one to its original text.</summary>
    public sealed class BuiltInInstaller
    {
        /// <summary>Not a .json file, so that the snippet loader does not read it.</summary>
        public const string MarkerFile = ".builtin-snippets";

        private readonly SnippetStore store;

        public BuiltInInstaller(SnippetStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Installs every built-in snippet that was never installed here. A snippet that was installed before is not touched again (so edits and
        /// deletions stay), and one whose prefix is used by a snippet of the user is skipped. Returns how many were written.
        /// </summary>
        public int Install()
        {
            var marker = Path.Combine(store.Folder, MarkerFile);
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(marker))
            {
                foreach (var line in File.ReadAllLines(marker))
                {
                    if (line.Trim().Length > 0)
                    {
                        known.Add(line.Trim());
                    }
                }
            }

            var existing = store.Load().Snippets;
            var prefixes = new HashSet<string>(existing.Select(s => s.Prefix), StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(existing.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            var written = 0;
            var added = new List<string>();
            foreach (var snippet in BuiltInSnippets.All)
            {
                if (known.Contains(snippet.Id))
                {
                    continue;
                }

                added.Add(snippet.Id);
                if (ids.Contains(snippet.Id) || prefixes.Contains(snippet.Prefix))
                {
                    continue;
                }

                store.Save(snippet);
                written++;
            }

            if (added.Count > 0)
            {
                Directory.CreateDirectory(store.Folder);
                File.AppendAllLines(marker, added);
            }

            return written;
        }

        /// <summary>Writes the original text of the built-in snippet over the file of its id; false when there is no such built-in snippet.</summary>
        public bool Restore(string id)
        {
            var snippet = BuiltInSnippets.Find(id);
            if (snippet == null)
            {
                return false;
            }

            store.Save(snippet);
            return true;
        }
    }
}
