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
        }
    }
}
