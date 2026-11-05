using System;

namespace SqlPrompter.Core.Completion
{
    /// <summary>Placeholder values for a column or a parameter of a given type, as written in an INSERT ... VALUES or an EXEC.</summary>
    public static class SqlDefaults
    {
        /// <param name="typeName">Type with its length as the model holds it, e.g. <c>nvarchar(50)</c>.</param>
        public static string ValueFor(string typeName, bool isNullable)
        {
            if (isNullable)
            {
                return "NULL";
            }

            var type = (typeName ?? string.Empty).Trim();
            var paren = type.IndexOf('(');
            if (paren >= 0)
            {
                type = type.Substring(0, paren);
            }

            switch (type.ToLowerInvariant())
            {
                case "char":
                case "varchar":
                case "text":
                case "xml":
                    return "''";
                case "nchar":
                case "nvarchar":
                case "ntext":
                    return "N''";
                case "bit":
                case "tinyint":
                case "smallint":
                case "int":
                case "bigint":
                case "decimal":
                case "numeric":
                case "float":
                case "real":
                case "money":
                case "smallmoney":
                    return "0";
                case "date":
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "datetimeoffset":
                    return "'1900-01-01'";
                case "time":
                    return "'00:00:00'";
                case "uniqueidentifier":
                    return "'00000000-0000-0000-0000-000000000000'";
                case "binary":
                case "varbinary":
                case "image":
                    return "0x";
                default:
                    return "NULL";
            }
        }
    }
}
