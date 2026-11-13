using SqlPrompter.Core.Navigation;
using Xunit;

namespace SqlPrompter.Tests
{
    public class DefinitionTabNameTests
    {
        [Fact]
        public void FirstTabOfAnObjectGetsNumberOne()
        {
            Assert.Equal("Crm.p_Nsure_Contract1.sql", DefinitionTabName.Next("Crm", "p_Nsure_Contract", new string[0]));
            Assert.Equal("Crm.p_Nsure_Contract1.sql", DefinitionTabName.Next("Crm", "p_Nsure_Contract", null));
        }

        [Fact]
        public void TheNumberGrowsWhileTabsAreOpen()
        {
            var open = new[] { "Crm.p_Nsure_Contract1.sql" };
            Assert.Equal("Crm.p_Nsure_Contract2.sql", DefinitionTabName.Next("Crm", "p_Nsure_Contract", open));
            Assert.Equal("Crm.p_Nsure_Contract3.sql", DefinitionTabName.Next("Crm", "p_Nsure_Contract", new[] { "Crm.p_Nsure_Contract1.sql", "Crm.p_Nsure_Contract2.sql" }));
        }

        [Fact]
        public void AFreedNumberIsUsedAgain()
        {
            Assert.Equal("Crm.p_Nsure_Contract1.sql", DefinitionTabName.Next("Crm", "p_Nsure_Contract", new[] { "Crm.p_Nsure_Contract2.sql" }));
            Assert.Equal("Crm.p_Nsure_Contract2.sql", DefinitionTabName.Next("Crm", "p_Nsure_Contract", new[] { "Crm.p_Nsure_Contract1.sql", "Crm.p_Nsure_Contract3.sql" }));
        }

        [Fact]
        public void ARegularFileWithTheNameOfTheObjectNeverCollides()
        {
            Assert.Equal("Crm.p_Nsure_Contract1.sql", DefinitionTabName.Next("Crm", "p_Nsure_Contract", new[] { "Crm.p_Nsure_Contract.sql" }));
        }

        [Fact]
        public void OtherObjectsAndCaseAreHandled()
        {
            Assert.Equal("Crm.p_Other1.sql", DefinitionTabName.Next("Crm", "p_Other", new[] { "Crm.p_Nsure_Contract1.sql", "Crm.p_Other_x1.sql" }));
            Assert.Equal("Crm.p_Nsure_Contract2.sql", DefinitionTabName.Next("Crm", "p_Nsure_Contract", new[] { "crm.P_NSURE_CONTRACT1.SQL" }));
        }

        [Fact]
        public void BracketsAndCharactersNotAllowedInFileNamesAreHandled()
        {
            Assert.Equal("dbo.My_Proc1.sql", DefinitionTabName.Next("[dbo]", "[My_Proc]", null));
            Assert.Equal("dbo.a_b_c_d1.sql", DefinitionTabName.Next("dbo", "a:b/c?d", null));
            Assert.Equal("dbo.a b1.sql", DefinitionTabName.Next("dbo", "[a b]", null));
        }

        [Fact]
        public void WithoutASchemaOnlyTheObjectIsUsed()
        {
            Assert.Equal("p_Orders1.sql", DefinitionTabName.Next(null, "p_Orders", null));
            Assert.Equal("p_Orders2.sql", DefinitionTabName.Next(string.Empty, "p_Orders", new[] { "p_Orders1.sql" }));
        }

        [Fact]
        public void TheSaveAsFileNameIsTheTabNameWithTheExtension()
        {
            Assert.Equal("Crm.p_Nsure_Contract1.sql", DefinitionTabName.FileNameOf("Crm.p_Nsure_Contract1.sql"));
            Assert.Equal("Orders check.sql", DefinitionTabName.FileNameOf("Orders check"));
            Assert.Equal("a_b.sql", DefinitionTabName.FileNameOf("a:b"));
            Assert.Equal(string.Empty, DefinitionTabName.FileNameOf("  "));
            Assert.Equal(string.Empty, DefinitionTabName.FileNameOf(null));
        }
    }
}
