using System;
using System.IO;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The snippets that ship with SQL Prompter and their installation (#227).</summary>
    public class BuiltInSnippetTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "sqlprompter-builtin-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void TheSetIsBigAndHasUniquePrefixesAndIds()
        {
            var all = BuiltInSnippets.All;

            Assert.True(all.Count >= 30);
            Assert.Equal(all.Count, all.Select(s => s.Prefix.ToLowerInvariant()).Distinct().Count());
            Assert.Equal(all.Count, all.Select(s => s.Id).Distinct().Count());
            Assert.All(all, s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
        }

        [Fact]
        public void TheIdsAreTheSameEveryTime() => Assert.Equal(BuiltInSnippets.IdOf("top100"), BuiltInSnippets.All.First(s => s.Prefix == "top100").Id);

        [Fact]
        public void EverySnippetExpandsToBalancedSql()
        {
            foreach (var snippet in BuiltInSnippets.All)
            {
                var expanded = SnippetExpander.Expand(snippet, new SnippetContext { SelectedText = "a", NewLine = "\n" });
                var tokens = Lexer.Tokenize(expanded.Text);

                Assert.DoesNotContain(tokens, t => t.IsUnterminated);
                Assert.True(tokens.Count(t => t.Kind == TokenKind.OpenParen) == tokens.Count(t => t.Kind == TokenKind.CloseParen), snippet.Prefix + ": parentheses");
                Assert.DoesNotContain("$", expanded.Text.Replace("$$", string.Empty));
            }
        }

        [Fact]
        public void EveryFieldOfABodyHasADefaultValue()
        {
            foreach (var snippet in BuiltInSnippets.All)
            {
                var expanded = SnippetExpander.Expand(snippet, new SnippetContext());

                Assert.All(expanded.Fields, f => Assert.Contains(f.Name, snippet.Placeholders.Select(p => p.Name)));
            }
        }

        [Fact]
        public void InstallingWritesTheSetOnceAndLeavesEditsAndDeletionsAlone()
        {
            var store = new SnippetStore(folder);
            var installer = new BuiltInInstaller(store);

            Assert.Equal(BuiltInSnippets.All.Count, installer.Install());
            Assert.Equal(BuiltInSnippets.All.Count, store.Load().Snippets.Count);

            var edited = store.Load().Snippets.First(s => s.Prefix == "cnt");
            edited.Body = "SELECT 1";
            store.Save(edited);
            store.Delete(store.Load().Snippets.First(s => s.Prefix == "del").Id);

            Assert.Equal(0, installer.Install());
            Assert.Equal("SELECT 1", store.Load().Snippets.First(s => s.Prefix == "cnt").Body);
            Assert.DoesNotContain(store.Load().Snippets, s => s.Prefix == "del");
        }

        [Fact]
        public void ASnippetOfTheUserWithTheSamePrefixIsKept()
        {
            var store = new SnippetStore(folder);
            store.Save(Snippet.Create("cnt", "mine", "SELECT 42"));

            new BuiltInInstaller(store).Install();

            Assert.Equal("SELECT 42", Assert.Single(store.Load().Snippets, s => s.Prefix == "cnt").Body);
        }

        [Fact]
        public void RestoreWritesTheOriginalTextBack()
        {
            var store = new SnippetStore(folder);
            var installer = new BuiltInInstaller(store);
            installer.Install();
            var cnt = store.Load().Snippets.First(s => s.Prefix == "cnt");
            cnt.Body = "SELECT 1";
            store.Save(cnt);

            Assert.True(installer.Restore(cnt.Id));
            Assert.Contains("COUNT(*)", store.Load().Snippets.First(s => s.Prefix == "cnt").Body);
            Assert.False(installer.Restore("nope"));
        }
    }
}
