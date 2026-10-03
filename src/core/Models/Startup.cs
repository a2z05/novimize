using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>Where a startup entry lives.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StartupKind
{
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
    Run,

    /// <summary>The per-user Startup folder of the Start Menu.</summary>
    StartupFolder,

    /// <summary>The all-users Startup folder.</summary>
    CommonStartupFolder,

    /// <summary>A scheduled task whose trigger is logon or boot.</summary>
    ScheduledTask,

    /// <summary>A UWP app registered as a startup task.</summary>
    StartupTask,
}

/// <summary>
/// One thing that runs when you sign in.
///
/// Novimize never removes these. The only thing it writes is the
/// StartupApproved flag Windows itself uses — the same byte Task Manager
/// flips — so disabling is a setting and restoring is setting it back. A
/// delete would have to guess what the entry was for, and a program that
/// expects to be registered at logon does not fail quietly when it is not.
/// </summary>
public sealed record StartupItem
{
    /// <summary>Stable key: the source, the hive, and the entry's own name.</summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public string? Publisher { get; init; }

    /// <summary>The command exactly as it is registered.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>The executable the command points at, when one could be found.</summary>
    public string? TargetPath { get; init; }

    /// <summary>Registry key or folder the entry came from.</summary>
    public string Location { get; init; } = string.Empty;

    public StartupKind Kind { get; init; }

    /// <summary>HKCU or HKLM — decides whether flipping it needs rights.</summary>
    public string Hive { get; init; } = "HKCU";

    /// <summary>For a scheduled task: the folder it lives in, which is required to name it.</summary>
    public string? TaskPath { get; init; }

    /// <summary>From the StartupApproved flag: absent means enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>True when flipping the flag will not need administrator rights.</summary>
    public bool Writable { get; init; } = true;

    /// <summary>Broken | Heavy | Medium | Low | Unknown.</summary>
    public string Impact { get; init; } = "Unknown";

    /// <summary>Why the row above said that, in one sentence.</summary>
    public string ImpactReason { get; init; } = string.Empty;

    public bool Broken { get; init; }
}

public sealed record StartupStatus
{
    public List<StartupItem> Items { get; init; } = new();
    public string UserStartupFolder { get; init; } = string.Empty;
    public string CommonStartupFolder { get; init; } = string.Empty;
    public int EnabledCount { get; init; }
    public int DisabledCount { get; init; }
    public int BrokenCount { get; init; }
    public string? Error { get; init; }
}

public record StartupChange
{
    public string Action { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool Unchanged { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Affected { get; init; }
    public bool NeedsElevation { get; init; }
    public string? Log { get; init; }

    /// <summary>What will be written, before it is written.</summary>
    public List<string> Preview { get; init; } = new();
}
