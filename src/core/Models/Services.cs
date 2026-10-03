using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>How a service is set to start.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceStartMode
{
    Boot,
    System,
    Automatic,
    Manual,
    Disabled,
}

public sealed record ServiceEntry
{
    /// <summary>The short name — the only thing that identifies it.</summary>
    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;
    public ServiceStartMode StartMode { get; init; } = ServiceStartMode.Manual;

    /// <summary>The executable, when the service has one of its own.</summary>
    public string? Path { get; init; }

    /// <summary>Services that must be running for this one to run.</summary>
    public List<string> Requires { get; init; } = new();

    /// <summary>Services that need this one running — the reason not to stop it.</summary>
    public List<string> DependentOn { get; init; } = new();

    /// <summary>
    /// True when Novimize refuses to change it. The reason is carried with
    /// the flag, because "you cannot touch this" without saying which of the
    /// three rules applied is not an answer.
    /// </summary>
    public bool Protected { get; init; }
    public string? ProtectReason { get; init; }

    /// <summary>Where the start mode stood before Novimize first changed it.</summary>
    public ServiceStartMode? OriginalStartMode { get; init; }
}

public sealed record ServiceStatus
{
    public List<ServiceEntry> Services { get; init; } = new();
    public int Running { get; init; }
    public int Stopped { get; init; }
    public int ProtectedCount { get; init; }
    public bool ElevationKnown { get; init; }
    public string? Error { get; init; }
}

public record ServiceChange
{
    public string Action { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool Unchanged { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Affected { get; init; }
    public bool NeedsElevation { get; init; }
    public string? Log { get; init; }
    public List<string> Preview { get; init; } = new();
}

/// <summary>One scheduled task, with everything the brief asks to show.</summary>
public sealed record ScheduledTaskEntry
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;

    /// <summary>Combined name and path — the pair uniquely identifies a task.</summary>
    public string Id { get; init; } = string.Empty;

    public string TaskPath { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>What starts it, as one line: "At logon of any user".</summary>
    public string Trigger { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;

    public DateTimeOffset? LastRun { get; init; }
    public DateTimeOffset? NextRun { get; init; }
    /// <summary>HRESULT from the last run — 0x800710E0-style values do not fit an int.</summary>
    public long LastResult { get; init; }

    /// <summary>True when the task belongs to Microsoft rather than to something installed.</summary>
    public bool SystemTask { get; init; }

    /// <summary>True when Novimize has changed it, so it can be put back.</summary>
    public bool ChangedByNovimize { get; init; }
    public bool? OriginalEnabled { get; init; }
}

public sealed record ScheduledTaskStatus
{
    public List<ScheduledTaskEntry> Tasks { get; init; } = new();
    public int Enabled { get; init; }
    public int Disabled { get; init; }
    public int ChangedByNovimize { get; init; }
    public string? Error { get; init; }
}

public record ScheduledTaskChange
{
    public string Action { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool Unchanged { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Affected { get; init; }
    public bool NeedsElevation { get; init; }
    public string? Log { get; init; }
    public List<string> Preview { get; init; } = new();
}
