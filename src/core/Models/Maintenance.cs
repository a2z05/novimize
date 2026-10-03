using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>One maintenance action the page offers.</summary>
public sealed record MaintenanceTool
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>One sentence on what it does when nothing is deleted.</summary>
    public string What { get; init; } = string.Empty;

    /// <summary>What will be deleted, named. Null when nothing is.</summary>
    public string? Deletes { get; init; }

    /// <summary>Bytes that would be freed, when that can be measured.</summary>
    public long? Bytes { get; init; }

    /// <summary>How long it usually takes, as a sentence.</summary>
    public string Effort { get; init; } = string.Empty;

    /// <summary>True when finishing needs a restart.</summary>
    public bool RestartRequired { get; init; }

    /// <summary>True for repairs that change system files rather than clearing caches.</summary>
    public bool Repairs { get; init; }

    /// <summary>Set when the size could not be measured, with why.</summary>
    public string? MeasuredNote { get; init; }

    public bool Available { get; init; } = true;
    public string? UnavailableReason { get; init; }
}

public sealed record MaintenanceStatus
{
    public List<MaintenanceTool> Tools { get; init; } = new();

    /// <summary>Bytes the clearable tools would free, where measurable.</summary>
    public long? ReclaimableBytes { get; init; }

    public int RepairCount { get; init; }
    public int RestartCount { get; init; }
    public string? Error { get; init; }
}

public record MaintenanceChange
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

    /// <summary>Bytes actually freed, when the tool reports it.</summary>
    public long? Freed { get; init; }
}
