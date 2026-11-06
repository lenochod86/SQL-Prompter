using SqlPrompter.Core.Analysis;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Attached (<see cref="Suggestion.Tag"/>) to an item of <c>ALTER PROCEDURE |</c> and the like: after the name is committed, the whole statement
    /// is replaced by the current definition of the module, turned into ALTER (or CREATE OR ALTER).
    /// </summary>
    public sealed class ModuleScript
    {
        public ModuleScript(ModuleKind kind, string schema, string name, bool createOrAlter, int headerStart)
        {
            Kind = kind;
            Schema = schema;
            Name = name;
            CreateOrAlter = createOrAlter;
            HeaderStart = headerStart;
        }

        public ModuleKind Kind { get; }

        /// <summary>The schema of the module; null for a database DDL trigger.</summary>
        public string Schema { get; }

        public string Name { get; }

        /// <summary>The header typed was CREATE OR ALTER, so the script says the same.</summary>
        public bool CreateOrAlter { get; }

        /// <summary>The offset (in the text the context was made from) where the typed statement starts: the replaced text begins there.</summary>
        public int HeaderStart { get; }
    }
}
