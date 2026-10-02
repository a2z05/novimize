using Microsoft.Win32;
using WinOpt.Core.Models;
using WinOpt.Engine.Gaming;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Game mode's whole claim is that it puts back what it changed. Every test
/// here owns both halves of that claim: a registry key it created, and a folder
/// the session file lives in — nothing in this class can reach the machine's
/// real session or a policy.
/// </summary>
///
/// <remarks>
/// The class is deliberately one unit: <see cref="GamingState"/> redirects its
/// storage for the whole process, so these tests must not overlap with each
/// other or with anything else that assumes the default location.
/// </remarks>
public class GameModeSessionTests : IDisposable
{
    private const string ValueName = "WinOptProbe";
    private readonly string _subKey = $@"Software\WinOpt\Tests\{Guid.NewGuid():N}";
    private readonly string _store;

    public GameModeSessionTests()
    {
        _store = Path.Combine(Path.GetTempPath(), "WinOptTests", Guid.NewGuid().ToString("N"));
        GamingState.UseDirectory(_store);
    }

    public void Dispose()
    {
        GamingState.ResetDirectory();
        try { Registry.CurrentUser.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false); } catch { /* already gone */ }
        try { Directory.Delete(_store, recursive: true); } catch { /* already gone */ }
    }

    // --- Capture → apply → restore ---

    [Fact]
    public async Task CaptureApplyRestore_PutsTheRecordedValueBack()
    {
        WriteDword(7);

        var control = Capture();

        Assert.Null(control.Error);
        Assert.True(control.Restorable, "A control that was read successfully must be undoable.");
        Assert.True(control.BeforeExisted);
        Assert.Equal("7", control.Before);

        var (applied, applyError) = await ApplyAsync(control);
        Assert.True(applied, applyError);
        Assert.Equal(1, Convert.ToInt32(ReadDword()));

        var (restored, restoreError) = await RestoreAsync(control);
        Assert.True(restored, restoreError);
        Assert.Equal(7, Convert.ToInt32(ReadDword()));
    }

    [Fact]
    public async Task Restore_LeavesWhatWasAbsentAbsent()
    {
        // "Never set" and "set to nothing" are different states. Restoring an
        // absent value by writing an empty string would leave a policy behind
        // that the user never had.
        var control = Capture();

        Assert.False(control.BeforeExisted);
        Assert.Null(control.Before);
        Assert.True(control.Restorable, "Absence is a recorded state — it is exactly what restore deletes.");

        var (applied, applyError) = await ApplyAsync(control);
        Assert.True(applied, applyError);
        Assert.Equal(1, Convert.ToInt32(ReadDword()));

        var (restored, restoreError) = await RestoreAsync(control);
        Assert.True(restored, restoreError);
        Assert.Null(ReadDword());
    }

    [Fact]
    public void Capture_WhenTheKeyCannotBeRead_IsReportedAsRefused()
    {
        // A capture that failed has no previous value, so it must never look
        // like something stop could undo.
        var control = GameModeManager.CaptureRegistryControl(
            "notifications", @"NotARealHive\Nope", ValueName, "1", "probe");

        Assert.NotNull(control.Error);
        Assert.False(control.Restorable);
        Assert.False(control.Applied);
    }

    // --- Session lifecycle ---

    [Fact]
    public async Task Start_WithNothingApplicable_WritesNoSessionFile()
    {
        var result = await new GameModeManager().StartAsync(new GameModeOptions
        {
            Plan = "no-such-plan-on-this-machine",
            Only = new[] { "power-plan" },
            Notifications = false,
            BackgroundApps = false,
        });

        Assert.False(result.Success);
        Assert.Contains("Nothing was changed", result.Message, StringComparison.OrdinalIgnoreCase);
        // The file is the restore path: with nothing captured there is nothing
        // to restore, and leaving a file behind would make every later start
        // refuse on a session that does not exist.
        Assert.False(File.Exists(GamingState.SessionFile));
    }

    [Fact]
    public async Task Start_OverALiveSession_IsRefusedAndLeavesTheOldOneIntact()
    {
        var seeded = new GameModeSession
        {
            Id = "keep-this-session",
            GamePath = @"D:\Games\SomeGame",
        };
        GamingState.Write(GamingState.SessionFile, seeded);

        var result = await new GameModeManager().StartAsync(new GameModeOptions
        {
            Plan = "balanced",
            Only = new[] { "power-plan" },
            Notifications = false,
            BackgroundApps = false,
        });

        Assert.False(result.Success);
        Assert.Contains("already active", result.Message, StringComparison.OrdinalIgnoreCase);

        var after = GamingState.Read<GameModeSession>(GamingState.SessionFile);
        Assert.NotNull(after);
        Assert.Equal("keep-this-session", after!.Id);
    }

    [Fact]
    public void Status_WithoutASession_ReportsInactive()
    {
        var status = new GameModeManager().Status();

        Assert.False(status.Active);
        Assert.Null(status.Session);
        Assert.Empty(status.Controls);
    }

    [Fact]
    public async Task Stop_WithoutASession_SaysSoInsteadOfPretending()
    {
        var result = await new GameModeManager().StopAsync();

        Assert.True(result.Success);
        Assert.Contains("nothing to restore", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Controls);
    }

    // --- Helpers ---

    private GameModeControl Capture() => GameModeManager.CaptureRegistryControl(
        // The kind only selects the apply/restore branch; the key path is what
        // decides where the write lands, and it is this test's own key.
        "notifications", $@"HKCU\{_subKey}", ValueName, "1", "probe");

    private static Task<(bool Ok, string? Error)> ApplyAsync(GameModeControl control)
        => new GameModeManager().ApplyAsync(control);

    private static Task<(bool Ok, string? Error)> RestoreAsync(GameModeControl control)
        => new GameModeManager().RestoreAsync(control);

    private void WriteDword(int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_subKey, writable: true);
        key.SetValue(ValueName, value, RegistryValueKind.DWord);
    }

    private object? ReadDword()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: false);
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }
}
