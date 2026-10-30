using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlAssist.Core.Update
{
    /// <summary>The latest GitHub release, as far as updating cares: its version, page and the setup exe to download.</summary>
    public sealed class ReleaseInfo
    {
        public const string Repository = "kriangdet-chuaboonjun/SSMS22SqlAssist_Addon";
        public const string LatestApiUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string ReleasesPageUrl = "https://github.com/" + Repository + "/releases";
        /// <summary>Downloads are only taken from this repository's own release assets.</summary>
        private const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";

        public Version Version { get; }
        public string Tag { get; }
        public string PageUrl { get; }
        /// <summary>Null when the release has no setup exe attached (the page can still be opened).</summary>
        public string SetupUrl { get; }
        public string SetupFileName { get; }
        /// <summary>Lower-case hex SHA-256 of the setup exe, from the asset's GitHub digest; null when GitHub gave none.</summary>
        public string SetupSha256 { get; }

        private ReleaseInfo(Version version, string tag, string pageUrl, string setupUrl, string setupFileName, string sha256)
        {
            Version = version;
            Tag = tag;
            PageUrl = pageUrl;
            SetupUrl = setupUrl;
            SetupFileName = setupFileName;
            SetupSha256 = sha256;
        }

        /// <summary>Reads the JSON of GitHub's "latest release" API. Null for drafts, prereleases or a tag that is not a version.</summary>
        public static ReleaseInfo Parse(string json)
        {
            GitHubRelease release;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                release = (GitHubRelease)new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(stream);
            if (release == null || release.Draft || release.Prerelease) return null;
            if (!UpdatePolicy.TryParseVersion(release.TagName, out var version)) return null;

            string pageUrl = IsHttps(release.HtmlUrl) ? release.HtmlUrl : ReleasesPageUrl;
            var setup = (release.Assets ?? new List<GitHubAsset>())
                .Where(a => a?.Name != null && Regex.IsMatch(a.Name, @"^SqlAssistSetup.*\.exe$", RegexOptions.IgnoreCase)
                            && a.DownloadUrl != null && a.DownloadUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal))
                .OrderByDescending(a => a.Name.IndexOf(version.ToString(), StringComparison.Ordinal) >= 0)
                .FirstOrDefault();
            return new ReleaseInfo(version, release.TagName, pageUrl, setup?.DownloadUrl, SafeFileName(setup?.Name), Sha256(setup?.Digest));
        }

        private static bool IsHttps(string url) => url != null && url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase);

        private static string SafeFileName(string name) =>
            name == null || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? null : name;

        /// <summary>GitHub writes the digest as "sha256:&lt;64 hex&gt;".</summary>
        private static string Sha256(string digest)
        {
            var m = Regex.Match(digest ?? "", "^sha256:([0-9a-fA-F]{64})$");
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")] public string TagName { get; set; }
            [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
            [DataMember(Name = "draft")] public bool Draft { get; set; }
            [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
            [DataMember(Name = "assets")] public List<GitHubAsset> Assets { get; set; }
        }

        [DataContract]
        private sealed class GitHubAsset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
            [DataMember(Name = "digest")] public string Digest { get; set; }
        }
    }
}
