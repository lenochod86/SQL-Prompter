using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    public class FormatterTests
    {
        private static string Format(string sql, FormatStyle style = null) => SqlFormatter.Format(sql, style ?? new FormatStyle(), "\n").Text;

        [Fact]
        public void ShortStatementStaysOnOneLineWithCasedKeywords() =>
            Assert.Equal("SELECT a, b FROM t WHERE x = 1", Format("select   a,b   from t\n where x=1"));

        [Fact]
        public void LongSelectIsLaidOutByClauses()
        {
            var result = Format("select c.Id, c.Name, c.Email, o.Total, o.CreatedAt from dbo.Customers c inner join dbo.Orders o on o.CustomerId = c.Id where c.Active = 1 and o.Total > 100 order by o.CreatedAt desc");

            Assert.Equal(
                "SELECT\tc.Id\n" +
                "\t  , c.Name\n" +
                "\t  , c.Email\n" +
                "\t  , o.Total\n" +
                "\t  , o.CreatedAt\n" +
                "FROM\tdbo.Customers c\n" +
                "INNER JOIN\tdbo.Orders o ON o.CustomerId = c.Id\n" +
                "WHERE\tc.Active = 1\n" +
                "\tAND o.Total > 100\n" +
                "ORDER BY\to.CreatedAt DESC",
                result);
        }

        [Fact]
        public void CommasAtTheEndOfTheLinesWhenTheStyleSaysSo()
        {
            var style = new FormatStyle { CommaBeforeItems = false, UseTabs = false, CollapseStatementsShorterThan = 0, AndOrBeforeConditions = false };
            var result = Format("select a, b from t where x = 1 or y = 2", style);

            Assert.Equal("SELECT  a,\n        b\nFROM    t\nWHERE   x = 1 OR\n        y = 2", result);
        }

        [Fact]
        public void BetweenAndIsNotSplit()
        {
            var style = new FormatStyle { CollapseStatementsShorterThan = 0 };
            var result = Format("select a from t where x between 1 and 5 and y = 2", style);

            Assert.Contains("WHERE\tx BETWEEN 1 AND 5\n\tAND y = 2", result);
        }

        [Fact]
        public void LongSubqueryIsExpanded()
        {
            var style = new FormatStyle { CollapseSubqueriesShorterThan = 20, CollapseStatementsShorterThan = 0 };
            var result = Format("select a from t where id in (select UserId from dbo.Orders where Total > 100)", style);

            Assert.Contains("WHERE\tid IN (\n", result);
            Assert.Contains("\n\t\t\tSELECT", result.Replace("SELECT\tUserId", "SELECT"));
            Assert.EndsWith(")", result.TrimEnd());
        }

        [Fact]
        public void CteIsLaidOutWithItsParentheses()
        {
            var style = new FormatStyle { CollapseSubqueriesShorterThan = 10, CollapseStatementsShorterThan = 0 };
            var result = Format("with c as (select a from t where x = 1) select * from c", style);

            Assert.StartsWith("WITH c AS\n(\n", result);
            Assert.Contains("\n)\nSELECT\t*\nFROM\tc", result);
        }

        [Fact]
        public void CasingOfFunctionsAndTypes()
        {
            var result = Format("declare @a int; select cast(x as varchar(10)), isnull(y, 0), date from t;");

            Assert.Contains("DECLARE @a INT;", result);
            Assert.Contains("CAST(x AS VARCHAR(10))", result);
            Assert.Contains("ISNULL(y, 0)", result);
            Assert.Contains(", date FROM", result);
        }

        [Fact]
        public void BlocksAndIfAreIndented()
        {
            var result = Format("if @a = 1\nbegin\nselect 1\nend\nelse\nselect 2");

            Assert.Equal("IF @a = 1\nBEGIN\n\tSELECT 1\nEND\nELSE\n\tSELECT 2", result);
        }

        [Fact]
        public void ElseIfStaysOnOneLine()
        {
            var result = Format("if @a = 1 select 1 else if @a = 2 select 2 else select 3");

            Assert.Contains("ELSE IF @a = 2", result);
        }

        [Theory]
        [InlineData("if @a>1 print 'a' else if @a<0 print 'b' else print 'c'")]
        [InlineData("if @a>1 print 'a' else if @a<0 print 'b'")]
        [InlineData("if @a>1 begin print 'a' end else if @a<0 begin print 'b' end else begin print 'c' end")]
        [InlineData("if @a>1 print 'a'\nelse if @a<0 print 'b'\nelse if @a=0 print 'z'")]
        public void ElseIfHasNoBlankLineAndFormattingTwiceChangesNothing(string sql)
        {
            var once = Format(sql);
            var twice = Format(once);

            Assert.DoesNotContain("\n\n", once);
            Assert.Equal(once, twice);
        }

        [Fact]
        public void ElseIfFollowsTheBodyOfTheIfDirectly()
        {
            Assert.Equal("IF @a > 1\n\tPRINT 'a'\nELSE IF @a < 0\n\tPRINT 'b'\nELSE\n\tPRINT 'c'", Format("if @a>1 print 'a' else if @a<0 print 'b' else print 'c'"));
        }

        private static FormatStyle Cased(CaseStyle keyword, CaseStyle function, CaseStyle dataType) =>
            new FormatStyle { KeywordCase = keyword, FunctionCase = function, DataTypeCase = dataType };

        [Theory]
        [InlineData("set nocount on", "SET NOCOUNT ON")]
        [InlineData("set xact_abort on", "SET XACT_ABORT ON")]
        [InlineData("set statistics io on", "SET STATISTICS IO ON")]
        [InlineData("select * from t with (nolock)", "SELECT * FROM t WITH (NOLOCK)")]
        [InlineData("select * from t with (nolock, index (ix))", "SELECT * FROM t WITH (NOLOCK, INDEX (ix))")]
        [InlineData("select * from t option (recompile)", "SELECT * FROM t OPTION (RECOMPILE)")]
        [InlineData("select * from t option (maxrecursion 10, maxdop 2)", "SELECT * FROM t OPTION (MAXRECURSION 10, MAXDOP 2)")]
        [InlineData("select top 1 with ties a from b order by a", "SELECT TOP 1 WITH TIES a FROM b ORDER BY a")]
        [InlineData("select * from openjson(@j) with (a int)", "SELECT * FROM OPENJSON(@j) WITH (a INT)")]
        [InlineData("select @@rowcount, @@error, @@trancount", "SELECT @@ROWCOUNT, @@ERROR, @@TRANCOUNT")]
        [InlineData("select * from string_split(@s, ',')", "SELECT * FROM STRING_SPLIT(@s, ',')")]
        public void ContextKeywordsFollowTheUpperCaseStyle(string sql, string expected)
        {
            Assert.Equal(expected, Format(sql, Cased(CaseStyle.Upper, CaseStyle.Upper, CaseStyle.Upper)));
        }

        [Theory]
        [InlineData("SET NOCOUNT ON", "set nocount on")]
        [InlineData("SELECT * FROM t WITH (NOLOCK)", "select * from t with (nolock)")]
        [InlineData("SELECT * FROM t OPTION (RECOMPILE)", "select * from t option (recompile)")]
        [InlineData("SELECT TOP 1 WITH TIES a FROM b ORDER BY a", "select top 1 with ties a from b order by a")]
        [InlineData("SELECT * FROM OPENJSON(@j) WITH (a INT)", "select * from openjson(@j) with (a int)")]
        [InlineData("SELECT @@ROWCOUNT, @@ERROR", "select @@rowcount, @@error")]
        public void ContextKeywordsFollowTheLowerCaseStyle(string sql, string expected)
        {
            Assert.Equal(expected, Format(sql, Cased(CaseStyle.Lower, CaseStyle.Lower, CaseStyle.Lower)));
        }

        [Fact]
        public void OutputAndReadonlyOfAProcedureParameterFollowTheKeywordCase()
        {
            var result = Format("create proc p @x int output, @t dbo.T readonly as select 1", Cased(CaseStyle.Upper, CaseStyle.Upper, CaseStyle.Upper));

            Assert.Contains("@x INT OUTPUT", result);
            Assert.Contains("READONLY", result);
        }

        [Fact]
        public void AVariableOfTheUserStaysAsWritten()
        {
            Assert.Equal("SELECT @rowcount, @Error", Format("select @rowcount, @Error", Cased(CaseStyle.Upper, CaseStyle.Upper, CaseStyle.Upper)));
        }

        [Fact]
        public void GoStaysAlone()
        {
            var result = Format("select 1\ngo\n\n\nselect 2\n");

            Assert.Equal("SELECT 1\nGO\nSELECT 2", result);
        }

        [Fact]
        public void BlankLineBetweenStatementsIsKeptOnce()
        {
            Assert.Equal("SELECT 1\n\nSELECT 2", Format("select 1\n\n\n\nselect 2"));
            Assert.Equal("SELECT 1\nSELECT 2", Format("select 1\nselect 2"));
        }

        [Fact]
        public void CommentsAreKept()
        {
            var result = Format("-- header\nselect a, /* inline */ b from t -- trailing\nwhere x = 1");

            Assert.Contains("-- header\n", result);
            Assert.Contains("/* inline */", result);
            Assert.Contains("-- trailing", result);
        }

        [Fact]
        public void CreateTableColumnsAreListed()
        {
            var style = new FormatStyle { CollapseDdlShorterThan = 20 };
            var result = Format("create table dbo.T (Id int not null, Name varchar(50), constraint PK primary key (Id))", style);

            Assert.Equal("CREATE TABLE dbo.T\n(\n\t  Id   INT NOT NULL\n\t, Name VARCHAR(50)\n\t, CONSTRAINT PK PRIMARY KEY (Id)\n)", result);
        }

        [Theory]
        [InlineData("select a<=b, c<>d, e>=-1, f!=g from t where x=-1")]
        [InlineData("select 'a''b', N'x', [a b], \"q\", @v, @@rowcount, #tmp.x, 1.5e3, 0xFF from dbo.[t]")]
        [InlineData("update t set a = a + 1, b = null where id = 3")]
        [InlineData("insert into dbo.T (a, b) values (1, 2), (3, 4)")]
        [InlineData("delete from t where id in (1,2,3)")]
        [InlineData("select top (10) percent with ties a from t order by a")]
        [InlineData("select a into #t from b; select * from #t")]
        [InlineData("select case when a = 1 then 'x' when a = 2 then 'y' else 'z' end as c from t")]
        [InlineData("select a from t union all select b from u except select c from v")]
        [InlineData("select count(*), sum(a) over (partition by b order by c) from t group by b having count(*) > 1")]
        [InlineData("exec dbo.p @a = 1, @b = 'x'; print 'done'")]
        [InlineData("merge t using s on t.id = s.id when matched then update set a = s.a when not matched then insert (a) values (s.a);")]
        public void FormattingNeverChangesTheScript(string sql)
        {
            var style = new FormatStyle { CollapseStatementsShorterThan = 0, CollapseSubqueriesShorterThan = 0 };
            var result = SqlFormatter.Format(sql, style, "\n");

            var before = SqlPrompter.Core.Lexing.Lexer.Tokenize(sql);
            var after = SqlPrompter.Core.Lexing.Lexer.Tokenize(result.Text);
            Assert.Equal(before.Count, after.Count);
            for (var i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].Text, after[i].Text, ignoreCase: before[i].Kind == SqlPrompter.Core.Lexing.TokenKind.Word || before[i].Text.StartsWith("@@"));
            }

            Assert.Equal(0, result.Kept);
        }

        [Fact]
        public void FormattingTwiceGivesTheSameText()
        {
            var style = new FormatStyle { CollapseStatementsShorterThan = 0 };
            var once = Format("select a, b, c from t inner join u on u.id = t.id where a = 1 and b = 2 order by a", style);

            Assert.Equal(once, Format(once, style));
        }
    }
}
