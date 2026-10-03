using WinOpt.Engine.Installer;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Opening a tool by name only works if the name we display and the name the
/// Start menu filed it under can be reconciled without guessing. These tests
/// pin the reconciliation rules, which are the part that decides whether
/// "PowerToys" opens PowerToys or nothing at all.
/// </summary>
public class StartMenuTests
{
    private static readonly StartEntry[] Entries =
    {
        new("PowerToys Settings", "Microsoft.PowerToys"),
        new("PowerToys Awake", "Microsoft.PowerToys.Awake"),
        new("Fences 6", "Stardock.Fences6"),
        new("Rainmeter", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Rainmeter\Rainmeter.exe"),
        new("TranslucentTB", "TranslucentTB"),
    };

    [Fact]
    public void Parse_ReadsTheTabSeparatedPairs()
    {
        var output = "Alpha.App\tAlpha\r\n" +
                     "Beta App\tBeta App Store\r\n" +
                     "\r\n" +
                     "NoTabHere\r\n";

        var entries = StartMenu.Parse(output);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Alpha.App", entries[0].AppId);
        Assert.Equal("Alpha", entries[0].Name);
        Assert.Equal("Beta App Store", entries[1].Name);
    }

    [Fact]
    public void Parse_KeepsATabInsideAName()
    {
        // Split on the first tab only: an AppID cannot contain one, a display
        // name conceivably can, and splitting on all of them would shift every
        // subsequent column.
        var entries = StartMenu.Parse("Shell.App\tSome\tName\n");

        var only = Assert.Single(entries);
        Assert.Equal("Shell.App", only.AppId);
        Assert.Equal("Some\tName", only.Name);
    }

    [Fact]
    public void Parse_IgnoresBlankAndMalformedLines()
    {
        var entries = StartMenu.Parse("\n\n   \njust-a-name\n\tNoAppId\n\n");

        Assert.Empty(entries);
    }

    [Fact]
    public void Resolve_PrefersAnExactMatch()
    {
        var match = StartMenu.Resolve(Entries, new[] { "Rainmeter" });

        Assert.Equal("Rainmeter", match!.Name);
    }

    [Fact]
    public void Resolve_UsesTheStartMenuNameWhenTheCardNameDiffers()
    {
        // The catalogue card says "PowerToys"; the Start menu filed it as
        // "PowerToys Settings". The Start name is offered first, so it wins.
        var match = StartMenu.Resolve(Entries, new[] { "PowerToys Settings", "PowerToys" });

        Assert.Equal("PowerToys Settings", match!.Name);
    }

    [Fact]
    public void Resolve_FallsBackToAPrefixWhenOnlyTheCardNameIsKnown()
    {
        var match = StartMenu.Resolve(Entries, new[] { "Fences" });

        Assert.Equal("Fences 6", match!.Name);
    }

    [Fact]
    public void Resolve_ChoosesTheShortestPrefix()
    {
        // Both "PowerToys Settings" and "PowerToys Awake" begin with the
        // candidate. The shorter wins, because when a base shortcut and its
        // features are both filed under the same prefix, the shorter is the
        // application and the longer are the features of it. It is a rule that
        // can be wrong — see the fixture, where neither is the application —
        // but it is deterministic, and a wrong-but-stable answer is one the
        // card can explain rather than one that changes between runs.
        var match = StartMenu.Resolve(Entries, new[] { "PowerToys" });

        Assert.Equal("PowerToys Awake", match!.Name);
    }

    [Fact]
    public void Resolve_ReturnsNothingWhenNoEntryMatches()
    {
        var match = StartMenu.Resolve(Entries, new[] { "GlazeWM" });

        Assert.Null(match);
    }

    [Fact]
    public void Resolve_SkipsBlankCandidates()
    {
        var match = StartMenu.Resolve(Entries, new[] { "", "   ", "TranslucentTB" });

        Assert.Equal("TranslucentTB", match!.Name);
    }

    [Fact]
    public void Resolve_MatchesWithoutCaseSensitivity()
    {
        var match = StartMenu.Resolve(Entries, new[] { "rainmeter" });

        Assert.Equal("Rainmeter", match!.Name);
    }
}
