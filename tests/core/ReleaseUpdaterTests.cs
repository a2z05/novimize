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
        var config = FindFile("tauri.conf.json", "src/ui/src-tauri");
        Assert.True(config is not null, "tauri.conf.json was not found above the test directory");

        using var document = JsonDocument.Parse(File.ReadAllText(config!));
        var bundled = document.RootElement.GetProperty("version").GetString();

        Assert.Equal(bundled, AppVersion.Value);
    }

    [Fact]
    public void AppVersion_IsASingleDigitSegmentVersionNotADefault()
    {
        // 1.0.0 is what a .NET assembly reports when nobody set one; an
        // updater built on it would compare every real release against a
        // number that was never true.
        Assert.NotEqual("1.0.0", AppVersion.Value);
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Value);
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
