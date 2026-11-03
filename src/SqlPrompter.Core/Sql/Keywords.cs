using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Sql
{
    public static class Keywords
    {
        /// <summary>Reserved T-SQL keywords (documentation "Reserved Keywords (Transact-SQL)").</summary>
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION", "BACKUP", "BEGIN", "BETWEEN", "BREAK",
            "BROWSE", "BULK", "BY", "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED", "COALESCE",
            "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT", "CONTAINS", "CONTAINSTABLE", "CONTINUE", "CONVERT",
            "CREATE", "CROSS", "CURRENT", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER", "CURSOR",
            "DATABASE", "DBCC", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DENY", "DESC", "DISK", "DISTINCT",
            "DISTRIBUTED", "DOUBLE", "DROP", "DUMP", "ELSE", "END", "ERRLVL", "ESCAPE", "EXCEPT", "EXEC", "EXECUTE",
            "EXISTS", "EXIT", "EXTERNAL", "FETCH", "FILE", "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FREETEXTTABLE",
            "FROM", "FULL", "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING", "HOLDLOCK", "IDENTITY", "IDENTITY_INSERT",
            "IDENTITYCOL", "IF", "IN", "INDEX", "INNER", "INSERT", "INTERSECT", "INTO", "IS", "JOIN", "KEY", "KILL",
            "LEFT", "LIKE", "LINENO", "LOAD", "MERGE", "NATIONAL", "NOCHECK", "NONCLUSTERED", "NOT", "NULL", "NULLIF",
            "OF", "OFF", "OFFSETS", "ON", "OPEN", "OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML", "OPTION",
            "OR", "ORDER", "OUTER", "OVER", "PERCENT", "PIVOT", "PLAN", "PRECISION", "PRIMARY", "PRINT", "PROC",
            "PROCEDURE", "PUBLIC", "RAISERROR", "READ", "READTEXT", "RECONFIGURE", "REFERENCES", "REPLICATION",
            "RESTORE", "RESTRICT", "RETURN", "REVERT", "REVOKE", "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE",
            "SAVE", "SCHEMA", "SECURITYAUDIT", "SELECT", "SEMANTICKEYPHRASETABLE", "SEMANTICSIMILARITYDETAILSTABLE",
            "SEMANTICSIMILARITYTABLE", "SESSION_USER", "SET", "SETUSER", "SHUTDOWN", "SOME", "STATISTICS",
            "SYSTEM_USER", "TABLE", "TABLESAMPLE", "TEXTSIZE", "THEN", "TO", "TOP", "TRAN", "TRANSACTION", "TRIGGER",
            "TRUNCATE", "TRY_CONVERT", "TSEQUAL", "UNION", "UNIQUE", "UNPIVOT", "UPDATE", "UPDATETEXT", "USE", "USER",
            "VALUES", "VARYING", "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE", "WITH", "WITHIN", "WRITETEXT",
        };

        /// <summary>Words that cannot be an alias after a table source even though they are not reserved.</summary>
        private static readonly HashSet<string> NotAlias = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "APPLY", "USING", "OUTPUT", "WINDOW", "OFFSET", "ROWS", "GO",
        };

        /// <summary>Keywords offered at the start of a statement.</summary>
        public static readonly IReadOnlyList<string> StatementStart = new[]
        {
            "SELECT", "INSERT INTO", "UPDATE", "DELETE FROM", "WITH", "DECLARE", "SET", "IF", "BEGIN", "END",
            "CREATE", "ALTER", "DROP", "EXEC", "TRUNCATE TABLE", "MERGE", "PRINT", "RETURN", "USE", "WHILE",
            "BEGIN TRANSACTION", "COMMIT", "ROLLBACK", "GO",
        };

        /// <summary>Keywords offered after a table source in FROM.</summary>
        public static readonly IReadOnlyList<string> AfterTableSource = new[]
        {
            "WHERE", "JOIN", "INNER JOIN", "LEFT JOIN", "LEFT OUTER JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN",
            "CROSS APPLY", "OUTER APPLY", "ON", "AS", "GROUP BY", "ORDER BY", "HAVING", "UNION", "UNION ALL", "WITH",
            "OPTION",
        };

        /// <summary>All keywords the extension offers.</summary>
        public static readonly IReadOnlyList<string> All = StatementStart
            .Concat(AfterTableSource)
            .Concat(new[]
            {
                "AND", "OR", "NOT", "NULL", "IS NULL", "IS NOT NULL", "IN", "EXISTS", "LIKE", "BETWEEN", "CASE",
                "WHEN", "THEN", "ELSE", "DISTINCT", "TOP", "ASC", "DESC", "OVER", "PARTITION BY", "OFFSET",
                "FETCH NEXT", "ROWS", "ONLY", "FROM", "INTO", "VALUES", "OUTPUT", "COLLATE", "ALL", "ANY", "SOME",
                "EXCEPT", "INTERSECT", "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "INDEX", "SCHEMA", "TRIGGER",
                "PRIMARY KEY", "FOREIGN KEY", "REFERENCES", "DEFAULT", "IDENTITY", "CONSTRAINT", "UNIQUE", "CHECK",
                "TRY", "CATCH", "TRANSACTION", "PIVOT", "UNPIVOT", "INNER", "OUTER", "LEFT", "RIGHT", "FULL", "CROSS",
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        /// <summary>Common built-in functions. Without them expressions would offer less than the built-in IntelliSense.</summary>
        public static readonly IReadOnlyList<string> BuiltInFunctions = new[]
        {
            "ABS", "AVG", "CAST", "CEILING", "CHARINDEX", "CHOOSE", "COALESCE", "CONCAT", "CONCAT_WS", "CONVERT",
            "COUNT", "COUNT_BIG", "DATALENGTH", "DATEADD", "DATEDIFF", "DATEFROMPARTS", "DATENAME", "DATEPART", "DAY",
            "DB_NAME", "DENSE_RANK", "EOMONTH", "FIRST_VALUE", "FLOOR", "FORMAT", "GETDATE", "GETUTCDATE", "IIF",
            "ISJSON", "ISNULL", "JSON_QUERY", "JSON_VALUE", "LAG", "LAST_VALUE", "LEAD", "LEN", "LOWER", "LTRIM",
            "MAX", "MIN", "MONTH", "NEWID", "NTILE", "NULLIF", "OBJECT_ID", "OBJECT_NAME", "PATINDEX", "POWER", "RANK",
            "REPLACE", "REPLICATE", "REVERSE", "ROUND", "ROW_NUMBER", "RTRIM", "SCHEMA_NAME", "SCOPE_IDENTITY",
            "SQRT", "STRING_AGG", "STRING_SPLIT", "STUFF", "SUBSTRING", "SUM", "SYSDATETIME", "SYSUTCDATETIME",
            "TRANSLATE", "TRIM", "TRY_CAST", "TRY_CONVERT", "UPPER", "YEAR",
        };

        /// <summary>SQL Server system data types.</summary>
        public static readonly IReadOnlyList<string> DataTypes = new[]
        {
            "BIGINT", "BINARY", "BIT", "CHAR", "DATE", "DATETIME", "DATETIME2", "DATETIMEOFFSET", "DECIMAL", "FLOAT",
            "GEOGRAPHY", "GEOMETRY", "HIERARCHYID", "IMAGE", "INT", "MONEY", "NCHAR", "NTEXT", "NUMERIC", "NVARCHAR",
            "REAL", "ROWVERSION", "SMALLDATETIME", "SMALLINT", "SMALLMONEY", "SQL_VARIANT", "SYSNAME", "TEXT", "TIME",
            "TINYINT", "UNIQUEIDENTIFIER", "VARBINARY", "VARCHAR", "XML", "TABLE",
        };

        public static bool IsReserved(string word) => word != null && Reserved.Contains(word);

        /// <summary>A word that cannot be the alias of a table source.</summary>
        public static bool CannotBeAlias(string word) => IsReserved(word) || NotAlias.Contains(word);

        /// <summary>
        /// Keywords are inserted in upper case unless the user started typing in lower case.
        /// </summary>
        public static string ApplyCase(string keyword, string typedPrefix)
        {
            if (!string.IsNullOrEmpty(typedPrefix) && typedPrefix.Any(char.IsLetter) && !typedPrefix.Any(char.IsUpper))
            {
                return keyword.ToLowerInvariant();
            }

            return keyword;
        }
    }
}
