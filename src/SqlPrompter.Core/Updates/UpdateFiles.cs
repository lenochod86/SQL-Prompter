using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SqlPrompter.Core.Updates
{
    /// <summary>Where the releases are.</summary>
    public static class Repository
    {
        public const string Owner = "lenochod86";
        public const string Name = "SQL-Prompter";

        public const string LatestReleaseApi = "https://api.github.com/repos/" + Owner + "/" + Name + "/releases/latest";

        public const string ReleasesDownloadPath = "/" + Owner + "/" + Name + "/releases/download/";
    }

    /// <summary>The downloaded VSIX files under %LOCALAPPDATA%\SqlPrompter\updates: their names, which of them are leftovers and the checksum check.</summary>
    public static class UpdateFiles
    {
        private static readonly Regex Name = new Regex(@"^SqlPrompter-(?<v>\d+(\.\d+){1,3})\.vsix(?<p>\.part)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The name of the downloaded VSIX of a version; one name per version, so a new download replaces the old copy.</summary>
        public static string VsixName(Version version) => "SqlPrompter-" + version + ".vsix";

        /// <summary>The name a download has until it is complete and verified.</summary>
        public static string PartName(Version version) => VsixName(version) + ".part";

        /// <summary>
        /// Which of the files in the updates folder to delete: every partial download, every VSIX of the running or an older version (the update happened or is moot)
        /// and, when <paramref name="keep"/> is given, every VSIX but that one. A name that is not ours is never returned.
        /// </summary>
        public static IReadOnlyList<string> Leftovers(IEnumerable<string> fileNames, Version running, Version keep = null)
        {
            var result = new List<string>();
            foreach (var fileName in fileNames)
            {
                var match = Name.Match(fileName ?? string.Empty);
                if (!match.Success || !Version.TryParse(match.Groups["v"].Value, out var version))
                {
                    continue;
                }

                var partial = match.Groups["p"].Success;
                if (partial || version <= running || (keep != null && version != keep))
                {
                    result.Add(fileName);
                }
            }

            return result;
        }

        /// <summary>
        /// Reads the hash from the text of a checksum file (the format of <c>sha256sum</c>: 64 hex digits, then the file name). Returns null when it holds no hash
        /// for <see cref="ReleaseInfo.VsixAssetName"/>.
        /// </summary>
        public static string ParseChecksum(string text)
        {
            foreach (var line in (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(line.Trim(), @"^(?<h>[0-9a-fA-F]{64})\s+\*?(?<n>.+)$");
                if (match.Success && string.Equals(match.Groups["n"].Value.Trim(), ReleaseInfo.VsixAssetName, StringComparison.Ordinal))
                {
                    return match.Groups["h"].Value.ToLowerInvariant();
                }
            }

            return null;
        }

        /// <summary>True when the SHA-256 of the data equals the hash from the checksum file.</summary>
        public static bool Matches(byte[] data, string expectedHash)
        {
            if (data == null || string.IsNullOrEmpty(expectedHash))
            {
                return false;
            }

            using (var sha = SHA256.Create())
            {
                var actual = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty);
                return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Downloads are accepted only over HTTPS from the releases of this repository (the host github.com and the path of its releases) or,
        /// for a test, from the loopback host that was named explicitly in <paramref name="testHost"/>.
        /// </summary>
        public static bool IsTrustedDownload(string url, string testHost = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(testHost) && string.Equals(uri.Host, testHost, StringComparison.OrdinalIgnoreCase))
            {
                return uri.IsLoopback;
            }

            return uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith(Repository.ReleasesDownloadPath, StringComparison.Ordinal);
        }
    }
}
