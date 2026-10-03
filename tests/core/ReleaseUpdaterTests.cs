using System.Text.Json;
using WinOpt.Core;
using WinOpt.Engine.Release;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The updater's version arithmetic and the single source of its version.
/// Both are the kind of thing that is obviously right until the day 0.10.0
/// ships and a string comparison calls it older than 0.9.0.
/// </summary>
public class ReleaseUpdaterTests
{
    [Theory]
    [InlineData("v0.1.0", "0.1.0")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData("  0.1.0 ", "0.1.0")]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("", "")]
    [InlineData("v", "")]
    public void NormalizeVersion_StripsTheTagPrefixAndWhitespace(string tag, string expected)
    {
        Assert.Equal(expected, ReleaseUpdater.NormalizeVersion(tag));
    }

    [Theory]
    [InlineData("0.1.0", "0.2.0", true)]
    [InlineData("0.1.0", "0.1.1", true)]
    [InlineData("0.9.0", "0.10.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.2.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.0-beta", false)]
    [InlineData("0.1.0", "", false)]
    [InlineData("0.1.0", "v0.1.1", true)]
    public void IsNewer_ComparesEachSegmentAsANumber(string current, string latest, bool expected)
    {
        Assert.Equal(expected, ReleaseUpdater.IsNewer(current, latest));
    }

    /// <summary>
    /// The version the updater compares a release tag against is the one the
    /// installer declares. If they disagree, the app reports an update for
    /// the build it is already running.
    /// </summary>
    [Fact]
    public void AppVersion_MatchesWhatTheTauriBundleDeclares()
    {
        var bundled = ReadBundledVersion();

        Assert.Equal(bundled, AppVersion.Value);
    }

    /// <summary>
    /// The version on the sidebar is typed into the component, so nothing
    /// holds it in step with the bundle. Without this the app says 1.0.0 in
    /// the corner and the updater offers you 1.0.0 as an upgrade.
    /// </summary>
    [Fact]
    public void TheVersionOnScreen_IsTheOneThatShips()
    {
        var sidebar = FindFile("Sidebar.tsx", "src/ui/src/components");
        Assert.True(sidebar is not null, "Sidebar.tsx was not found above the test directory");

        var source = File.ReadAllText(sidebar!);
        var bundled = ReadBundledVersion();

        Assert.Contains($"v{bundled}", source);
    }

    private static string? ReadBundledVersion()
    {
        var config = FindFile("tauri.conf.json", "src/ui/src-tauri");
        Assert.True(config is not null, "tauri.conf.json was not found above the test directory");

        using var document = JsonDocument.Parse(File.ReadAllText(config!));
        var version = document.RootElement.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(version), "tauri.conf.json declares no version");
        return version;
    }

    /// <summary>
    /// 1.0.0 is what a .NET assembly reports when nobody set one, so it used
    /// to be treated as a smell. It is the shipped version now, and the test
    /// above against tauri.conf.json is what stops the number drifting from
    /// the release tag. What is left here is the shape: a three-segment
    /// numeric version, not a default, a prerelease or an empty string.
    /// </summary>
    [Fact]
    public void AppVersion_IsAThreeSegmentNumericVersion()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Value);
    }

    /// <summary>
    /// The value has to be one a person chose. An untouched 1.0.0 that
    /// happens to agree with an untouched assembly is indistinguishable from
    /// never having set it, so the constant carries the tag it was released
    /// as.
    /// </summary>
    [Fact]
    public void AppVersion_CarriesTheTagThatWasReleased()
    {
        Assert.Equal("1.0.0", AppVersion.Value);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 kB")]
    [InlineData(26_600_000, "25.4 MB")]
    public void Format_SaysBytesTheWayAPersonWould(long bytes, string expected)
    {
        Assert.Equal(expected, ReleaseUpdater.Format(bytes));
    }

    /// <summary>
    /// The feed address is a real, published URL. A typo here means every
    /// check fails with a 404 and the app reports an error instead of a
    /// version.
    /// </summary>
    [Fact]
    public void ReleasesUrl_IsAnHttpsGitHubApiAddressForThisRepository()
    {
        Assert.StartsWith("https://api.github.com/repos/", ReleaseUpdater.ReleasesUrl);
        Assert.Contains("novimize", ReleaseUpdater.ReleasesUrl);
        Assert.EndsWith("/releases/latest", ReleaseUpdater.ReleasesUrl);
    }

    private static string? FindFile(string name, string relativeDirectory)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, relativeDirectory, name);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }
}
