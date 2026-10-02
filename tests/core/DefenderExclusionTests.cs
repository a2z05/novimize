using WinOpt.Engine.Security;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Where an exclusion may point. This is the gate on the one feature that turns
/// a scanner off for a folder, so the refusals are the part worth testing: a
/// game library must be excludable, and a system location must not be.
/// </summary>
public class DefenderExclusionTests
{
    private static string Under(params string[] parts)
        => Path.Combine(parts);

    [Theory]
    [InlineData(@"C:\Windows", "the Windows directory")]
    [InlineData(@"C:\Windows\System32", "a Windows subtree")]
    [InlineData(@"C:\Program Files\Common Files", "a Program Files subtree")]
    [InlineData(@"C:\Program Files (x86)\Steam", "a 32-bit Program Files subtree")]
    public void ValidatePath_RefusesSystemLocations(string path, string because)
    {
        var (allowed, reason) = DefenderExclusions.ValidatePath(path);

        Assert.False(allowed, $"'{path}' should have been refused ({because}).");
        Assert.False(string.IsNullOrWhiteSpace(reason), "A refusal has to say why.");
    }

    [Fact]
    public void ValidatePath_RefusesTheUserProfile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var (allowed, reason) = DefenderExclusions.ValidatePath(Under(home, "Games", "SomeGame"));

        Assert.False(allowed);
        Assert.Contains(home, reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePath_RefusesTheWindowsDirectoryItself()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.False(DefenderExclusions.ValidatePath(windows).Allowed);
    }

    [Fact]
    public void ValidatePath_RefusesEntireDrives()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var (allowed, reason) = DefenderExclusions.ValidatePath(root);

        Assert.False(allowed);
        Assert.Contains("drive", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePath_RefusesNetworkPaths()
    {
        var (allowed, reason) = DefenderExclusions.ValidatePath(@"\\fileserver\games\SomeGame");

        Assert.False(allowed);
        Assert.Contains("UNC", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePath_RefusesAnEmptyPath()
    {
        Assert.False(DefenderExclusions.ValidatePath("").Allowed);
        Assert.False(DefenderExclusions.ValidatePath("   ").Allowed);
        Assert.False(DefenderExclusions.ValidatePath(null!).Allowed);
    }

    [Theory]
    [InlineData(@"C:\Games\SomeGame")]
    [InlineData(@"D:\SteamLibrary\steamapps\common\ELDEN RING")]
    [InlineData(@"E:\Launchers\GOG Games")]
    public void ValidatePath_AllowsGameLibraries(string path)
    {
        var (allowed, reason) = DefenderExclusions.ValidatePath(path);

        Assert.True(allowed, $"'{path}' should be excludable — it is not a system location. Reason: {reason}");
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void ValidatePath_AllowsALocationThatDoesNotExistYet()
    {
        // Existence is gate 3's job, not this one's: validation is about what
        // kind of path it is, and a mistyped path is caught by the caller.
        var (allowed, _) = DefenderExclusions.ValidatePath(@"C:\Games\NotInstalledYet\Game");
        Assert.True(allowed);
    }
}
