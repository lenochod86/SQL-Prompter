using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;

namespace SqlPrompter.Tests
{
    internal static class TestSupport
    {
        /// <summary>Rozebere SQL, ve kterém „|“ označuje pozici kurzoru.</summary>
        public static CaretContext Analyze(string sqlWithCaret)
        {
            var caret = sqlWithCaret.IndexOf('|');
            var sql = sqlWithCaret.Remove(caret, 1);
            return ContextAnalyzer.Analyze(sql, caret);
        }

        /// <summary>Malá databáze: dbo.Users, dbo.Orders, sales.Customers, pohled dbo.ActiveUsers, sys.objects.</summary>
        public static DatabaseSnapshot SampleDatabase(bool withColumns = true)
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Orders", DbObjectKind.Table, false),
                new DbObject(3, "sales", "Customers", DbObjectKind.Table, false),
                new DbObject(4, "dbo", "ActiveUsers", DbObjectKind.View, false),
                new DbObject(5, "dbo", "Order Details", DbObjectKind.Table, false),
                new DbObject(100, "sys", "objects", DbObjectKind.View, true),
            };

            var snapshot = new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales", "sys" }, objects, "v1");
            if (!withColumns)
            {
                return snapshot;
            }

            return snapshot.WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
            {
                [1] = Columns("Id", "Name", "Email"),
                [2] = Columns("Id", "UserId", "Total"),
                [3] = Columns("CustomerId", "Name"),
                [4] = Columns("Id", "Name"),
                [5] = Columns("Order Id", "Qty"),
                [100] = Columns("object_id", "name"),
            });
        }

        public static IReadOnlyList<DbColumn> Columns(params string[] names) =>
            names.Select(n => new DbColumn(n, "int", false)).ToList();
    }
}
