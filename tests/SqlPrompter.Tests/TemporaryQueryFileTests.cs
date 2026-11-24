using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    public class TemporaryQueryFileTests
    {
        private const string Temp = @"C:\Users\x\AppData\Local\Temp\";

        [Theory]
        [InlineData(@"C:\Users\x\AppData\Local\Temp\SQLQuery8.sql", true)]
        [InlineData(@"c:\users\x\appdata\local\temp\sqlquery12.sql", true)]
        [InlineData(@"C:\Users\x\AppData\Local\Temp\notes.sql", false)]
        [InlineData(@"C:\Work\SQLQuery8.sql", false)]
        [InlineData(@"C:\Users\x\AppData\Local\Temp\sub\SQLQuery8.sql", false)]
        [InlineData(@"C:\Users\x\AppData\Local\Temp\~vsyplna1we.sql", true)]
        [InlineData(@"D:\elsewhere\~vse240isf3.sql", true)]
        [InlineData(@"C:\Work\~vsnotes.txt", false)]
        [InlineData("SQLQuery8.sql", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyANewQueryWindowInTheTempFolderIsTemporary(string path, bool expected)
        {
            Assert.Equal(expected, TemporaryQueryFile.Is(path, Temp));
        }

        [Fact]
        public void WithoutATempFolderNothingIsTemporary()
        {
            Assert.False(TemporaryQueryFile.Is(@"C:\Temp\SQLQuery1.sql", null));
        }
    }
}
