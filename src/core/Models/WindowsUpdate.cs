using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>One entry from the update history Windows keeps.</summary>
public sealed record UpdateHistoryEntry
{
    public string Title { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public DateTimeOffset? When { get; init; }
    public int HResult { get; init; }
    public bool Succeeded => HResult == 0;
}

/// <summary>One update Windows says is waiting.</summary>
public sealed record PendingUpdate
{
    public string Title { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public long? SizeBytes { get; init; }
    public bool IsDownloaded { get; init; }
    public string? Description { get; init; }
}

public sealed record WindowsUpdateStatus
{
    /// <summary>Installed | Pending reboot | Scan failed | Service stopped | Unknown.</summary>
    public string State { get; init; } = "Unknown";

    public bool UpdateServiceRunning { get; init; }
    public DateTimeOffset? LastSearchSuccess { get; init; }
    public DateTimeOffset? LastInstallSuccess { get; init; }
    public DateTimeOffset? LastBoot { get; init; }

    /// <summary>Why a restart is owed, named. Empty when none is.</summary>
    public List<string> PendingRebootReasons { get; init; } = new();

    public bool PendingReboot => PendingRebootReasons.Count > 0;

    public List<UpdateHistoryEntry> History { get; init; } = new();

    /// <summary>Populated only by an explicit scan — never on a page load.</summary>
    public List<PendingUpdate> Available { get; init; } = new();
    public bool Scanned { get; init; }
    public string? ScanNote { get; init; }

    public string? Error { get; init; }
}

public record WindowsUpdateChange
{
    public string Action { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool Unchanged { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Affected { get; init; }
    public bool NeedsElevation { get; init; }
    public string? Log { get; init; }
    public List<string> Preview { get; init; } = new();
    public bool RestartRequired { get; init; }
}
