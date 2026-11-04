using System;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Three-part names: <c>OtherDb.|</c>, <c>OtherDb..|</c> and <c>OtherDb.dbo.|</c> need the objects of the other database.
    /// </summary>
    public static class ForeignDatabase
    {
        /// <summary>
        /// If the qualifier starts with the name of a database, returns it with the context as it looks inside that database
        /// (the database part of the qualifier removed). A schema of the current database wins over a database of the same name.
        /// </summary>
        public static bool TryResolve(CaretContext context, IDatabaseMetadata current, out string database, out CaretContext inside)
        {
            database = null;
            inside = context;
            if (context.Kind != ContextKind.ObjectName || context.Qualifier.Count == 0 || current == null)
            {
                return false;
            }

            var first = context.Qualifier[0];
            var isDatabase = first.Length > 0 &&
                current.Databases.Any(d => string.Equals(d, first, StringComparison.OrdinalIgnoreCase)) &&
                (context.Qualifier.Count == 2 || (context.Qualifier.Count == 1 && !current.HasSchema(first)));
            if (!isDatabase)
            {
                return false;
            }

            database = current.Databases.First(d => string.Equals(d, first, StringComparison.OrdinalIgnoreCase));
            inside = context.WithQualifier(context.Qualifier.Skip(1).ToList());
            return true;
        }
    }
}
