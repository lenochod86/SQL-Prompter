using System;
using System.Runtime.Serialization;

namespace SqlPrompter.Core.Settings
{
    /// <summary>When the column list of an INSERT is offered.</summary>
    public enum InsertColumnsMode
    {
        /// <summary>Choosing the table after INSERT INTO inserts it together with its column list.</summary>
        Automatic,

        /// <summary>The table is inserted alone; a second item inserts it with the column list.</summary>
        Item,

        /// <summary>The column list is offered after typing the opening parenthesis behind the table name.</summary>
        AfterParenthesis,
    }

    /// <summary>How a list of columns or values is laid out.</summary>
    public enum ListLayout
    {
        OneLine,
        EachOnItsOwnLine,

        /// <summary>As many items on a line as fit the width of the editor, more lines when needed.</summary>
        FitWidth,
    }

    /// <summary>What follows the column list of an INSERT.</summary>
    public enum InsertValuesMode
    {
        /// <summary>A new line after the closing parenthesis, nothing else.</summary>
        None,

        /// <summary><c>VALUES</c> with one empty value for each column.</summary>
        Values,
    }

    /// <summary>What the INSERT statements and EXEC calls inserted by the list look like.</summary>
    [DataContract]
    public sealed class InsertSettings
    {
        public InsertSettings()
        {
            SetDefaults();
        }

        public InsertColumnsMode ColumnsMode { get; set; }

        public ListLayout Layout { get; set; }

        public InsertValuesMode ValuesMode { get; set; }

        [DataMember(Name = "columnsMode")]
        private string ColumnsModeText
        {
            get => ColumnsMode.ToString();
            set => ColumnsMode = Enum.TryParse(value, true, out InsertColumnsMode parsed) ? parsed : InsertColumnsMode.Automatic;
        }

        [DataMember(Name = "layout")]
        private string LayoutText
        {
            get => Layout.ToString();
            set => Layout = Enum.TryParse(value, true, out ListLayout parsed) ? parsed : ListLayout.EachOnItsOwnLine;
        }

        [DataMember(Name = "valuesMode")]
        private string ValuesModeText
        {
            get => ValuesMode.ToString();
            set => ValuesMode = Enum.TryParse(value, true, out InsertValuesMode parsed) ? parsed : InsertValuesMode.None;
        }

        public InsertSettings Clone() => (InsertSettings)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults()
        {
            ColumnsMode = InsertColumnsMode.Automatic;
            Layout = ListLayout.EachOnItsOwnLine;
            ValuesMode = InsertValuesMode.None;
        }
    }
}
