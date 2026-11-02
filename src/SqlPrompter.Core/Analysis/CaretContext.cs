using System;
using System.Collections.Generic;

namespace SqlPrompter.Core.Analysis
{
    public enum ContextKind
    {
        /// <summary>Žádné našeptávání: řetězec, komentář, číslo.</summary>
        None,

        /// <summary>
        /// Uživatel píše nový nebo neznámý název (alias za AS, proměnná, #tabulka, neznámý kvalifikátor).
        /// Seznam je prázdný, ale rozšíření se účastní, aby se neukázal vestavěný seznam.
        /// </summary>
        Name,

        /// <summary>Datový typ: CAST(x AS …), DECLARE @x ….</summary>
        DataType,

        /// <summary>Místo, kterému rozšíření zatím nerozumí: klíčová slova, funkce a datové typy.</summary>
        Fallback,

        /// <summary>Jen klíčová slova: začátek příkazu nebo místo za zdrojem tabulky.</summary>
        Keyword,

        /// <summary>Název tabulky nebo pohledu (za FROM, JOIN, UPDATE, INTO …).</summary>
        ObjectName,

        /// <summary>Výraz: sloupce, aliasy, funkce a klíčová slova.</summary>
        Expression,
    }

    public enum TableSourceKind
    {
        /// <summary>Tabulka, pohled nebo tabulková funkce s názvem.</summary>
        Object,

        /// <summary>Odvozená tabulka (SELECT …) alias.</summary>
        Derived,

        /// <summary>Tabulková proměnná @t.</summary>
        Variable,

        /// <summary>Dočasná tabulka #t.</summary>
        TempTable,
    }

    /// <summary>Zdroj řádků ve FROM, JOIN, APPLY, UPDATE nebo MERGE.</summary>
    public sealed class TableSource
    {
        public TableSource(TableSourceKind kind, IReadOnlyList<string> nameParts, string alias, int scopeLevel)
        {
            Kind = kind;
            NameParts = nameParts ?? Array.Empty<string>();
            Alias = alias;
            ScopeLevel = scopeLevel;
        }

        public TableSourceKind Kind { get; }

        /// <summary>Části názvu, např. [dbo, Users]. U odvozené tabulky prázdné.</summary>
        public IReadOnlyList<string> NameParts { get; }

        public string Alias { get; }

        /// <summary>0 = blok s kurzorem, 1 = nadřazený dotaz (korelovaný poddotaz) atd.</summary>
        public int ScopeLevel { get; }

        public string ObjectName => NameParts.Count > 0 ? NameParts[NameParts.Count - 1] : null;

        public string Schema => NameParts.Count > 1 ? NameParts[NameParts.Count - 2] : null;

        public string Database => NameParts.Count > 2 ? NameParts[NameParts.Count - 3] : null;

        /// <summary>Jak se na zdroj odkazuje ve výrazu: alias, jinak název objektu.</summary>
        public string ReferenceName => Alias ?? ObjectName;

        public override string ToString() => string.Join(".", NameParts) + (Alias != null ? " " + Alias : string.Empty) + " @" + ScopeLevel;
    }

    /// <summary>Výsledek rozboru textu na pozici kurzoru.</summary>
    public sealed class CaretContext
    {
        public static readonly CaretContext None = new CaretContext(ContextKind.None, 0, 0, string.Empty);

        public CaretContext(ContextKind kind, int replaceStart, int replaceLength, string typedPrefix)
        {
            Kind = kind;
            ReplaceStart = replaceStart;
            ReplaceLength = replaceLength;
            TypedPrefix = typedPrefix ?? string.Empty;
        }

        public ContextKind Kind { get; }

        /// <summary>Začátek úseku, který návrh nahradí (slovo pod kurzorem).</summary>
        public int ReplaceStart { get; }

        public int ReplaceLength { get; }

        /// <summary>Text slova od jeho začátku po kurzor.</summary>
        public string TypedPrefix { get; }

        /// <summary>Uživatel začal psát identifikátor s „[“, vkládá se tedy vždy v závorkách.</summary>
        public bool QuoteIdentifiers { get; internal set; }

        /// <summary>Části názvu před slovem pod kurzorem, např. [dbo] u „dbo.Us|“.</summary>
        public IReadOnlyList<string> Qualifier { get; internal set; } = Array.Empty<string>();

        /// <summary>Zdroje tabulek viditelné z pozice kurzoru, nejbližší blok první.</summary>
        public IReadOnlyList<TableSource> Sources { get; internal set; } = Array.Empty<TableSource>();

        /// <summary>U „alias.|“ zdroj, na který kvalifikátor odkazuje.</summary>
        public TableSource QualifiedSource { get; internal set; }

        /// <summary>Klíčová slova, která mají v tomto místě přednost.</summary>
        public IReadOnlyList<string> PreferredKeywords { get; internal set; } = Array.Empty<string>();

        /// <summary>
        /// Uživatel nejspíš píše vlastní název (alias), proto výběr nemá být pevný,
        /// aby Enter nevložil první návrh.
        /// </summary>
        public bool SoftSelection { get; internal set; }

        public override string ToString() => Kind + " [" + string.Join(".", Qualifier) + "] '" + TypedPrefix + "'";
    }
}
