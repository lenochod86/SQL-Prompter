using System;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Four-part names: <c>Srv.|</c> offers the databases of a linked server, <c>Srv.Db.|</c> its schemas and objects,
    /// <c>Srv.Db.schema.|</c> the objects of a schema. The contents of a linked server are loaded lazily by the caller.
    /// </summary>
    public static class LinkedServerName
    {
        /// <summary>
        /// If the qualifier starts with the name of a linked server (and is not a schema or a database of the current database,
        /// which win), returns the server, the database named after it (null for "Srv.") and the context as it looks inside.
        /// </summary>
        public static bool TryResolve(CaretContext context, IDatabaseMetadata current, out string server, out string database, out CaretContext inside)
        {
            server = null;
            database = null;
            inside = context;
            if (context.Kind != ContextKind.ObjectName || context.Qualifier.Count == 0 || context.Qualifier.Count > 3 || current == null)
            {
                return false;
            }

            var first = context.Qualifier[0];
            var known = current.LinkedServers.FirstOrDefault(s => string.Equals(s, first, StringComparison.OrdinalIgnoreCase));
            if (known == null || first.Length == 0)
            {
                return false;
            }

            // A schema or a database of the current database with the same name wins for the shorter forms.
            if (context.Qualifier.Count == 1 && (current.HasSchema(first) || current.Databases.Any(d => string.Equals(d, first, StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            if (context.Qualifier.Count == 2 && current.Databases.Any(d => string.Equals(d, first, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            server = known;
            if (context.Qualifier.Count == 1)
            {
                inside = context.WithQualifier(new string[0], inOtherDatabase: false);
                return true;
            }

            database = context.Qualifier[1];
            if (database.Length == 0)
            {
                return false;
            }

            inside = context.WithQualifier(context.Qualifier.Skip(2).ToList());
            return true;
        }
    }
}
