using System;
using System.Collections.Generic;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Completion
{
    public enum ReadinessState
    {
        /// <summary>Lze sestavit úplný seznam návrhů.</summary>
        Ready,

        /// <summary>Metadata databáze ještě nejsou načtená.</summary>
        NeedsMetadata,

        /// <summary>Chybí sloupce některých objektů; po jejich načtení bude Ready.</summary>
        NeedsColumns,

        /// <summary>Kontext odkazuje na něco, co rozšíření nezná (CTE, dočasná tabulka, jiná databáze …).</summary>
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

        /// <summary>Objekty, jejichž sloupce je potřeba načíst.</summary>
        public IReadOnlyList<DbObject> MissingColumns { get; }

        /// <summary>
        /// Zjistí, jestli metadata stačí na úplný seznam a co je případně potřeba načíst.
        /// Seznam se nabízí i tak; chybějící části se doplní při dalším našeptání.
        /// </summary>
        public static Readiness Evaluate(CaretContext context, IDatabaseMetadata metadata)
        {
            switch (context.Kind)
            {
                case ContextKind.Keyword:
                case ContextKind.Name:
                case ContextKind.DataType:
                case ContextKind.Fallback:
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
                    return EvaluateExpression(context, metadata);

                default:
                    return new Readiness(ReadinessState.Unsupported, null);
            }
        }

        /// <summary>Najde objekt v metadatech pro zdroj tabulky, nebo null.</summary>
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

        private static Readiness EvaluateExpression(CaretContext context, IDatabaseMetadata metadata)
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
                var dbObject = Resolve(source, metadata);
                if (dbObject == null)
                {
                    // Vnější zdroje jsou jen doplněk; neznámý zdroj v aktuálním bloku by seznam ochudil.
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
