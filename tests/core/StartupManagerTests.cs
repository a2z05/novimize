using WinOpt.Engine.Startup;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Turning a Run value into a path, and saying something honest about what it
/// costs at sign-in. Both are heuristics over other people's command lines,
/// so the cases here are the ones real entries actually contain.
/// </summary>
public class StartupManagerTests
{
    [Theory]
    [InlineData("\"C:\\App\\app.exe\" -silent", "C:\\App\\app.exe")]
    [InlineData("C:\\App\\app.exe", "C:\\App\\app.exe")]
    [InlineData("'C:\\App\\app.exe' /start", "C:\\App\\app.exe")]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --flag", "C:\\Program Files\\App\\app.exe")]
    public void ExtractTarget_TakesTheQuotedPathAsAWhole(string command, string expected)
    {
        Assert.Equal(expected, StartupManager.ExtractTarget(command));
    }

    /// <summary>
    /// The Run key parser does not stop at the first space — it grows the
    /// candidate until a file exists, which is why an unquoted path with a
    /// space in it starts at all. Stopping at the first space reports that
    /// entry as a missing file called "C:\Program".
    /// </summary>
    [Fact]
    public void ExtractTarget_GrowsAnUnquotedPathUntilAFileIsFound()
    {
        var exe = Path.Combine(Path.GetTempPath(), $"novimize-startup-{Guid.NewGuid():N}.exe");
        var directory = Path.GetDirectoryName(exe)!;
        var spaced = Path.Combine(directory, "a b c", "app.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(spaced)!);
        File.WriteAllText(spaced, "");

        try
        {
            // The whole command is the path plus arguments; the path is what
            // exists, so that is what must come back.
            var command = $"{spaced} -silent --flag";

            Assert.Equal(spaced, StartupManager.ExtractTarget(command));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(spaced)!, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void ExtractTarget_ReportsAnMissingExecutableRatherThanItsFirstWord()
    {
        var target = StartupManager.ExtractTarget(
            @"C:\Program Files (x86)\Gone\app.exe -silent");

        Assert.Equal(@"C:\Program Files (x86)\Gone\app.exe", target);
    }

    [Theory]
    [InlineData("app.exe -flag")]
    [InlineData("")]
    [InlineData(null)]
    public void ExtractTarget_WithNoDirectoryIsNotAPathWeCanTest(string? command)
    {
        Assert.Null(StartupManager.ExtractTarget(command));
    }

    private static RawItem Item(string name, string command, string kind = "run") => new()
    {
        Name = name,
        Command = command,
        Kind = kind,
        Location = "HKCU Run",
        Hive = "HKCU",
    };

    [Fact]
    public void Classify_WhenTheFileIsGoneSaysSoFirst()
    {
        var (impact, reason) = StartupManager.Classify(
            Item("Ghost", @"C:\Gone\app.exe"), target: @"C:\Gone\app.exe", broken: true);

        Assert.Equal("Broken", impact);
        Assert.Contains(@"C:\Gone\app.exe", reason);
    }

    [Theory]
    [InlineData("Discord", "\"C:\\Users\\x\\Discord\\Update.exe\" --processStart Discord.exe")]
    [InlineData("FACEIT", "\"C:\\Users\\x\\FACEIT\\update.exe\" --processStart FACEIT.exe")]
    public void Classify_AnUpdaterStubIsCalledHeavy(string name, string command)
    {
        var (impact, reason) = StartupManager.Classify(Item(name, command), "x", broken: false);

        Assert.Equal("Heavy", impact);
        Assert.Contains("updater", reason);
    }

    [Fact]
    public void Classify_ASingleExecutableWithNoArgumentsIsCheap()
    {
        var (impact, reason) = StartupManager.Classify(
            Item("Ditto", @"C:\Tools\Ditto\Ditto.exe"), @"C:\Tools\Ditto\Ditto.exe", broken: false);

        Assert.Equal("Low", impact);
        Assert.Contains("no arguments", reason);
    }

    [Fact]
    public void Classify_AProgramWithArgumentsIsInTheMiddle()
    {
        var (impact, _) = StartupManager.Classify(
            Item("Epic", "\"C:\\Epic\\EpicGamesLauncher.exe\" -silent -launchcontext=boot"),
            "x", broken: false);

        Assert.Equal("Medium", impact);
    }

    [Fact]
    public void Classify_ALogonTaskSaysWhatItIsRatherThanGuessingAtItsCost()
    {
        var (impact, reason) = StartupManager.Classify(
            Item("SomeTask", @"C:\Windows\System32\notepad.exe", kind: "task"),
            @"C:\Windows\System32\notepad.exe",
            broken: false);

        Assert.Equal("Medium", impact);
        Assert.Contains("scheduled task", reason);
    }

    /// <summary>
    /// The whole point of the impact column: every row has to be able to say
    /// why it was put in the box it is in.
    /// </summary>
    [Theory]
    [InlineData("run", "C:\\a\\b.exe", false)]
    [InlineData("run", "C:\\a\\b.exe -x", false)]
    [InlineData("folder", "C:\\Startup\\thing.lnk", false)]
    [InlineData("task", "C:\\Windows\\x.exe", false)]
    public void Classify_AlwaysReturnsAReason(string kind, string command, bool broken)
    {
        var (impact, reason) = StartupManager.Classify(Item("x", command, kind), "t", broken);

        Assert.NotEmpty(impact);
        Assert.NotEmpty(reason);
        Assert.Contains(impact, new[] { "Broken", "Heavy", "Medium", "Low", "Unknown" });
    }
}
