using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>
/// A point-in-time snapshot of system state before optimizations
/// were applied. Enables full rollback.
/// </summary>
public sealed record Snapshot
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("systemInfo")]
    public SystemInfoSnapshot? SystemInfo { get; init; }

    [JsonPropertyName("entries")]
    public List<SnapshotEntry> Entries { get; init; } = new();

    [JsonPropertyName("tweaksApplied")]
    public List<string> TweaksApplied { get; init; } = new();

    [JsonPropertyName("checksum")]
    public string Checksum { get; init; } = string.Empty;
}

/// <summary>
/// Minimal system info at snapshot time (subset of SystemInfo).
/// </summary>
public sealed class SystemInfoSnapshot
{
    [JsonPropertyName("osVersion")]
    public string OsVersion { get; init; } = string.Empty;

    [JsonPropertyName("buildNumber")]
    public int BuildNumber { get; init; }

    [JsonPropertyName("cpuName")]
    public string CpuName { get; init; } = string.Empty;

    [JsonPropertyName("ramGb")]
    public double RamGb { get; init; }

    [JsonPropertyName("hostname")]
    public string Hostname { get; init; } = Environment.MachineName;
}

/// <summary>
/// A single entry in a snapshot — captures the before-state of
/// one specific setting that was modified.
/// </summary>
public sealed class SnapshotEntry
{
    /// <summary>Tweak ID this entry corresponds to</summary>
    [JsonPropertyName("tweakId")]
    public string TweakId { get; init; } = string.Empty;

    /// <summary>What was modified (e.g. registry path, service name)</summary>
    [JsonPropertyName("target")]
    public string Target { get; init; } = string.Empty;

    /// <summary>The value before modification</summary>
    [JsonPropertyName("oldValue")]
    public string? OldValue { get; init; }

    /// <summary>The value after modification</summary>
    [JsonPropertyName("newValue")]
    public string? NewValue { get; init; }

    /// <summary>The method used to make the change</summary>
    [JsonPropertyName("method")]
    public TweakMethod Method { get; init; }

    /// <summary>The exact command that was executed</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>Whether the change was verified after application</summary>
    [JsonPropertyName("verified")]
    public bool Verified { get; init; }

    /// <summary>The restore command to undo this specific change</summary>
    [JsonPropertyName("restoreCommand")]
    public string? RestoreCommand { get; init; }
}

/// <summary>
/// Summary of changes applied in a session.
/// </summary>
public sealed record SessionResult
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("snapshotId")]
    public string SnapshotId { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("tweaksAttempted")]
    public int TweaksAttempted { get; init; }

    [JsonPropertyName("tweaksSucceeded")]
    public int TweaksSucceeded { get; init; }

    [JsonPropertyName("tweaksSkipped")]
    public int TweaksSkipped { get; init; }

    [JsonPropertyName("tweaksFailed")]
    public int TweaksFailed { get; init; }

    /// <summary>
    /// Tweaks that were refused because the process is not elevated. These are
    /// not failures — the command is valid, the shell just lacks the rights —
    /// so they are counted apart from <see cref="TweaksFailed"/> and reported
    /// with their own hint.
    /// </summary>
    [JsonPropertyName("tweaksNeedElevation")]
    public int TweaksNeedElevation { get; init; }

    [JsonPropertyName("results")]
    public List<TweakResult> Results { get; init; } = new();

    [JsonPropertyName("duration")]
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Result of applying a single tweak.
/// </summary>
public sealed class TweakResult
{
    [JsonPropertyName("tweakId")]
    public string TweakId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public TweakResultStatus Status { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("previousState")]
    public TweakState PreviousState { get; init; }

    [JsonPropertyName("currentState")]
    public TweakState CurrentState { get; init; }

    [JsonPropertyName("verified")]
    public bool Verified { get; init; }

    [JsonPropertyName("snapshotEntry")]
    public SnapshotEntry? SnapshotEntry { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TweakResultStatus
{
    Success,
    AlreadyApplied,
    Skipped,
    Failed,
    VerificationFailed,
    RollbackFailed,
    Incompatible,
    ConflictsDetected,
    RequiresElevation,
    SecurityBlocked
}
