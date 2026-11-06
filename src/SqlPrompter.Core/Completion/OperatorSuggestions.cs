using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Completion
{
    /// <summary>The kinds of SQL Server types that decide which operators and values fit.</summary>
    public enum TypeClass
    {
        Unknown,
        Text,
        Number,
        Date,
        Bit,
        Guid,
    }

    /// <summary>Operators and predicates that fit a column in a condition (#204): chosen by the type of the column.</summary>
    public static class OperatorSuggestions
    {
        private static readonly string[] Texts = { "char", "varchar", "nchar", "nvarchar", "text", "ntext", "sysname" };
        private static readonly string[] Numbers = { "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "float", "real", "money", "smallmoney" };
        private static readonly string[] Dates = { "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset", "time" };

        /// <summary>The class of a type name such as nvarchar(50) or decimal(18,2).</summary>
        public static TypeClass Classify(string typeName)
        {
            var name = (typeName ?? string.Empty).Trim();
            var paren = name.IndexOf('(');
            if (paren >= 0)
            {
                name = name.Substring(0, paren).Trim();
            }

            name = name.Trim('[', ']').ToLowerInvariant();
            if (Texts.Contains(name))
            {
                return TypeClass.Text;
            }

            if (Numbers.Contains(name))
            {
                return TypeClass.Number;
            }

            if (Dates.Contains(name))
            {
                return TypeClass.Date;
            }

            return name == "bit" ? TypeClass.Bit : name == "uniqueidentifier" ? TypeClass.Guid : TypeClass.Unknown;
        }

        /// <summary>The operators for a column of the type, the likeliest first: <c>IS NULL</c> / <c>IS NOT NULL</c> first for a nullable column.</summary>
        public static IReadOnlyList<string> For(TypeClass type, bool nullable)
        {
            var list = new List<string> { "=", "<>" };
            switch (type)
            {
                case TypeClass.Text:
                    list.AddRange(new[] { "LIKE", "NOT LIKE" });
                    break;
                case TypeClass.Number:
                case TypeClass.Date:
                    list.AddRange(new[] { "<", ">", "<=", ">=", "BETWEEN" });
                    break;
            }

            list.AddRange(new[] { "IN", "NOT IN" });
            var nulls = new[] { "IS NULL", "IS NOT NULL" };
            if (nullable)
            {
                list.InsertRange(0, nulls);
            }
            else
            {
                list.AddRange(nulls);
            }

            return list;
        }

        /// <summary>The operators as list items; <c>IN</c> comes with its parentheses and the caret between them.</summary>
        internal static IEnumerable<Suggestion> Build(DbColumn column)
        {
            var order = 0;
            foreach (var op in For(Classify(column.TypeName), column.IsNullable))
            {
                var list = op.EndsWith("IN", StringComparison.Ordinal);
                yield return new Suggestion(SuggestionKind.Keyword, op, list ? op + " ()" : op, op, null, "operator for " + column.Name + " " + column.TypeName,
                    FirstGroup + order++, caretBack: list ? 1 : 0);
            }
        }

        /// <summary>The group of the first operator; every operator has a group of its own so that the order is kept.</summary>
        private const int FirstGroup = -40;
    }
}
