using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Completion
{
    public enum SuggestionKind
    {
        Keyword,
        Schema,
        Table,
        View,
        TableFunction,
        Column,
        Alias,
        Function,
        DataType,
    }

    public sealed class Suggestion
    {
        public Suggestion(SuggestionKind kind, string displayText, string insertText, string filterText, string suffix, string description, int group)
        {
            Kind = kind;
            DisplayText = displayText;
            InsertText = insertText;
            FilterText = filterText;
            Suffix = suffix;
            Description = description;
            Group = group;
        }

        public SuggestionKind Kind { get; }

        public string DisplayText { get; }

        public string InsertText { get; }

        /// <summary>Text, podle kterého se seznam filtruje při psaní.</summary>
        public string FilterText { get; }

        /// <summary>Krátký doplněk vpravo v seznamu, např. schéma nebo alias.</summary>
        public string Suffix { get; }

        public string Description { get; }

        /// <summary>Pořadí skupiny v seznamu, menší dřív.</summary>
        public int Group { get; }

        /// <summary>Klíč řazení: skupina, potom text.</summary>
        public string SortText => Group.ToString("D2") + DisplayText;

        public override string ToString() => Kind + " " + DisplayText + " → " + InsertText;
    }

    public static class SuggestionBuilder
    {
        public static IReadOnlyList<Suggestion> Build(CaretContext context, IDatabaseMetadata metadata)
        {
            switch (context.Kind)
            {
                case ContextKind.Keyword:
                    return KeywordSuggestions(context, 0).ToList();
                case ContextKind.ObjectName:
                    return ObjectSuggestions(context, metadata).ToList();
                case ContextKind.Expression:
                    return ExpressionSuggestions(context, metadata).ToList();
                case ContextKind.DataType:
                    return DataTypeSuggestions(context, 0).ToList();
                case ContextKind.Fallback:
                    return KeywordSuggestions(context, 0)
                        .Concat(FunctionSuggestions(context, 2))
                        .Concat(DataTypeSuggestions(context, 3))
                        .ToList();
                default:
                    return Array.Empty<Suggestion>();
            }
        }

        private static IEnumerable<Suggestion> FunctionSuggestions(CaretContext context, int group)
        {
            foreach (var function in Keywords.BuiltInFunctions)
            {
                var text = Keywords.ApplyCase(function, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.Function, text, text, text, null, "vestavěná funkce", group);
            }
        }

        private static IEnumerable<Suggestion> DataTypeSuggestions(CaretContext context, int group)
        {
            foreach (var type in Keywords.DataTypes)
            {
                var text = Keywords.ApplyCase(type, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.DataType, text, text, text, null, "datový typ", group);
            }
        }

        private static IEnumerable<Suggestion> KeywordSuggestions(CaretContext context, int firstGroup)
        {
            var preferred = new HashSet<string>(context.PreferredKeywords, StringComparer.OrdinalIgnoreCase);
            foreach (var keyword in context.PreferredKeywords.Concat(Keywords.All).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var text = Keywords.ApplyCase(keyword, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.Keyword, text, text, text, null, null,
                    preferred.Contains(keyword) ? firstGroup : firstGroup + 1);
            }
        }

        private static IEnumerable<Suggestion> ObjectSuggestions(CaretContext context, IDatabaseMetadata metadata)
        {
            if (metadata == null)
            {
                yield break;
            }

            var quote = context.QuoteIdentifiers;
            if (context.Qualifier.Count == 1)
            {
                var schema = context.Qualifier[0];
                foreach (var dbObject in metadata.Objects.Where(o => string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return ObjectSuggestion(dbObject, SqlIdentifier.Quote(dbObject.Name, quote), null);
                }

                yield break;
            }

            foreach (var dbObject in metadata.Objects.Where(o => !o.IsSystem))
            {
                var insert = SqlIdentifier.Quote(dbObject.Schema, quote) + "." + SqlIdentifier.Quote(dbObject.Name, quote);
                yield return ObjectSuggestion(dbObject, insert, dbObject.Schema);
            }

            foreach (var schema in metadata.Schemas)
            {
                yield return new Suggestion(SuggestionKind.Schema, schema, SqlIdentifier.Quote(schema, quote), schema, null, "schéma", 1);
            }

            foreach (var keyword in context.PreferredKeywords)
            {
                var text = Keywords.ApplyCase(keyword, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.Keyword, text, text, text, null, null, 2);
            }
        }

        private static Suggestion ObjectSuggestion(DbObject dbObject, string insertText, string suffix)
        {
            SuggestionKind kind;
            string description;
            switch (dbObject.Kind)
            {
                case DbObjectKind.View:
                    kind = SuggestionKind.View;
                    description = "pohled";
                    break;
                case DbObjectKind.TableFunction:
                    kind = SuggestionKind.TableFunction;
                    description = "tabulková funkce";
                    break;
                default:
                    kind = SuggestionKind.Table;
                    description = "tabulka";
                    break;
            }

            return new Suggestion(kind, dbObject.Name, insertText, dbObject.Name, suffix, description + " " + dbObject, 0);
        }

        private static IEnumerable<Suggestion> ExpressionSuggestions(CaretContext context, IDatabaseMetadata metadata)
        {
            var quote = context.QuoteIdentifiers;
            if (context.QualifiedSource != null)
            {
                var dbObject = Readiness.Resolve(context.QualifiedSource, metadata);
                var columns = dbObject != null ? metadata.GetColumns(dbObject) : null;
                foreach (var column in columns ?? Array.Empty<DbColumn>())
                {
                    yield return ColumnSuggestion(column, SqlIdentifier.Quote(column.Name, quote), null, dbObject, 0);
                }

                yield break;
            }

            var resolved = context.Sources
                .Select(s => new { Source = s, Object = Readiness.Resolve(s, metadata) })
                .Where(x => x.Object != null && metadata.GetColumns(x.Object) != null)
                .ToList();

            // Sloupec se stejným názvem ve více zdrojích stejného bloku se vkládá s aliasem.
            var ambiguous = new HashSet<string>(
                resolved.Where(x => x.Source.ScopeLevel == 0)
                    .SelectMany(x => metadata.GetColumns(x.Object).Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                    .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            foreach (var x in resolved)
            {
                var reference = x.Source.ReferenceName;
                var inner = x.Source.ScopeLevel == 0;
                foreach (var column in metadata.GetColumns(x.Object))
                {
                    var name = SqlIdentifier.Quote(column.Name, quote);
                    var qualify = !inner || ambiguous.Contains(column.Name);
                    var insert = qualify ? SqlIdentifier.Quote(reference, quote) + "." + name : name;
                    yield return ColumnSuggestion(column, insert, reference, x.Object, inner ? 0 : 2);
                }
            }

            foreach (var source in context.Sources.Where(s => s.ReferenceName != null)
                         .GroupBy(s => s.ReferenceName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                var target = source.Kind == TableSourceKind.Derived ? "odvozená tabulka" : string.Join(".", source.NameParts);
                yield return new Suggestion(SuggestionKind.Alias, source.ReferenceName, SqlIdentifier.Quote(source.ReferenceName, quote),
                    source.ReferenceName, source.Alias != null ? target : null, (source.Alias != null ? "alias " : "zdroj ") + target, 1);
            }

            foreach (var function in FunctionSuggestions(context, 3))
            {
                yield return function;
            }

            foreach (var keyword in KeywordSuggestions(context, 4))
            {
                yield return keyword;
            }
        }

        private static Suggestion ColumnSuggestion(DbColumn column, string insertText, string suffix, DbObject owner, int group)
        {
            var description = column.Name + " " + column.TypeName + (column.IsNullable ? " null" : " not null") + " (" + owner + ")";
            return new Suggestion(SuggestionKind.Column, column.Name, insertText, column.Name, suffix, description, group);
        }
    }
}
