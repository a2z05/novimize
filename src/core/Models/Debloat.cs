using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>What the policy file thinks about a package, before the engine's own checks.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DebloatVerdict
{
    /// <summary>Microsoft ships it and the Store can put it back.</summary>
    Safe,

    /// <summary>Removable, but something you will notice.</summary>
    Keep,

    /// <summary>Novimize will not offer it, with a reason.</summary>
    Protected,

    /// <summary>Not in the policy file. Removable, but nobody has said otherwise.</summary>
    Unknown,
}

public sealed record DebloatEntry
{
    public string Id { get; init; } = string.Empty;
    public DebloatVerdict Verdict { get; init; } = DebloatVerdict.Unknown;
    public string Reason { get; init; } = string.Empty;
}

/// <summary>One installed Store package, with everything needed to judge removal.</summary>
public sealed record DebloatPackage
{
    public string Name { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;

    /// <summary>User, Machine, or Both.</summary>
    public string Scope { get; init; } = "User";
    public string Version { get; init; } = string.Empty;

    /// <summary>Frameworks are never offered — other packages are built on them.</summary>
    public bool IsFramework { get; init; }

    /// <summary>Windows' own marker: it refuses to remove these at all.</summary>
    public bool NonRemovable { get; init; }

    /// <summary>Packages that depend on this one. Removing it takes them with it.</summary>
    public List<string> DependedOnBy { get; init; } = new();

    public string? InstalledLocation { get; init; }

    /// <summary>True when a provisioned copy exists, so it can be registered again.</summary>
    public bool Provisioned { get; init; }

    public DebloatVerdict Verdict { get; init; } = DebloatVerdict.Unknown;
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// True when the engine refuses it. Frameworks, Windows' own non-removable
    /// flag and a waiting dependent are facts; the policy's "protected"
    /// verdict is enforced here too, so either way the row says no with a
    /// reason rather than offering a button that fails after the click.
    /// </summary>
    public bool EngineRefused { get; init; }

    /// <summary>What actually stops a removal, in one sentence.</summary>
    public string? RefusalReason { get; init; }

    public bool Removable => !EngineRefused;
}

public sealed record DebloatStatus
{
    public List<DebloatPackage> Packages { get; init; } = new();
    public int Removable { get; init; }
    public int ProtectedCount { get; init; }
    public int Frameworks { get; init; }
    public string Scope { get; init; } = "User";
    public string PolicyPath { get; init; } = string.Empty;
    public int PolicyEntries { get; init; }
    public string? Error { get; init; }
}

public record DebloatChange
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
