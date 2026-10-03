using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>One power setting as the machine has it now.</summary>
public sealed record PowerSetting
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>What it is set to, already in its own unit.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>The same value on battery, when the plan keeps two.</summary>
    public string? BatteryValue { get; init; }

    public string Unit { get; init; } = string.Empty;

    /// <summary>One sentence on what moving it costs.</summary>
    public string? Note { get; init; }

    /// <summary>True when the plan does not expose this setting at all.</summary>
    public bool Unavailable { get; init; }

    /// <summary>Set when another part of Novimize already manages this.</summary>
    public string? ExistingTweak { get; init; }
}

/// <summary>A power plan Windows actually has, with its real GUID.</summary>
public sealed record PowerPlan
{
    public string Guid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Active { get; init; }

    /// <summary>True for the well-known schemes rather than one the user copied.</summary>
    public bool BuiltIn { get; init; }
}

public sealed record BatteryState
{
    public bool Present { get; init; }
    public int? Percent { get; init; }
    public string? Status { get; init; }

    /// <summary>Estimated minutes left, as the battery reports it.</summary>
    public int? MinutesRemaining { get; init; }
    public bool OnAc { get; init; } = true;
}

/// <summary>Everything the Power Center reads in one pass.</summary>
public sealed record PowerStatus
{
    public List<PowerPlan> Plans { get; init; } = new();
    public string ActivePlan { get; init; } = string.Empty;
    public string ActivePlanGuid { get; init; } = string.Empty;
    public List<PowerSetting> Settings { get; init; } = new();
    public BatteryState Battery { get; init; } = new();

    /// <summary>Desktop, laptop, tablet — from the chassis, not from a guess.</summary>
    public string FormFactor { get; init; } = "Unknown";
    public bool IsLaptop { get; init; }

    /// <summary>Where the plan stood before Novimize last changed it, if it knows.</summary>
    public string? PreviousPlanGuid { get; init; }
    public string? PreviousPlanName { get; init; }

    /// <summary>True when Ultimate Performance exists as its own scheme.</summary>
    public bool UltimateAvailable { get; init; }
    public string? Error { get; init; }
}

public record PowerChange
{
    public string Action { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool Unchanged { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Affected { get; init; }
    public string? Log { get; init; }

    /// <summary>Command lines, in order. Shown before anything runs.</summary>
    public List<string> Preview { get; init; } = new();

    /// <summary>Set when the operation wants a restart — none of these do.</summary>
    public bool RestartRequired { get; init; }
}
