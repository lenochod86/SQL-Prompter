using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Model
{
    /// <summary>
    /// The Summary of an object next to the script: the columns of a table or view (name, data type, nullability) or the parameters of a procedure
    /// or function (name, data type). Built from the metadata that is loaded already, so it never waits for the database; an object without
    /// columns or parameters has no summary (the script alone is shown).
    /// </summary>
    public sealed class ObjectSummary
    {
        private ObjectSummary(string[] headers, IReadOnlyList<string[]> rows)
        {
            Headers = headers;
            Rows = rows;
        }

        /// <summary>The column titles, e.g. "Column Name", "Data Type", "Nullability".</summary>
        public IReadOnlyList<string> Headers { get; }

        /// <summary>One array of cells per row, as many cells as headers.</summary>
        public IReadOnlyList<string[]> Rows { get; }

        /// <summary>The summary as tab separated lines (with the header line), for the clipboard.</summary>
        public string ToText() => string.Join("\r\n", new[] { string.Join("\t", Headers) }.Concat(Rows.Select(r => string.Join("\t", r))));

        /// <summary>
        /// The summary with a Description column from the database (MS_Description, #206): one cell per column of a table or view, long texts shortened to
        /// one line. The summary itself when there is no description or it is a list of parameters.
        /// </summary>
        public ObjectSummary WithDescriptions(IEnumerable<KeyValuePair<string, string>> descriptions)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in descriptions ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    var oneLine = string.Join(" ", pair.Value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
                    byName[pair.Key] = oneLine.Length > 80 ? oneLine.Substring(0, 80).TrimEnd() + "..." : oneLine;
                }
            }

            if (byName.Count == 0 || Headers.Count == 0 || Headers[0] != "Column Name")
            {
                return this;
            }

            return new ObjectSummary(
                Headers.Concat(new[] { "Description" }).ToArray(),
                Rows.Select(r => r.Concat(new[] { byName.TryGetValue(r[0], out var text) ? text : string.Empty }).ToArray()).ToList());
        }

        /// <summary>The summary of the object, or null when there is nothing to show.</summary>
        public static ObjectSummary For(IDatabaseMetadata metadata, string schema, string name)
        {
            if (metadata == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var procedure = metadata.Procedures?.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrEmpty(schema) || string.Equals(p.Schema, schema, StringComparison.OrdinalIgnoreCase)));
            if (procedure != null)
            {
                return OfParameters(procedure.Parameters);
            }

            var dbObject = metadata.FindObject(schema, name);
            if (dbObject == null)
            {
                return null;
            }

            if (dbObject.Kind == DbObjectKind.ScalarFunction || dbObject.Kind == DbObjectKind.TableFunction)
            {
                return OfParameters(metadata.GetFunctionParameters(dbObject));
            }

            var columns = metadata.GetColumns(dbObject);
            if (columns == null || columns.Count == 0)
            {
                return null;
            }

            return new ObjectSummary(
                new[] { "Column Name", "Data Type", "Nullability" },
                columns.Select(c => new[] { c.Name, c.TypeName, c.IsNullable ? "null" : "not null" }).ToList());
        }

        private static ObjectSummary OfParameters(IReadOnlyList<DbParameter> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return null;
            }

            return new ObjectSummary(
                new[] { "Parameter Name", "Data Type" },
                parameters.Select(p => new[] { p.Name, p.TypeName + (p.IsOutput ? " OUTPUT" : string.Empty) + (p.HasDefault ? " = " + (p.DefaultText ?? "default") : string.Empty) }).ToList());
        }
    }
}
