using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Completion
{
    /// <summary>The signature of the function or procedure being called at the caret and the parameter being typed.</summary>
    public sealed class ParameterInfo
    {
        public ParameterInfo(string name, IReadOnlyList<string> parameters, int current)
        {
            Name = name;
            Parameters = parameters;
            Current = current;
        }

        /// <summary>The name as written, e.g. <c>dbo.fnTax</c>.</summary>
        public string Name { get; }

        /// <summary>Each parameter as shown, e.g. <c>@Rate decimal(5,2) = 0</c>.</summary>
        public IReadOnlyList<string> Parameters { get; }

        /// <summary>Index of the parameter at the caret; -1 when the caret is past the last one.</summary>
        public int Current { get; }
    }

    /// <summary>
    /// Finds the call the caret is in: a function of the database between parentheses (<c>dbo.fn(a, |</c>) or a stored procedure after EXEC
    /// (<c>EXEC dbo.P @a = 1, |</c>). Built-in functions are not known.
    /// </summary>
    public static class ParameterInfoFinder
    {
        public static ParameterInfo Find(string text, int caret, IDatabaseMetadata metadata)
        {
            if (text == null || caret < 0 || caret > text.Length || metadata == null)
            {
                return null;
            }

            var all = Lexer.Tokenize(text);
            if (all.Any(t => (t.Kind == TokenKind.Comment || t.Kind == TokenKind.String) && t.Start < caret && caret <= t.End))
            {
                return null;
            }

            var tokens = all.Where(t => t.Kind != TokenKind.Comment).ToList();
            var before = tokens.Count(t => t.Start < caret);
            return FindFunctionCall(tokens, before, metadata) ?? FindProcedureCall(tokens, before, caret, metadata);
        }

        private static ParameterInfo FindFunctionCall(List<Token> tokens, int before, IDatabaseMetadata metadata)
        {
            var depth = 0;
            var commas = 0;
            for (var j = before - 1; j >= 0; j--)
            {
                var token = tokens[j];
                if (token.Kind == TokenKind.Semicolon)
                {
                    return null;
                }

                if (token.Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (token.Kind == TokenKind.Comma && depth == 0)
                {
                    commas++;
                }
                else if (token.Kind == TokenKind.OpenParen)
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    var name = NameBefore(tokens, j);
                    var function = name == null ? null : metadata.FindObject(name.Count > 1 ? name[name.Count - 2] : null, name[name.Count - 1]);
                    if (function != null && (function.Kind == DbObjectKind.ScalarFunction || function.Kind == DbObjectKind.TableFunction))
                    {
                        var parameters = metadata.GetFunctionParameters(function);
                        if (parameters.Count > 0)
                        {
                            return new ParameterInfo(string.Join(".", name), parameters.Select(Describe).ToList(), commas < parameters.Count ? commas : -1);
                        }
                    }

                    // A built-in function, IN (…), a subquery: look at the call around it.
                    commas = 0;
                }
            }

            return null;
        }

        private static ParameterInfo FindProcedureCall(List<Token> tokens, int before, int caret, IDatabaseMetadata metadata)
        {
            if (before == 0)
            {
                return null;
            }

            var bounds = StatementSplitter.Split(tokens);
            var statement = 0;
            foreach (var bound in bounds)
            {
                if (bound <= before - 1)
                {
                    statement = bound;
                }
            }

            var i = statement;
            if (i >= tokens.Count || !(tokens[i].IsWord("EXEC") || tokens[i].IsWord("EXECUTE")))
            {
                return null;
            }

            i++;
            if (i + 1 < tokens.Count && tokens[i].Kind == TokenKind.Variable && tokens[i + 1].Kind == TokenKind.Operator && tokens[i + 1].Text == "=")
            {
                i += 2;
            }

            var parts = new List<string>();
            var nameEnd = -1;
            while (i < tokens.Count && tokens[i].IsNamePart)
            {
                parts.Add(tokens[i].IdentifierValue);
                nameEnd = tokens[i].End;
                i++;
                if (i + 1 < tokens.Count && tokens[i].Kind == TokenKind.Dot && tokens[i + 1].IsNamePart)
                {
                    i++;
                }
                else
                {
                    break;
                }
            }

            if (parts.Count == 0 || caret <= nameEnd || i > before)
            {
                return null;
            }

            var schema = parts.Count > 1 ? parts[parts.Count - 2] : null;
            var procedure = metadata.Procedures.FirstOrDefault(p => string.Equals(p.Name, parts[parts.Count - 1], StringComparison.OrdinalIgnoreCase) &&
                (schema != null ? string.Equals(p.Schema, schema, StringComparison.OrdinalIgnoreCase) : string.Equals(p.Schema, metadata.DefaultSchema, StringComparison.OrdinalIgnoreCase)))
                ?? (schema == null ? metadata.Procedures.FirstOrDefault(p => string.Equals(p.Name, parts[parts.Count - 1], StringComparison.OrdinalIgnoreCase) && string.Equals(p.Schema, "dbo", StringComparison.OrdinalIgnoreCase)) : null);
            if (procedure == null && (schema == null || string.Equals(schema, SystemProcedures.Schema, StringComparison.OrdinalIgnoreCase) || string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase)))
            {
                procedure = SystemProcedures.Find(parts[parts.Count - 1]);
            }

            if (procedure == null || procedure.Parameters.Count == 0)
            {
                return null;
            }

            // The argument at the caret: counted by commas, or by name when it starts with @name =.
            var commas = 0;
            var depth = 0;
            var argumentStart = i;
            for (var j = i; j < before; j++)
            {
                if (tokens[j].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (tokens[j].Kind == TokenKind.CloseParen)
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (tokens[j].Kind == TokenKind.Comma && depth == 0)
                {
                    commas++;
                    argumentStart = j + 1;
                }
            }

            var current = commas;
            if (argumentStart + 1 < tokens.Count && argumentStart < before && tokens[argumentStart].Kind == TokenKind.Variable &&
                tokens[argumentStart + 1].Kind == TokenKind.Operator && tokens[argumentStart + 1].Text == "=")
            {
                var named = procedure.Parameters.ToList().FindIndex(p => string.Equals(p.Name, tokens[argumentStart].Text, StringComparison.OrdinalIgnoreCase));
                if (named >= 0)
                {
                    current = named;
                }
            }

            return new ParameterInfo(string.Join(".", parts), procedure.Parameters.Select(Describe).ToList(), current < procedure.Parameters.Count ? current : -1);
        }

        /// <summary>The name parts before the opening parenthesis (<c>dbo.fn(</c> → dbo, fn), or null when no name stands there.</summary>
        private static List<string> NameBefore(List<Token> tokens, int open)
        {
            var parts = new List<string>();
            var k = open - 1;
            while (k >= 0 && tokens[k].IsNamePart)
            {
                parts.Insert(0, tokens[k].IdentifierValue);
                if (k >= 2 && tokens[k - 1].Kind == TokenKind.Dot)
                {
                    k -= 2;
                }
                else
                {
                    break;
                }
            }

            return parts.Count == 0 || parts.Count > 2 ? null : parts;
        }

        private static string Describe(DbParameter parameter) =>
            parameter.Name + " " + parameter.TypeName + (parameter.HasDefault ? " = " + (parameter.DefaultText ?? "...") : string.Empty) + (parameter.IsOutput ? " OUTPUT" : string.Empty);
    }
}
