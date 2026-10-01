using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>
/// What the engine decided to do with one requested tweak.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlanAction
{
    /// <summary>Safe to run, in this order.</summary>
    Apply,

    /// <summary>A tweak it depends on is not part of the run.</summary>
    MissingDependency,

    /// <summary>Another tweak in the run writes the same resource to a different value.</summary>
    Conflict,

    /// <summary>Blocked transitively — something it depends on was blocked above.</summary>
    BlockedByDependency,

    /// <summary>dependsOn forms a loop; no valid order exists.</summary>
    DependencyCycle
}

/// <summary>
/// A requested tweak names a dependency that the run does not contain.
/// </summary>
public sealed record MissingDependency
{
    /// <summary>The tweak that cannot run.</summary>
    [JsonPropertyName("tweakId")]
    public string TweakId { get; init; } = string.Empty;

    /// <summary>The dependency it expects.</summary>
    [JsonPropertyName("requiredId")]
    public string RequiredId { get; init; } = string.Empty;

    /// <summary>
    /// False when the dependency is not in the catalogue at all, which means
    /// adding it to the run cannot help — the definition is broken.
    /// </summary>
    [JsonPropertyName("requiredExists")]
    public bool RequiredExists { get; init; }
}

/// <summary>
/// Two tweaks in the same run that cannot both hold at once.
/// </summary>
public sealed record TweakConflict
{
    [JsonPropertyName("a")]
    public string A { get; init; } = string.Empty;

    [JsonPropertyName("b")]
    public string B { get; init; } = string.Empty;

    /// <summary>The resource both write to, e.g. <c>registry:HKCU\...::Value</c>.</summary>
    [JsonPropertyName("resource")]
    public string Resource { get; init; } = string.Empty;

    /// <summary>Human-readable explanation of why both cannot stand.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// One requested tweak and the engine's verdict on it.
/// </summary>
public sealed record PlanEntry
{
    [JsonPropertyName("tweakId")]
    public string TweakId { get; init; } = string.Empty;

    [JsonPropertyName("action")]
    public PlanAction Action { get; init; } = PlanAction.Apply;

    /// <summary>Why it was held back. Empty when <see cref="Action"/> is Apply.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    /// <summary>Tweak IDs responsible for the block (dependencies or the conflicting peer).</summary>
    [JsonPropertyName("blockedBy")]
    public IReadOnlyList<string> BlockedBy { get; init; } = Array.Empty<string>();

    /// <summary>Position within the resolved run. -1 when the tweak does not run.</summary>
    [JsonPropertyName("order")]
    public int Order { get; init; } = -1;
}

/// <summary>
/// The engine's decision for an entire batch: what runs, in what order, and
/// what is held back with a reason. Produced before anything is written to the
/// system, so both the CLI and the UI can show it as a preview.
/// </summary>
public sealed class BatchPlan
{
    [JsonPropertyName("entries")]
    public List<PlanEntry> Entries { get; init; } = new();

    [JsonPropertyName("missingDependencies")]
    public List<MissingDependency> MissingDependencies { get; init; } = new();

    [JsonPropertyName("conflicts")]
    public List<TweakConflict> Conflicts { get; init; } = new();

    /// <summary>Tweak IDs that will run, in execution order.</summary>
    [JsonPropertyName("orderedTweakIds")]
    public List<string> OrderedTweakIds { get; init; } = new();

    /// <summary>Everything requested, whether or not it will run.</summary>
    [JsonPropertyName("requestedCount")]
    public int RequestedCount { get; init; }

    public int ApplicableCount => OrderedTweakIds.Count;

    public int BlockedCount => Entries.Count(e => e.Action != PlanAction.Apply);

    public bool HasIssues => MissingDependencies.Count > 0 || Conflicts.Count > 0 || BlockedCount > 0;
}

