using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>
    /// The boundary of the projects (docs/architecture.md): SqlPrompter.Core and SqlPrompter.Metadata must not depend on Visual Studio or SSMS types,
    /// so that they build and are tested everywhere and could be moved out of the SSMS process (#189).
    /// </summary>
    public class DependencyBoundaryTests
    {
        private static readonly string[] Forbidden =
        {
            "Microsoft.VisualStudio", "Microsoft.SqlServer", "EnvDTE", "VSLangProj", "stdole", "PresentationFramework", "PresentationCore", "WindowsBase",
            "System.Windows", "System.Drawing", "System.ComponentModel.Composition", "Microsoft.Data.SqlClient",
        };

        private static string RepositoryRoot([CallerFilePath] string path = null) =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", ".."));

        private static bool IsForbidden(string name) => Forbidden.Any(f => name == f || name.StartsWith(f + ".", System.StringComparison.Ordinal));

        [Theory]
        [InlineData("Microsoft.VisualStudio.Shell.15.0", true)]
        [InlineData("Microsoft.SqlServer.Management.Sdk.Sfc", true)]
        [InlineData("EnvDTE", true)]
        [InlineData("System.Windows.Forms", true)]
        [InlineData("System.Collections", false)]
        [InlineData("netstandard", false)]
        public void TheCheckRecognizesHostAssemblies(string name, bool forbidden)
        {
            Assert.Equal(forbidden, IsForbidden(name));
        }

        [Fact]
        public void CoreReferencesOnlyTheBaseLibrary()
        {
            var references = typeof(SqlPrompter.Core.Sql.Keywords).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

            Assert.DoesNotContain(references, IsForbidden);
            Assert.All(references, r => Assert.True(r == "netstandard" || r == "mscorlib" || r.StartsWith("System", System.StringComparison.Ordinal), "unexpected reference of Core: " + r));
        }

        [Fact]
        public void MetadataReferencesOnlyTheBaseLibraryAndCore()
        {
            var references = typeof(SqlPrompter.Metadata.ConnectionSettings).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

            Assert.DoesNotContain(references, IsForbidden);
            Assert.All(references, r => Assert.True(
                r == "netstandard" || r == "mscorlib" || r == "SqlPrompter.Core" || r.StartsWith("System", System.StringComparison.Ordinal),
                "unexpected reference of Metadata: " + r));
        }

        [Theory]
        [InlineData("src/SqlPrompter.Core/SqlPrompter.Core.csproj")]
        [InlineData("src/SqlPrompter.Metadata/SqlPrompter.Metadata.csproj")]
        public void ProjectFilesHaveNoPackageOrAssemblyReferencesToTheHost(string project)
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), project));

            Assert.Contains("<TargetFramework>netstandard2.0</TargetFramework>", text);
            foreach (var line in text.Split('\n').Where(l => l.Contains("<PackageReference") || l.Contains("<Reference ") || l.Contains("<ProjectReference")))
            {
                Assert.DoesNotContain("SqlPrompter.Ssms", line);
                Assert.DoesNotContain(Forbidden, f => line.Contains("\"" + f));
            }
        }

        [Fact]
        public void CoreProjectDoesNotReferenceMetadata()
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/SqlPrompter.Core/SqlPrompter.Core.csproj"));

            Assert.DoesNotContain("SqlPrompter.Metadata", text);
        }
    }
}
