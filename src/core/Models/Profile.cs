using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>
/// Optimization profile — a named configuration that selects
/// which tweaks to apply based on use case and hardware.
/// </summary>
public sealed class OptimizationProfile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "⚙️";

    /// <summary>Minimum hardware tier to recommend this profile (null = any)</summary>
    [JsonPropertyName("minTier")]
    public HardwareTier? MinTier { get; init; }

    /// <summary>Maximum hardware tier for this profile (null = any)</summary>
    [JsonPropertyName("maxTier")]
    public HardwareTier? MaxTier { get; init; }

    /// <summary>Restrict to form factor (null = any)</summary>
    [JsonPropertyName("formFactor")]
    public string? FormFactor { get; init; }

    /// <summary>Restrict to GPU vendor (null = any)</summary>
    [JsonPropertyName("gpuVendor")]
    public string? GpuVendor { get; init; }

    /// <summary>Categories of tweaks to include</summary>
    [JsonPropertyName("includeCategories")]
    public List<string> IncludeCategories { get; init; } = new();

    /// <summary>Categories to exclude</summary>
    [JsonPropertyName("excludeCategories")]
    public List<string> ExcludeCategories { get; init; } = new();

    /// <summary>Specific tweak IDs to include (overrides category matching)</summary>
    [JsonPropertyName("includeTweaks")]
    public List<string> IncludeTweaks { get; init; } = new();

    /// <summary>Specific tweak IDs to exclude</summary>
    [JsonPropertyName("excludeTweaks")]
    public List<string> ExcludeTweaks { get; init; } = new();

    /// <summary>Maximum risk level allowed in this profile</summary>
    [JsonPropertyName("maxRisk")]
    public RiskLevel MaxRisk { get; init; } = RiskLevel.Safe;

    /// <summary>Minimum evidence score required</summary>
    [JsonPropertyName("minEvidence")]
    public int MinEvidence { get; init; } = 3;

    /// <summary>Whether auto-optimize mode is allowed for this profile</summary>
    [JsonPropertyName("allowAutoOptimize")]
    public bool AllowAutoOptimize { get; init; } = true;
}

/// <summary>
/// Built-in profiles matching the user's requirements.
/// </summary>
public static class BuiltInProfiles
{
    public static readonly List<OptimizationProfile> All = new()
    {
        new OptimizationProfile
        {
            Id = "gaming",
            Name = "Gaming",
            Description = "Maximum gaming performance: aggressive CPU/GPU tuning, low latency network, Game Mode, reduced input lag",
            Icon = "🎮",
            MinTier = HardwareTier.Mid,
            IncludeCategories = new() { "cpu", "gpu", "network", "gaming", "visual-effects", "power" },
            ExcludeCategories = new() { "privacy" },
            MaxRisk = RiskLevel.Optional,
            MinEvidence = 3,
            AllowAutoOptimize = false // Gaming requires manual confirmation
        },
        new OptimizationProfile
        {
            Id = "potato-pc",
            Name = "Potato PC",
            Description = "Ultra-conservative optimization for very old/low-end hardware: maximum background reduction, minimal visual effects",
            Icon = "🥔",
            MaxTier = HardwareTier.Mid,
            IncludeCategories = new() { "services", "startup", "visual-effects", "cleanup", "apps" },
            MaxRisk = RiskLevel.Safe,
            MinEvidence = 4,
            AllowAutoOptimize = true
        },
        new OptimizationProfile
        {
            Id = "office",
            Name = "Office / Productivity",
            Description = "Optimized for Office apps, browsers, and multitasking: snappy UI, fast boot, reliable updates",
            Icon = "💼",
            IncludeCategories = new() { "services", "startup", "visual-effects", "memory", "cleanup" },
            ExcludeCategories = new() { "gaming" },
            MaxRisk = RiskLevel.Safe,
            MinEvidence = 4,
            AllowAutoOptimize = true
        },
        new OptimizationProfile
        {
            Id = "daily",
            Name = "Daily Driver",
            Description = "Balanced optimization for everyday use: good performance without breaking anything",
            Icon = "🖥️",
            MaxRisk = RiskLevel.Safe,
            MinEvidence = 4,
            AllowAutoOptimize = true
        },
        new OptimizationProfile
        {
            Id = "streaming",
            Name = "Streaming",
            Description = "Optimized for OBS/streaming: CPU encoding priority, network upload focus, minimal background processes",
            Icon = "📺",
            MinTier = HardwareTier.Mid,
            IncludeCategories = new() { "cpu", "gpu", "network", "services", "startup", "power" },
            MaxRisk = RiskLevel.Optional,
            MinEvidence = 3,
            AllowAutoOptimize = false
        },
        new OptimizationProfile
        {
            Id = "developer",
            Name = "Developer",
            Description = "Optimized for development: Docker/WSL performance, fast builds, maximum RAM availability",
            Icon = "👨‍💻",
            IncludeCategories = new() { "memory", "storage", "network", "services", "startup" },
            ExcludeCategories = new() { "privacy", "cleanup" }, // Don't remove dev tools
            MaxRisk = RiskLevel.Optional,
            MinEvidence = 3,
            AllowAutoOptimize = false
        },
        new OptimizationProfile
        {
            Id = "workstation",
            Name = "Workstation",
            Description = "High-end workstation for CAD/rendering/VMs: maximum resources, no power throttling",
            Icon = "🖥️",
            MinTier = HardwareTier.High,
            IncludeCategories = new() { "cpu", "gpu", "memory", "storage", "power", "network" },
            MaxRisk = RiskLevel.Optional,
            MinEvidence = 3,
            AllowAutoOptimize = false
        },
        new OptimizationProfile
        {
            Id = "battery-saver",
            Name = "Battery Saver",
            Description = "Maximum battery life: aggressive power saving, reduced performance, longer uptime",
            Icon = "🔋",
            FormFactor = "laptop",
            IncludeCategories = new() { "power", "services", "startup", "visual-effects" },
            MaxRisk = RiskLevel.Safe,
            MinEvidence = 4,
            AllowAutoOptimize = true
        }
    };

    /// <summary>
    /// Auto-select the best profile based on system info.
    /// </summary>
    public static OptimizationProfile AutoSelect(SystemInfo info)
    {
        // Battery saver for laptops on AC
        if (info.IsLaptop && !info.OnAcPower)
            return All.First(p => p.Id == "battery-saver");

        // Potato PC for very low-end
        if (info.OverallTier == HardwareTier.VeryLow)
            return All.First(p => p.Id == "potato-pc");

        // Workstation for ultra-high-end desktops
        if (info.OverallTier == HardwareTier.Ultra && info.FormFactor == FormFactor.Desktop)
            return All.First(p => p.Id == "workstation");

        // Default to daily
        return All.First(p => p.Id == "daily");
    }
}
