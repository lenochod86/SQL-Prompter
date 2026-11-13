using System.Runtime.Serialization;

namespace SqlPrompter.Core.Settings
{
    /// <summary>Code analysis: which problems are marked with a squiggle in the editor.</summary>
    [DataContract]
    public sealed class AnalysisSettings
    {
        public AnalysisSettings()
        {
            SetDefaults();
        }

        /// <summary>The whole analysis; off means no squiggles and no work in the background.</summary>
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "equalsNull")]
        public bool EqualsNull { get; set; }

        [DataMember(Name = "joinWithoutOn")]
        public bool JoinWithoutOn { get; set; }

        [DataMember(Name = "insertWithoutColumns")]
        public bool InsertWithoutColumns { get; set; }

        [DataMember(Name = "unusedVariable")]
        public bool UnusedVariable { get; set; }

        [DataMember(Name = "selectStar")]
        public bool SelectStar { get; set; }

        [DataMember(Name = "lockHint")]
        public bool LockHint { get; set; }

        /// <summary>A script that stops in the middle of a construct (unclosed parenthesis, CTE without a statement, FROM without a table ...).</summary>
        [DataMember(Name = "unexpectedEnd")]
        public bool UnexpectedEnd { get; set; }

        /// <summary>A function on a column in WHERE / ON / HAVING (YEAR(d) = 2025, UPPER(c) = 'X'): the index on the column cannot be used.</summary>
        [DataMember(Name = "functionOnColumn")]
        public bool FunctionOnColumn { get; set; }

        /// <summary>LIKE '%text': a leading wildcard cannot use an index.</summary>
        [DataMember(Name = "leadingWildcard")]
        public bool LeadingWildcard { get; set; }

        /// <summary>SELECT TOP n without ORDER BY returns arbitrary rows.</summary>
        [DataMember(Name = "topWithoutOrderBy")]
        public bool TopWithoutOrderBy { get; set; }

        /// <summary>@@IDENTITY (use SCOPE_IDENTITY()).</summary>
        [DataMember(Name = "atAtIdentity")]
        public bool AtAtIdentity { get; set; }

        /// <summary>varchar, nvarchar, char ... without a length in DECLARE, CAST and CONVERT (the length is 1 or 30).</summary>
        [DataMember(Name = "typeWithoutLength")]
        public bool TypeWithoutLength { get; set; }

        /// <summary>NOT IN (SELECT ...) returns nothing when the subquery returns a NULL.</summary>
        [DataMember(Name = "notInSubquery")]
        public bool NotInSubquery { get; set; }

        /// <summary>FROM a, b WHERE a.x = b.x (use JOIN ... ON).</summary>
        [DataMember(Name = "oldStyleJoin")]
        public bool OldStyleJoin { get; set; }

        /// <summary>An unqualified column that two tables of the query have (needs the loaded metadata).</summary>
        [DataMember(Name = "ambiguousColumn")]
        public bool AmbiguousColumn { get; set; }

        /// <summary>A column compared with a literal of another type family (needs the loaded metadata).</summary>
        [DataMember(Name = "implicitConversion")]
        public bool ImplicitConversion { get; set; }

        /// <summary>alias.column where the alias is a temporary table or table variable of the script whose columns are all known and the column is not one of them (#452).</summary>
        [DataMember(Name = "unknownColumn")]
        public bool UnknownColumn { get; set; }

        public AnalysisSettings Clone() => (AnalysisSettings)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults()
        {
            // SELECT * and NOLOCK are everyday in ad-hoc queries, so they are off until the user asks for them.
            Enabled = true;
            EqualsNull = true;
            JoinWithoutOn = true;
            InsertWithoutColumns = true;
            UnusedVariable = true;
            SelectStar = false;
            LockHint = false;
            UnexpectedEnd = true;
            FunctionOnColumn = true;
            LeadingWildcard = true;
            TopWithoutOrderBy = true;
            AtAtIdentity = true;
            TypeWithoutLength = true;
            NotInSubquery = true;
            OldStyleJoin = true;
            AmbiguousColumn = true;
            ImplicitConversion = true;
            UnknownColumn = true;
        }
    }
}
