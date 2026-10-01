using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>
/// Risk classification for tweaks based on potential system impact.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RiskLevel
{
    Safe,
    Recommended,
    Optional,
    Experimental,
    Risky,
    Dangerous,
    Deprecated,
    Myth
}

/// <summary>
/// The type of operation used to apply/rollback a tweak.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TweakMethod
{
    Registry,
    Service,
    PowerCfg,
    NetSh,
    PowerShell,
    Dism,
    AppX,
    TaskScheduler,
    Script
}

/// <summary>
/// Current state of a tweak relative to the system.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TweakState
{
    NotApplied,
    PartiallyApplied,
    Applied,
    ConflictsDetected,
    DetectionFailed,
    Incompatible
}

/// <summary>
/// Defines a single optimization tweak with all metadata needed
/// for detection, application, rollback, and verification.
/// </summary>
public sealed class TweakDefinition
{
    /// <summary>Unique identifier, e.g. "network.tcpip.autoTuning"</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable name</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Longer description of what this tweak does</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Category for grouping, e.g. "network", "cpu", "privacy"</summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    /// <summary>Subcategory for finer grouping</summary>
    [JsonPropertyName("subcategory")]
    public string? Subcategory { get; init; }

    /// <summary>Risk level</summary>
    [JsonPropertyName("risk")]
    public RiskLevel Risk { get; init; } = RiskLevel.Optional;

    /// <summary>Evidence score 0-5 (0=no evidence, 5=Microsoft documented)</summary>
    [JsonPropertyName("evidence")]
    public int Evidence { get; init; }

    /// <summary>Minimum Windows build required (0 = any)</summary>
    [JsonPropertyName("minBuild")]
    public int MinBuild { get; init; }

    /// <summary>Maximum Windows build (0 = no limit)</summary>
    [JsonPropertyName("maxBuild")]
    public int MaxBuild { get; init; }

    /// <summary>Restrict to form factor: null=any, "desktop", "laptop"</summary>
    [JsonPropertyName("formFactor")]
    public string? FormFactor { get; init; }

    /// <summary>Required GPU vendor (null=any): "nvidia", "amd", "intel"</summary>
    [JsonPropertyName("gpuVendor")]
    public string? GpuVendor { get; init; }

    /// <summary>
    /// Power-plan setting alias this tweak needs (null=any), e.g. "TURBOBOOST".
    /// Machines whose active scheme does not expose the alias cannot apply the
    /// tweak, so it is filtered out instead of failing detection.
    /// </summary>
    [JsonPropertyName("requiresPowerSetting")]
    public string? RequiresPowerSetting { get; init; }

    /// <summary>Tags for filtering: "gaming", "privacy", "performance", etc.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = new();

    /// <summary>IDs of tweaks this conflicts with</summary>
    [JsonPropertyName("conflictsWith")]
    public List<string> ConflictsWith { get; init; } = new();

    /// <summary>IDs of tweaks that must be applied before this one</summary>
    [JsonPropertyName("dependsOn")]
    public List<string> DependsOn { get; init; } = new();

    /// <summary>IDs of protected services that must remain enabled</summary>
    [JsonPropertyName("protectedServices")]
    public List<string> ProtectedServices { get; init; } = new();

    /// <summary>The method used to apply this tweak</summary>
    [JsonPropertyName("method")]
    public TweakMethod Method { get; init; } = TweakMethod.Registry;

    /// <summary>The desired/target state value</summary>
    [JsonPropertyName("targetValue")]
    public string TargetValue { get; init; } = string.Empty;

    /// <summary>The default/original state value for rollback</summary>
    [JsonPropertyName("defaultValue")]
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>Method-specific parameters (registry path, service name, etc.)</summary>
    [JsonPropertyName("params")]
    public Dictionary<string, string> Params { get; init; } = new();

    /// <summary>Detection commands or logic identifiers</summary>
    [JsonPropertyName("detect")]
    public DetectionSpec Detection { get; init; } = new();

    /// <summary>Application commands/logic</summary>
    [JsonPropertyName("apply")]
    public OperationSpec Apply { get; init; } = new();

    /// <summary>Rollback commands/logic</summary>
    [JsonPropertyName("rollback")]
    public OperationSpec Rollback { get; init; } = new();

    /// <summary>Post-apply verification commands</summary>
    [JsonPropertyName("verify")]
    public DetectionSpec Verify { get; init; } = new();
}

/// <summary>
/// Specification for detecting the current state of a tweak.
/// </summary>
public sealed class DetectionSpec
{
    /// <summary>PowerShell command to detect state. Should output the current value.</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>Registry key path</summary>
    [JsonPropertyName("registryKey")]
    public string? RegistryKey { get; init; }

    /// <summary>Registry value name</summary>
    [JsonPropertyName("registryValue")]
    public string? RegistryValue { get; init; }

    /// <summary>Service name to check</summary>
    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    /// <summary>Expected value when tweak IS applied</summary>
    [JsonPropertyName("expectedApplied")]
    public string? ExpectedApplied { get; init; }

    /// <summary>Expected value when tweak is NOT applied (default state)</summary>
    [JsonPropertyName("expectedDefault")]
    public string? ExpectedDefault { get; init; }

    /// <summary>Regex pattern to extract value from command output</summary>
    [JsonPropertyName("extractPattern")]
    public string? ExtractPattern { get; init; }
}

/// <summary>
/// Specification for applying or rolling back a tweak.
/// </summary>
public sealed class OperationSpec
{
    /// <summary>PowerShell/script command to execute</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>Registry key path to write</summary>
    [JsonPropertyName("registryKey")]
    public string? RegistryKey { get; init; }

    /// <summary>Registry value name to write</summary>
    [JsonPropertyName("registryValue")]
    public string? RegistryValue { get; init; }

    /// <summary>Registry value data</summary>
    [JsonPropertyName("registryData")]
    public string? RegistryData { get; init; }

    /// <summary>Registry value type: DWORD, SZ, QWORD, BINARY, MULTI_SZ, EXPAND_SZ</summary>
    [JsonPropertyName("registryType")]
    public string? RegistryType { get; init; }

    /// <summary>Service name to modify</summary>
    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    /// <summary>Target service start type: auto, demand, disabled</summary>
    [JsonPropertyName("serviceStartType")]
    public string? ServiceStartType { get; init; }

    /// <summary>Whether to stop the service after changing start type</summary>
    [JsonPropertyName("serviceStop")]
    public bool ServiceStop { get; init; }

    /// <summary>Whether to start the service after changing start type</summary>
    [JsonPropertyName("serviceStart")]
    public bool ServiceStart { get; init; }
}
