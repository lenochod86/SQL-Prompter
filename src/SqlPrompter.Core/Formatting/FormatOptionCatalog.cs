using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Formatting
{
    public enum FormatOptionKind
    {
        Bool,
        Number,
        Case,
    }

    /// <summary>One option of <see cref="FormatStyle"/> as the formatting styles window shows it: where it belongs, what it is called and how it is read and written.</summary>
    public sealed class FormatOption
    {
        internal FormatOption(string id, string group, string title, string help, FormatOptionKind kind, Func<FormatStyle, object> get, Action<FormatStyle, object> set, int min = 0, int max = 0, string unit = null)
        {
            Id = id;
            Group = group;
            Title = title;
            Help = help;
            Kind = kind;
            Get = get;
            Set = set;
            Min = min;
            Max = max;
            Unit = unit;
        }

        public string Id { get; }

        public string Group { get; }

        public string Title { get; }

        public string Help { get; }

        public FormatOptionKind Kind { get; }

        /// <summary>The value: a bool, an int or a <see cref="CaseStyle"/>.</summary>
        public Func<FormatStyle, object> Get { get; }

        public Action<FormatStyle, object> Set { get; }

        public int Min { get; }

        public int Max { get; }

        /// <summary>The unit shown after a number ("characters"), or null.</summary>
        public string Unit { get; }
    }

    /// <summary>A group of options with a sample of SQL that shows what the options of the group do.</summary>
    public sealed class FormatOptionGroup
    {
        internal FormatOptionGroup(string name, string description, string sample)
        {
            Name = name;
            Description = description;
            Sample = sample;
        }

        public string Name { get; }

        public string Description { get; }

        /// <summary>Made-up SQL, written untidily, that is formatted with the style being edited.</summary>
        public string Sample { get; }
    }

    /// <summary>
    /// The options of the formatting style in groups, for the window that edits the styles: the groups with their samples, the options with their
    /// controls and a search by name or help text.
    /// </summary>
    public static class FormatOptionCatalog
    {
        public static IReadOnlyList<FormatOptionGroup> Groups { get; } = new[]
        {
            new FormatOptionGroup("Casing", "The case of keywords, built-in functions and data types.",
                "select a.Name,count(*) as Total,getdate() as Today,cast(a.Id as varchar(10)) as Code from dbo.Accounts a where a.Id in (select b.IdAccount from dbo.Balances b) group by a.Name;\r\ndeclare @id int=1,@name nvarchar(50);\r\ncreate table #t (Id int not null,Name varchar(20) null);"),
            new FormatOptionGroup("Indentation", "Tabs or spaces and the width of an indent level.",
                "select a.Name,a.City from dbo.Accounts a inner join dbo.Cities c on c.Id=a.IdCity where a.Active=1;\r\nif @id>0 begin update dbo.Accounts set Active=0 where Id=@id end"),
            new FormatOptionGroup("Lists", "Where the comma of a list goes and how long lists are broken.",
                "select a.Id,a.Name,a.City,a.Phone,a.Email,a.Created from dbo.Accounts a;\r\nselect a.City,count(*) as Total from dbo.Accounts a group by a.City,a.Country order by a.City,a.Country;\r\ninsert into dbo.Accounts (Id,Name,City,Phone,Email,Created,Modified,Source,Notes,Flags,Rating,Region) values (1,'a','b','c','d',getdate(),getdate(),'x','y',0,5,'z');\r\nupdate dbo.Accounts set Name='a',City='b',Phone='c' where Id=1;"),
            new FormatOptionGroup("Conditions", "WHERE and HAVING conditions: one per line and where AND / OR stand.",
                "select a.Id from dbo.Accounts a where a.Active=1 and a.City='Prague' or (a.City='Brno' and a.Rating>3) group by a.Id having count(*)>1 and sum(a.Rating)>10;"),
            new FormatOptionGroup("JOIN", "Where ON of a JOIN goes.",
                "select a.Id,c.Name from dbo.Accounts a inner join dbo.Cities c on c.Id=a.IdCity and c.Active=1 left join dbo.Regions r on r.Id=c.IdRegion where a.Active=1;"),
            new FormatOptionGroup("SELECT", "The line break after DISTINCT and TOP.",
                "select distinct a.City,a.Name,a.Phone,a.Email,a.Street,a.Zip,a.Country from dbo.Accounts a where a.Active=1 order by a.Name;\r\nselect top 10 a.Id,a.Name,a.Phone,a.Email,a.Street,a.Zip,a.Country from dbo.Accounts a where a.Active=1 order by a.Name;"),
            new FormatOptionGroup("Short statements", "Statements, subqueries and CREATE TABLE that are short enough stay on one line.",
                "select Id from dbo.Accounts where Active=1;\r\nselect a.Id,(select max(b.Amount) from dbo.Balances b where b.IdAccount=a.Id) as MaxAmount from dbo.Accounts a;\r\ncreate table dbo.Small (Id int not null,Name varchar(20) null);\r\ncreate table dbo.Bigger (Id int not null,Name varchar(200) null,City varchar(100) null,Created datetime2(0) not null,Modified datetime2(0) null);"),
            new FormatOptionGroup("Blocks and blank lines", "BEGIN ... END blocks and the blank lines between statements.",
                "if @id>0\r\nbegin\r\nselect 1\r\n\r\nupdate dbo.Accounts set Active=1 where Id=@id\r\nend\r\nelse\r\nbegin\r\nselect 2\r\nend\r\n\r\n\r\nselect 3"),
            new FormatOptionGroup("While typing", "Formatting that happens as you type.",
                "select a.Id, a.Name from dbo.Accounts a where a.Id = 1"),
        };

        public static IReadOnlyList<FormatOption> Options { get; } = new[]
        {
            Case("keywordCase", "Casing", "Keywords", "SELECT, FROM, WHERE and the other reserved words.", s => s.KeywordCase, (s, v) => s.KeywordCase = v),
            Case("functionCase", "Casing", "Built-in functions", "COUNT, ISNULL, DATEADD and so on (only when followed by a parenthesis).", s => s.FunctionCase, (s, v) => s.FunctionCase = v),
            Case("dataTypeCase", "Casing", "Data types", "int, varchar and so on where a type is expected (after a variable, in CAST and CONVERT, in table definitions).", s => s.DataTypeCase, (s, v) => s.DataTypeCase = v),
            Bool("useTabs", "Indentation", "Use tabs", "Indentation and alignment are made of tabs where a whole tab fits.", s => s.UseTabs, (s, v) => s.UseTabs = v),
            Number("tabWidth", "Indentation", "Tab width", "The width of one indent level.", 1, 16, "characters", s => s.TabWidth, (s, v) => s.TabWidth = v),
            Bool("commaBeforeItems", "Lists", "Commas before items", "Lists put the comma at the start of the next line (, item) instead of the end of the line.", s => s.CommaBeforeItems, (s, v) => s.CommaBeforeItems = v),
            Bool("groupOrderBySeparateLines", "Lists", "GROUP BY and ORDER BY items on separate lines", "Each item of GROUP BY and ORDER BY gets a line when there are several; off keeps them on the line of the keyword.", s => s.GroupOrderBySeparateLines, (s, v) => s.GroupOrderBySeparateLines = v),
            Bool("onOnNewLine", "JOIN", "ON on a new line", "ON of a JOIN starts its own line, indented under the JOIN, instead of following the joined table.", s => s.OnOnNewLine, (s, v) => s.OnOnNewLine = v),
            Bool("conditionsOnSeparateLines", "Conditions", "Conditions on separate lines", "The conditions of WHERE and HAVING each get a line when there are several.", s => s.ConditionsOnSeparateLines, (s, v) => s.ConditionsOnSeparateLines = v),
            Bool("andOrBeforeConditions", "Conditions", "AND / OR start the line", "Conditions on several lines begin with AND or OR instead of ending with them.", s => s.AndOrBeforeConditions, (s, v) => s.AndOrBeforeConditions = v),
            Bool("newLineAfterDistinctAndTop", "SELECT", "Line break after DISTINCT and TOP", "SELECT TOP 10 / SELECT DISTINCT end the line; the first item starts the next one.", s => s.NewLineAfterDistinctAndTop, (s, v) => s.NewLineAfterDistinctAndTop = v),
            Number("collapseStatementsShorterThan", "Short statements", "Keep statements shorter than", "A SELECT, INSERT, UPDATE or DELETE that fits in fewer characters stays on one line. 0 turns it off.", 0, 1000, "characters", s => s.CollapseStatementsShorterThan, (s, v) => s.CollapseStatementsShorterThan = v),
            Number("collapseSubqueriesShorterThan", "Short statements", "Keep subqueries shorter than", "A subquery or CTE that fits in fewer characters stays on the line of its item. 0 turns it off.", 0, 1000, "characters", s => s.CollapseSubqueriesShorterThan, (s, v) => s.CollapseSubqueriesShorterThan = v),
            Number("collapseDdlShorterThan", "Short statements", "Keep CREATE TABLE shorter than", "A CREATE TABLE that fits in fewer characters stays on one line. 0 turns it off.", 0, 1000, "characters", s => s.CollapseDdlShorterThan, (s, v) => s.CollapseDdlShorterThan = v),
            Bool("indentBlocks", "Blocks and blank lines", "Indent BEGIN ... END", "The statements between BEGIN and END are indented.", s => s.IndentBlocks, (s, v) => s.IndentBlocks = v),
            Bool("keepBlankLines", "Blocks and blank lines", "Keep one blank line", "A blank line between statements stays (as one line); otherwise blank lines are removed.", s => s.KeepBlankLines, (s, v) => s.KeepBlankLines = v),
            Bool("caseKeywordsWhileTyping", "While typing", "Case keywords while typing", "A reserved word you type gets the keyword case as soon as you type a space, parenthesis, comma or semicolon after it.", s => s.CaseKeywordsWhileTyping, (s, v) => s.CaseKeywordsWhileTyping = v),
        };

        public static IReadOnlyList<FormatOption> OptionsOf(string group) => Options.Where(o => o.Group == group).ToList();

        /// <summary>Options whose title, help or group contains every word of the text (case-insensitive); all of them for an empty text.</summary>
        public static IReadOnlyList<FormatOption> Search(string text)
        {
            var words = (text ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return Options.Where(o => words.All(w =>
                o.Title.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 ||
                o.Help.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 ||
                o.Group.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        }

        public static FormatOptionGroup GroupOf(string name) => Groups.First(g => g.Name == name);

        private static FormatOption Bool(string id, string group, string title, string help, Func<FormatStyle, bool> get, Action<FormatStyle, bool> set) =>
            new FormatOption(id, group, title, help, FormatOptionKind.Bool, s => get(s), (s, v) => set(s, (bool)v));

        private static FormatOption Number(string id, string group, string title, string help, int min, int max, string unit, Func<FormatStyle, int> get, Action<FormatStyle, int> set) =>
            new FormatOption(id, group, title, help, FormatOptionKind.Number, s => get(s), (s, v) => set(s, Math.Max(min, Math.Min(max, (int)v))), min, max, unit);

        private static FormatOption Case(string id, string group, string title, string help, Func<FormatStyle, CaseStyle> get, Action<FormatStyle, CaseStyle> set) =>
            new FormatOption(id, group, title, help, FormatOptionKind.Case, s => get(s), (s, v) => set(s, (CaseStyle)v));
    }
}
