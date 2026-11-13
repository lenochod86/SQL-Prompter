using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    public class SnippetHighlighterTests
    {
        private static string Kinds(string text) => string.Concat(SnippetHighlighter.Classify(text));

        private static int[] Classes(string text) => SnippetHighlighter.Classify(text);

        [Fact]
        public void OperatorsAndPunctuationAreOperators()
        {
            var kinds = Classes("SELECT a.b, (1 + 2) * c;");

            Assert.Equal(SnippetHighlighter.Operator, kinds[8]); // .
            Assert.Equal(SnippetHighlighter.Operator, kinds[10]); // ,
            Assert.Equal(SnippetHighlighter.Operator, kinds[12]); // (
            Assert.Equal(SnippetHighlighter.Operator, kinds[15]); // +
            Assert.Equal(SnippetHighlighter.Operator, kinds[18]); // )
            Assert.Equal(SnippetHighlighter.Operator, kinds[20]); // *
            Assert.Equal(SnippetHighlighter.Operator, kinds[23]); // ;
            Assert.Equal(SnippetHighlighter.Plain, kinds[7]);
        }

        [Fact]
        public void SystemVariablesAreFunctionsAndUserVariablesAreVariables()
        {
            Assert.Equal(SnippetHighlighter.Function, Classes("SELECT @@ROWCOUNT")[10]);
            Assert.Equal(SnippetHighlighter.Variable, Classes("SELECT @rows")[10]);
        }

        [Fact]
        public void SystemObjectsGetTheirOwnClasses()
        {
            var table = Classes("SELECT * FROM sys.objects o");
            Assert.Equal(SnippetHighlighter.SystemTable, table[14]); // sys
            Assert.Equal(SnippetHighlighter.Operator, table[17]); // .
            Assert.Equal(SnippetHighlighter.SystemTable, table[18]); // objects
            Assert.Equal(SnippetHighlighter.Plain, table[26]); // o
            Assert.Equal(SnippetHighlighter.SystemTable, Classes("SELECT * FROM sysobjects")[14]);
            Assert.Equal(SnippetHighlighter.Plain, Classes("SELECT * FROM dbo.objects")[18]);

            Assert.Equal(SnippetHighlighter.SystemProcedure, Classes("EXEC sp_help")[5]);
            Assert.Equal(SnippetHighlighter.SystemProcedure, Classes("EXEC xp_cmdshell 'dir'")[5]);
            Assert.Equal(SnippetHighlighter.Plain, Classes("EXEC dbo.sp_mine")[9]);
            Assert.Equal(SnippetHighlighter.Plain, Classes("EXEC Crm.p_Contract")[10]);
        }

        [Fact]
        public void KeywordsStringsCommentsAndVariablesAreTold()
        {
            Assert.Equal("111111022203333", Kinds("SELECT 'a' -- c"));
            Assert.Equal("111111044", Kinds("SELECT @v"));
        }
        [Fact]
        public void FieldsWinOverSql()
        {
            var kinds = SnippetHighlighter.Classify("SELECT $CURSOR$, $DATE(yyyy-MM-dd)$ FROM $table$");

            Assert.Equal(SnippetHighlighter.Keyword, kinds[0]);
            Assert.Equal(SnippetHighlighter.Field, kinds[7]);
            Assert.Equal(SnippetHighlighter.Field, kinds[14]);
            Assert.Equal(SnippetHighlighter.Operator, kinds[15]);
            Assert.Equal(SnippetHighlighter.Field, kinds[17]);
            Assert.Equal(SnippetHighlighter.Keyword, kinds["SELECT $CURSOR$, $DATE(yyyy-MM-dd)$ FROM $table$".IndexOf("FROM")]);
        }

        [Fact]
        public void EmptyAndNullAreFine()
        {
            Assert.Empty(SnippetHighlighter.Classify(null));
            Assert.Empty(SnippetHighlighter.Classify(string.Empty));
        }
    }
}
