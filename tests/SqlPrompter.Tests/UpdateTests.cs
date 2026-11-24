using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Updates;
using Xunit;

namespace SqlPrompter.Tests
{
    public class UpdateTests
    {
        private static readonly DateTime Monday = new DateTime(2026, 11, 16, 9, 30, 0);
        private static readonly Version Running = new Version(0, 200, 54);

        private static ReleaseInfo Release(string version) => new ReleaseInfo(new Version(version), "https://github.com/x/y/releases/tag/v" + version, "https://github.com/x/y/a.vsix", "https://github.com/x/y/a.sha256");

        private const string ReleaseJson = @"{
            ""tag_name"": ""v0.200.55"", ""html_url"": ""https://github.com/lenochod86/SQL-Prompter/releases/tag/v0.200.55"", ""draft"": false, ""prerelease"": false,
            ""assets"": [
                { ""name"": ""SqlPrompter.Ssms.vsix"", ""browser_download_url"": ""https://github.com/lenochod86/SQL-Prompter/releases/download/v0.200.55/SqlPrompter.Ssms.vsix"" },
                { ""name"": ""SqlPrompter.Ssms.vsix.sha256"", ""browser_download_url"": ""https://github.com/lenochod86/SQL-Prompter/releases/download/v0.200.55/SqlPrompter.Ssms.vsix.sha256"" }
            ] }";

        [Fact]
        public void ParsesTheLatestRelease()
        {
            var release = ReleaseInfo.Parse(ReleaseJson);

            Assert.NotNull(release);
            Assert.Equal(new Version(0, 200, 55), release.Version);
            Assert.EndsWith("/v0.200.55/SqlPrompter.Ssms.vsix", release.VsixUrl);
            Assert.EndsWith("/v0.200.55/SqlPrompter.Ssms.vsix.sha256", release.ChecksumUrl);
            Assert.EndsWith("/tag/v0.200.55", release.PageUrl);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("[1,2]")]
        [InlineData(@"{ ""message"": ""Not Found"" }")]
        public void AnythingThatIsNotAReleaseGivesNull(string json)
        {
            Assert.Null(ReleaseInfo.Parse(json));
        }

        [Fact]
        public void DraftsPrereleasesAndOddTagsAreIgnored()
        {
            Assert.Null(ReleaseInfo.Parse(ReleaseJson.Replace(@"""draft"": false", @"""draft"": true")));
            Assert.Null(ReleaseInfo.Parse(ReleaseJson.Replace(@"""prerelease"": false", @"""prerelease"": true")));
            Assert.Null(ReleaseInfo.Parse(ReleaseJson.Replace("v0.200.55\", \"html", "nightly\", \"html")));
            Assert.Null(ReleaseInfo.Parse(ReleaseJson.Replace("\"v0.200.55\", \"html", "\"0.200.55\", \"html")));
        }

        [Fact]
        public void AReleaseWithoutTheVsixOrTheChecksumIsIgnored()
        {
            Assert.Null(ReleaseInfo.Parse(ReleaseJson.Replace("SqlPrompter.Ssms.vsix.sha256", "other.txt")));
            Assert.Null(ReleaseInfo.Parse(ReleaseJson.Replace("\"SqlPrompter.Ssms.vsix\"", "\"other.vsix\"")));
        }

        [Fact]
        public void ChecksOnceADay()
        {
            var settings = new UpdateSettings();
            Assert.True(UpdateDecision.ShouldCheck(settings, Monday));

            settings = UpdateDecision.Checked(settings, Monday);
            Assert.False(UpdateDecision.ShouldCheck(settings, Monday.AddHours(8)));
            Assert.True(UpdateDecision.ShouldCheck(settings, Monday.AddDays(1)));
        }

        [Fact]
        public void DoesNotCheckWhenTurnedOff()
        {
            var settings = new UpdateSettings { CheckForUpdates = false };

            Assert.False(UpdateDecision.ShouldCheck(settings, Monday));
            Assert.False(UpdateDecision.ShouldAnnounce(settings, Running, Release("0.200.55"), Monday));
        }

        [Fact]
        public void ABrokenLastCheckDayMeansCheckNow()
        {
            Assert.True(UpdateDecision.ShouldCheck(new UpdateSettings { LastCheckDay = "yesterday" }, Monday));
        }

        [Fact]
        public void AnnouncesOnlyANewerVersion()
        {
            var settings = new UpdateSettings();

            Assert.True(UpdateDecision.ShouldAnnounce(settings, Running, Release("0.200.55"), Monday));
            Assert.True(UpdateDecision.ShouldAnnounce(settings, Running, Release("0.201.0"), Monday));
            Assert.False(UpdateDecision.ShouldAnnounce(settings, Running, Release("0.200.54"), Monday));
            Assert.False(UpdateDecision.ShouldAnnounce(settings, Running, Release("0.200.9"), Monday));
            Assert.False(UpdateDecision.ShouldAnnounce(settings, Running, null, Monday));
        }

        [Fact]
        public void VersionsAreComparedAsNumbersNotAsText()
        {
            Assert.True(UpdateDecision.ShouldAnnounce(new UpdateSettings(), new Version(0, 200, 9), Release("0.200.10"), Monday));
        }

        [Fact]
        public void RemindMeTomorrowHidesTheWindowUntilTheNextDay()
        {
            var settings = UpdateDecision.RemindTomorrow(new UpdateSettings(), Monday);
            var release = Release("0.200.55");

            Assert.False(UpdateDecision.ShouldAnnounce(settings, Running, release, Monday));
            Assert.False(UpdateDecision.ShouldAnnounce(settings, Running, release, Monday.AddHours(10)));
            Assert.False(UpdateDecision.ShouldAnnounce(settings, Running, release, new DateTime(2026, 11, 16, 23, 59, 0)));
            Assert.True(UpdateDecision.ShouldAnnounce(settings, Running, release, new DateTime(2026, 11, 17, 0, 1, 0)));
            Assert.True(UpdateDecision.ShouldAnnounce(settings, Running, release, Monday.AddDays(5)));
        }

        [Fact]
        public void RemindMeTomorrowWorksAcrossTheEndOfAMonthAndYear()
        {
            var settings = UpdateDecision.RemindTomorrow(new UpdateSettings(), new DateTime(2026, 12, 31, 22, 0, 0));

            Assert.Equal("2027-01-01", settings.RemindOn);
        }

        [Fact]
        public void SkipHidesThatVersionButAnnouncesANewerOne()
        {
            var settings = UpdateDecision.Skip(new UpdateSettings(), Release("0.200.55"));

            Assert.False(UpdateDecision.ShouldAnnounce(settings, Running, Release("0.200.55"), Monday.AddDays(30)));
            Assert.True(UpdateDecision.ShouldAnnounce(settings, Running, Release("0.200.56"), Monday));
            Assert.True(UpdateDecision.ShouldAnnounce(settings, Running, Release("0.201.0"), Monday));
        }

        [Fact]
        public void TheDecisionsDoNotChangeTheOriginalSettings()
        {
            var settings = new UpdateSettings();

            UpdateDecision.RemindTomorrow(settings, Monday);
            UpdateDecision.Skip(settings, Release("0.200.55"));
            UpdateDecision.Checked(settings, Monday);

            Assert.Equal(string.Empty, settings.RemindOn);
            Assert.Equal(string.Empty, settings.SkippedVersion);
            Assert.Equal(string.Empty, settings.LastCheckDay);
        }

        [Fact]
        public void TheUpdateSettingsSurviveTheSettingsFile()
        {
            var settings = new PrompterSettings();
            settings.Updates = UpdateDecision.Skip(UpdateDecision.RemindTomorrow(UpdateDecision.Checked(settings.Updates, Monday), Monday), Release("0.200.55"));
            settings.Updates.CheckForUpdates = false;

            var back = SettingsJson.Deserialize(SettingsJson.Serialize(settings)).Updates;

            Assert.False(back.CheckForUpdates);
            Assert.Equal("0.200.55", back.SkippedVersion);
            Assert.Equal("2026-11-16", back.LastCheckDay);
        }

        [Fact]
        public void AFileWithoutTheUpdateGroupGetsTheDefaults()
        {
            var back = SettingsJson.Deserialize("{ \"version\": 1 }").Updates;

            Assert.True(back.CheckForUpdates);
            Assert.Equal(string.Empty, back.SkippedVersion);
        }

        [Fact]
        public void LeftoversAreOlderVersionsAndPartialDownloads()
        {
            var names = new[]
            {
                "SqlPrompter-0.200.50.vsix",
                "SqlPrompter-0.200.54.vsix",
                "SqlPrompter-0.200.55.vsix",
                "SqlPrompter-0.200.56.vsix.part",
                "SqlPrompter-0.200.9.vsix",
                "notes.txt",
                "other.vsix",
            };

            var leftovers = UpdateFiles.Leftovers(names, Running);

            Assert.Equal(new[] { "SqlPrompter-0.200.50.vsix", "SqlPrompter-0.200.54.vsix", "SqlPrompter-0.200.56.vsix.part", "SqlPrompter-0.200.9.vsix" }, leftovers.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void AfterADownloadOnlyThatVersionIsKept()
        {
            var names = new[] { "SqlPrompter-0.200.55.vsix", "SqlPrompter-0.200.56.vsix", "SqlPrompter-0.200.57.vsix", "readme.md" };

            var leftovers = UpdateFiles.Leftovers(names, Running, new Version(0, 200, 56));

            Assert.Equal(new[] { "SqlPrompter-0.200.55.vsix", "SqlPrompter-0.200.57.vsix" }, leftovers.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void ANewDownloadReplacesTheCopyOfTheSameVersion()
        {
            Assert.Equal(UpdateFiles.VsixName(new Version(0, 200, 55)), UpdateFiles.VsixName(new Version("0.200.55")));
            Assert.Equal("SqlPrompter-0.200.55.vsix.part", UpdateFiles.PartName(new Version(0, 200, 55)));
        }

        [Fact]
        public void ReadsTheChecksumFileOfTheRelease()
        {
            var hash = new string('a', 32) + new string('B', 32);

            Assert.Equal(hash.ToLowerInvariant(), UpdateFiles.ParseChecksum(hash + " *SqlPrompter.Ssms.vsix\n"));
            Assert.Equal(hash.ToLowerInvariant(), UpdateFiles.ParseChecksum(hash + "  SqlPrompter.Ssms.vsix\r\n"));
            Assert.Null(UpdateFiles.ParseChecksum(hash + " *Other.vsix"));
            Assert.Null(UpdateFiles.ParseChecksum("deadbeef *SqlPrompter.Ssms.vsix"));
            Assert.Null(UpdateFiles.ParseChecksum(""));
            Assert.Null(UpdateFiles.ParseChecksum(null));
        }

        [Fact]
        public void TheChecksumDecidesWhetherTheDownloadIsGenuine()
        {
            var data = Encoding.UTF8.GetBytes("a vsix");
            string hash;
            using (var sha = SHA256.Create())
            {
                hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
            }

            Assert.True(UpdateFiles.Matches(data, hash));
            Assert.True(UpdateFiles.Matches(data, hash.ToUpperInvariant()));
            Assert.False(UpdateFiles.Matches(Encoding.UTF8.GetBytes("another vsix"), hash));
            Assert.False(UpdateFiles.Matches(data, null));
            Assert.False(UpdateFiles.Matches(null, hash));
        }

        [Theory]
        [InlineData("https://github.com/lenochod86/SQL-Prompter/releases/download/v0.200.55/SqlPrompter.Ssms.vsix", true)]
        [InlineData("http://github.com/lenochod86/SQL-Prompter/releases/download/v0.200.55/SqlPrompter.Ssms.vsix", false)]
        [InlineData("https://github.com/someone-else/SQL-Prompter/releases/download/v1.0.0/SqlPrompter.Ssms.vsix", false)]
        [InlineData("https://github.com.evil.example/lenochod86/SQL-Prompter/releases/download/v1/a.vsix", false)]
        [InlineData("https://evil.example/lenochod86/SQL-Prompter/releases/download/v1/a.vsix", false)]
        [InlineData("https://github.com/lenochod86/SQL-Prompter/archive/main.zip", false)]
        [InlineData("not a url", false)]
        [InlineData("", false)]
        public void OnlyTheReleasesOfTheRepositoryAreTrusted(string url, bool trusted)
        {
            Assert.Equal(trusted, UpdateFiles.IsTrustedDownload(url));
        }

        [Fact]
        public void ATestHostIsTrustedOnlyWhenItIsLoopback()
        {
            Assert.True(UpdateFiles.IsTrustedDownload("http://localhost:8765/a.vsix", "localhost"));
            Assert.True(UpdateFiles.IsTrustedDownload("http://127.0.0.1:8765/a.vsix", "127.0.0.1"));
            Assert.False(UpdateFiles.IsTrustedDownload("http://evil.example/a.vsix", "evil.example"));
            Assert.False(UpdateFiles.IsTrustedDownload("http://localhost:8765/a.vsix"));
        }
    }
}
