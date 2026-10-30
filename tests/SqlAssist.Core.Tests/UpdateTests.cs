using System;
using SqlAssist.Core.Update;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class UpdateTests
    {
        private const string Download = "https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/releases/download/v1.3.0/";
        private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static string Json(string tag = "v1.3.0", string assets = null, bool prerelease = false) =>
            "{\"tag_name\":\"" + tag + "\",\"name\":\"SqlAssist " + tag + "\",\"draft\":false,\"prerelease\":" + (prerelease ? "true" : "false") + "," +
            "\"html_url\":\"https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/releases/tag/" + tag + "\",\"body\":\"notes\"," +
            "\"assets\":[" + (assets ?? "{\"name\":\"SqlAssistSetup-1.3.0.exe\",\"browser_download_url\":\"" + Download + "SqlAssistSetup-1.3.0.exe\",\"digest\":\"sha256:" + Hash.ToUpperInvariant() + "\",\"size\":1}") + "]}";

        [Theory]
        [InlineData("v1.3.0", "1.3.0")]
        [InlineData("1.3", "1.3.0")]
        [InlineData("V1.2.24.7", "1.2.24")]
        public void TryParseVersion_NormalizesToThreeParts(string text, string expected)
        {
            Assert.True(UpdatePolicy.TryParseVersion(text, out var v));
            Assert.Equal(new Version(expected), v);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("latest")]
        public void TryParseVersion_RejectsNonVersions(string text) => Assert.False(UpdatePolicy.TryParseVersion(text, out _));

        [Fact]
        public void IsNewer_ComparesNormalizedVersions()
        {
            Assert.True(UpdatePolicy.IsNewer(new Version("1.2.24"), new Version("1.3.0")));
            Assert.False(UpdatePolicy.IsNewer(new Version("1.3.0"), new Version("1.3")));
            Assert.False(UpdatePolicy.IsNewer(new Version("1.3.1"), new Version("1.3.0")));
        }

        [Fact]
        public void Parse_ReadsVersionPageAndSetupAsset()
        {
            var r = ReleaseInfo.Parse(Json());
            Assert.Equal(new Version("1.3.0"), r.Version);
            Assert.Equal("https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/releases/tag/v1.3.0", r.PageUrl);
            Assert.Equal(Download + "SqlAssistSetup-1.3.0.exe", r.SetupUrl);
            Assert.Equal("SqlAssistSetup-1.3.0.exe", r.SetupFileName);
            Assert.Equal(Hash, r.SetupSha256);
        }

        [Fact]
        public void Parse_IgnoresAssetsOutsideThisRepository()
        {
            var r = ReleaseInfo.Parse(Json(assets: "{\"name\":\"SqlAssistSetup-1.3.0.exe\",\"browser_download_url\":\"https://evil.example/SqlAssistSetup-1.3.0.exe\"}"));
            Assert.NotNull(r);
            Assert.Null(r.SetupUrl);
        }

        [Fact]
        public void Parse_PrefersTheAssetNamedForTheVersion()
        {
            var r = ReleaseInfo.Parse(Json(assets:
                "{\"name\":\"SqlAssistSetup-old.exe\",\"browser_download_url\":\"" + Download + "SqlAssistSetup-old.exe\"}," +
                "{\"name\":\"SqlAssist.vsix\",\"browser_download_url\":\"" + Download + "SqlAssist.vsix\"}," +
                "{\"name\":\"SqlAssistSetup-1.3.0.exe\",\"browser_download_url\":\"" + Download + "SqlAssistSetup-1.3.0.exe\",\"digest\":null}"));
            Assert.Equal("SqlAssistSetup-1.3.0.exe", r.SetupFileName);
            Assert.Null(r.SetupSha256);
        }

        [Fact]
        public void Parse_SkipsPrereleasesAndBadTags()
        {
            Assert.Null(ReleaseInfo.Parse(Json(prerelease: true)));
            Assert.Null(ReleaseInfo.Parse(Json(tag: "nightly")));
        }

        [Fact]
        public void ShouldAutoCheck_OncePerDayAndOnlyWhenEnabled()
        {
            var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(UpdatePolicy.ShouldAutoCheck(new UpdateState(), now));
            Assert.False(UpdatePolicy.ShouldAutoCheck(new UpdateState { LastCheckUtc = now.AddHours(-2) }, now));
            Assert.True(UpdatePolicy.ShouldAutoCheck(new UpdateState { LastCheckUtc = now.AddDays(-1) }, now));
            Assert.True(UpdatePolicy.ShouldAutoCheck(new UpdateState { LastCheckUtc = now.AddDays(3) }, now));
            Assert.False(UpdatePolicy.ShouldAutoCheck(new UpdateState { AutoCheck = false }, now));
        }

        [Fact]
        public void ShouldNotify_SkippedVersionIsQuietOnlyForTheAutomaticCheck()
        {
            var release = ReleaseInfo.Parse(Json());
            var current = new Version("1.2.24");
            var skipped = new UpdateState { SkippedVersion = new Version("1.3.0") };
            Assert.True(UpdatePolicy.ShouldNotify(current, release, new UpdateState(), manual: false));
            Assert.False(UpdatePolicy.ShouldNotify(current, release, skipped, manual: false));
            Assert.True(UpdatePolicy.ShouldNotify(current, release, skipped, manual: true));
            Assert.False(UpdatePolicy.ShouldNotify(new Version("1.3.0"), release, new UpdateState(), manual: true));
        }

        [Fact]
        public void UpdateState_RoundTrips()
        {
            var state = new UpdateState { AutoCheck = false, LastCheckUtc = new DateTime(2026, 10, 20, 5, 6, 7, DateTimeKind.Utc), SkippedVersion = new Version("1.3.0") };
            var back = UpdateState.Parse(state.ToString());
            Assert.False(back.AutoCheck);
            Assert.Equal(state.LastCheckUtc, back.LastCheckUtc);
            Assert.Equal(DateTimeKind.Utc, back.LastCheckUtc.Value.Kind);
            Assert.Equal(state.SkippedVersion, back.SkippedVersion);
        }

        [Fact]
        public void UpdateState_ToleratesGarbage()
        {
            var state = UpdateState.Parse("junk\r\nAutoCheck=maybe\r\nLastCheckUtc=never\r\n=x\r\n");
            Assert.True(state.AutoCheck);
            Assert.Null(state.LastCheckUtc);
        }
    }
}
