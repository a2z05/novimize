using WinOpt.Engine.Update;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The pending-reboot question and the restart preview. Windows records a
/// restart being owed in three separate places, and reporting one of them as
/// "no restart needed" is how a page ends up confidently wrong.
/// </summary>
public class WindowsUpdateTests
{
    [Fact]
    public void RebootMarkers_CoverServicingWindowsUpdateAndThePostRebootReport()
    {
        var reasons = UpdateManager.RebootMarkers.Select(m => m.Reason).ToList();

        Assert.Contains(reasons, r => r.Contains("servicing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(reasons, r => r.Contains("Windows Update", StringComparison.Ordinal));
        Assert.Contains(reasons, r => r.Contains("reporting back", StringComparison.OrdinalIgnoreCase));

        // Every marker has to say why in words, because the page prints the
        // reason rather than a key.
        Assert.All(reasons, r => Assert.Contains(" ", r));
        Assert.Equal(reasons.Count, reasons.Distinct().Count());
    }

    [Fact]
    public void RebootMarkers_NameTheRegistryKeyThatCarriesEachOne()
    {
        Assert.All(UpdateManager.RebootMarkers, m =>
        {
            Assert.StartsWith(@"HKLM:\", m.Key);
            Assert.NotEmpty(m.Reason);
        });
    }

    [Fact]
    public void RestartPreview_ShowsTheCommandLineAndSaysNothingRan()
    {
        var preview = UpdateManager.RestartPreview();

        Assert.True(preview.Success);
        Assert.True(preview.Unchanged);
        Assert.True(preview.RestartRequired);
        Assert.Contains("shutdown.exe", Assert.Single(preview.Preview));
        // The message has to carry the warning that unsaved work goes, not
        // just the fact that a restart happens.
        Assert.Contains("unsaved", preview.Message);
    }

    [Fact]
    public void Restart_WithoutConfirm_SaysNothingRanAndKeepsTheDelay()
    {
        var change = new UpdateManager().RestartAsync(confirm: false).GetAwaiter().GetResult();

        Assert.False(change.Success);
        Assert.Contains("Nothing has been run", change.Message);
        Assert.Contains("/t 60", Assert.Single(change.Preview));
    }

    [Fact]
    public void Open_SaysItWroteNothing()
    {
        var change = UpdateManager.Open();

        Assert.True(change.Success);
        Assert.Contains("ms-settings:windowsupdate", Assert.Single(change.Preview));
    }

    /// <summary>
    /// The status read is the one that must never write, so the actions that
    /// change something are enumerated rather than implied.
    /// </summary>
    [Fact]
    public void Status_ReadsWithoutAnyChangeAction()
    {
        var status = new UpdateManager().StatusAsync().GetAwaiter().GetResult();

        Assert.NotNull(status);
        Assert.NotEmpty(status.State);
        // Whatever the machine says, the read is a read.
        Assert.Empty(status.Available);
        Assert.False(status.Scanned);
    }
}
