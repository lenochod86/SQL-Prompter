using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlPrompter.Core.Updates
{
    /// <summary>A published release of SQL Prompter: its version and where its VSIX and checksum are.</summary>
    public sealed class ReleaseInfo
    {
        public const string VsixAssetName = "SqlPrompter.Ssms.vsix";
        public const string ChecksumAssetName = VsixAssetName + ".sha256";

        public ReleaseInfo(Version version, string pageUrl, string vsixUrl, string checksumUrl)
        {
            Version = version;
            PageUrl = pageUrl;
            VsixUrl = vsixUrl;
            ChecksumUrl = checksumUrl;
        }

        public Version Version { get; }

        /// <summary>The page of the release on GitHub (the fallback when the VSIX cannot be installed from here).</summary>
        public string PageUrl { get; }

        public string VsixUrl { get; }

        public string ChecksumUrl { get; }

        /// <summary>
        /// Reads the answer of the GitHub API for a release (<c>/repos/{owner}/{repo}/releases/latest</c>). Returns null for anything that is not a usable
        /// release: not JSON, a draft or pre-release, a tag that is not <c>vX.Y.Z</c>, or a release without the VSIX and its checksum.
        /// </summary>
        public static ReleaseInfo Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            GitHubRelease release;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    release = new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(stream) as GitHubRelease;
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is ArgumentException || ex is InvalidCastException)
            {
                return null;
            }

            if (release == null || release.Draft || release.Prerelease || string.IsNullOrEmpty(release.Tag) || release.Tag.Length < 2 || release.Tag[0] != 'v')
            {
                return null;
            }

            if (!Version.TryParse(release.Tag.Substring(1), out var version))
            {
                return null;
            }

            string vsix = null;
            string checksum = null;
            foreach (var asset in release.Assets ?? new List<GitHubAsset>())
            {
                if (string.Equals(asset.Name, VsixAssetName, StringComparison.Ordinal))
                {
                    vsix = asset.Url;
                }
                else if (string.Equals(asset.Name, ChecksumAssetName, StringComparison.Ordinal))
                {
                    checksum = asset.Url;
                }
            }

            return string.IsNullOrEmpty(vsix) || string.IsNullOrEmpty(checksum) ? null : new ReleaseInfo(version, release.PageUrl, vsix, checksum);
        }

        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            public string Tag { get; set; }

            [DataMember(Name = "html_url")]
            public string PageUrl { get; set; }

            [DataMember(Name = "draft")]
            public bool Draft { get; set; }

            [DataMember(Name = "prerelease")]
            public bool Prerelease { get; set; }

            [DataMember(Name = "assets")]
            public List<GitHubAsset> Assets { get; set; }
        }

        [DataContract]
        private sealed class GitHubAsset
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "browser_download_url")]
            public string Url { get; set; }
        }
    }
}
