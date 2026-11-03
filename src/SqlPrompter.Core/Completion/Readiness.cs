using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Completion
{
    public enum ReadinessState
    {
        /// <summary>A complete list of suggestions can be built.</summary>
        Ready,

        /// <summary>Database metadata is not loaded yet.</summary>
        NeedsMetadata,

        /// <summary>Columns of some objects are missing; Ready once they are loaded.</summary>
        NeedsColumns,

        /// <summary>The context refers to something the extension does not know (CTE, temporary table, another database …).</summary>
        Unsupported,
    }

    public sealed class Readiness
    {
        private Readiness(ReadinessState state, IReadOnlyList<DbObject> missingColumns)
        {
            State = state;
            MissingColumns = missingColumns ?? Array.Empty<DbObject>();
        }

        public ReadinessState State { get; }

        /// <summary>Objects whose columns need to be loaded.</summary>
        public IReadOnlyList<DbObject> MissingColumns { get; }

        /// <summary>
        /// Determines whether the metadata is enough for a complete list and what needs to be loaded.
        /// The list is offered anyway; missing parts are added on the next completion.
        /// </summary>
        public static Readiness Evaluate(CaretContext context, IDatabaseMetadata metadata, ScriptModel script = null)
        {
            switch (context.Kind)
            {
                case ContextKind.Keyword:
                case ContextKind.Name:
                case ContextKind.DataType:
                case ContextKind.Fallback:
                case ContextKind.Variable:
                    return new Readiness(ReadinessState.Ready, null);

                case ContextKind.ObjectName:
                    if (metadata == null)
                    {
                        return new Readiness(ReadinessState.NeedsMetadata, null);
                    }

                    return context.Qualifier.Count == 1 && !metadata.HasSchema(context.Qualifier[0])
                        ? new Readiness(ReadinessState.Unsupported, null)
                        : new Readiness(ReadinessState.Ready, null);

                case ContextKind.Expression:
                    return EvaluateExpression(context, metadata, script ?? ScriptModel.Empty);

                default:
                    return new Readiness(ReadinessState.Unsupported, null);
            }
        }

        /// <summary>Finds the object in the metadata for a table source, or null.</summary>
        public static DbObject Resolve(TableSource source, IDatabaseMetadata metadata)
        {
            if (source.Kind != TableSourceKind.Object || metadata == null || source.NameParts.Count > 3)
            {
                return null;
            }

            if (source.Database != null && !string.Equals(source.Database, metadata.DatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return metadata.FindObject(string.IsNullOrEmpty(source.Schema) ? null : source.Schema, source.ObjectName);
        }

        private static Readiness EvaluateExpression(CaretContext context, IDatabaseMetadata metadata, ScriptModel script)
        {
            var needed = new List<TableSource>();
            if (context.QualifiedSource != null)
            {
                needed.Add(context.QualifiedSource);
            }
            else
            {
                needed.AddRange(context.Sources);
            }

            if (needed.Count == 0)
            {
                return new Readiness(ReadinessState.Ready, null);
            }

            if (metadata == null)
            {
                return new Readiness(ReadinessState.NeedsMetadata, null);
            }

            var missing = new List<DbObject>();
            foreach (var source in needed)
            {
                var scripted = script.Resolve(source, metadata);
                if (scripted != null)
                {
                    // CTE, temporary table, table variable or derived table; SELECT * in it needs the columns of database objects.
                    missing.AddRange(scripted.MissingColumns.Where(m => !missing.Contains(m)));
                    continue;
                }

                var dbObject = Resolve(source, metadata);
                if (dbObject == null)
                {
                    // Outer sources are only a bonus; an unknown source in the current block would make the list incomplete.
                    if (source.ScopeLevel == 0 || source == context.QualifiedSource)
                    {
                        return new Readiness(ReadinessState.Unsupported, null);
                    }

                    continue;
                }

                if (metadata.GetColumns(dbObject) == null && !missing.Contains(dbObject))
                {
                    missing.Add(dbObject);
                }
            }

            return missing.Count > 0
                ? new Readiness(ReadinessState.NeedsColumns, missing)
                : new Readiness(ReadinessState.Ready, null);
        }
    }
}
