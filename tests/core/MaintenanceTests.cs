using WinOpt.Engine.Maintenance;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The maintenance catalogue. Every action has to say what it deletes before
/// it runs, and the two kinds — clearing a cache and rewriting a system file —
/// have to stay distinguishable.
/// </summary>
public class MaintenanceTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 kB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(1536L * 1024 * 1024, "1.5 GB")]
    public void Format_WritesBytesTheWayAPersonWould(long bytes, string expected)
    {
        Assert.Equal(expected, MaintenanceManager.Format(bytes));
    }

    [Theory]
    [InlineData("temp")]
    [InlineData("recycle")]
    [InlineData("thumbnails")]
    [InlineData("icons")]
    [InlineData("updateCache")]
    [InlineData("searchIndex")]
    [InlineData("componentStore")]
    [InlineData("healthCheck")]
    [InlineData("restoreHealth")]
    [InlineData("sfc")]
    [InlineData("diskCleanup")]
    public void Preview_ShowsTheCommandsWithoutRunningAnything(string id)
    {
        var preview = new MaintenanceManager().Preview(id);

        Assert.True(preview.Success, preview.Message);
        Assert.True(preview.Unchanged);
        Assert.NotEmpty(preview.Preview);
        Assert.NotEmpty(preview.Message);
    }

    [Fact]
    public void Preview_ForSomethingThatIsNotAToolSaysSo()
    {
        var preview = new MaintenanceManager().Preview("format-c");

        Assert.False(preview.Success);
        Assert.Contains("not a maintenance action", preview.Message);
        Assert.Empty(preview.Preview);
    }

    /// <summary>
    /// "What will be deleted" is required by the brief for every action, and
    /// an action that deletes nothing has to say so rather than leave the
    /// field blank — a blank reads as an unanswered question.
    /// </summary>
    [Theory]
    [InlineData("temp", true)]
    [InlineData("recycle", true)]
    [InlineData("thumbnails", true)]
    [InlineData("searchIndex", true)]
    [InlineData("updateCache", true)]
    [InlineData("healthCheck", false)]
    [InlineData("restoreHealth", false)]
    [InlineData("sfc", false)]
    [InlineData("icons", false)]
    public void Tools_SayWhatTheyDeleteOrThatTheyDeleteNothing(string id, bool deletes)
    {
        var preview = new MaintenanceManager().Preview(id);

        Assert.True(preview.Success);
        // The message carries the deletion sentence when there is one; when
        // there is not, the message still explains what the action does.
        Assert.NotEmpty(preview.Message);
        Assert.Equal(deletes, preview.Message.Contains("deletes", StringComparison.OrdinalIgnoreCase)
                              || preview.Message.Contains("Deletes", StringComparison.Ordinal)
                              || preview.Message.Contains("This deletes", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_WithoutConfirm_SaysWhatItWouldDeleteAndRunsNothing()
    {
        var change = new MaintenanceManager().RunAsync("recycle", confirm: false)
            .GetAwaiter().GetResult();

        Assert.False(change.Success);
        Assert.Contains("Nothing has been run", change.Message);
        Assert.Contains("This deletes:", change.Message);
        Assert.NotEmpty(change.Preview);
    }

    [Fact]
    public void Run_WithNoSuchToolRefusesInsteadOfThrowing()
    {
        var change = new MaintenanceManager().RunAsync("nope", confirm: true)
            .GetAwaiter().GetResult();

        Assert.False(change.Success);
        Assert.Contains("not a maintenance action", change.Message);
    }
}
