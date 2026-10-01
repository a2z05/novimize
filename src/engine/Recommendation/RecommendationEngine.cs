using WinOpt.Core.Models;
using WinOpt.Engine.Logging;
using WinOpt.Engine.Tweaks;

namespace WinOpt.Engine.Recommendation;

/// <summary>
/// Scores and ranks tweaks based on system info, evidence, risk,
/// and profile alignment. Produces actionable recommendations.
/// </summary>
public sealed class RecommendationEngine
{
    private readonly TweakDatabase _database;
    private readonly WinOptLogger _logger;

    public RecommendationEngine(TweakDatabase database, WinOptLogger logger)
    {
        _database = database;
        _logger = logger;
    }

    /// <summary>
    /// Generate ranked recommendations for a system profile.
    /// </summary>
    public List<TweakRecommendation> Recommend(SystemInfo systemInfo, string? profileId = null)
    {
        var allTweaks = _database.Tweaks.Values.ToList();
        var compatible = _database.FilterCompatible(allTweaks, systemInfo);

        // If a profile is specified, further filter
        OptimizationProfile? profile = null;
        if (!string.IsNullOrEmpty(profileId))
            profile = BuiltInProfiles.All.FirstOrDefault(p => p.Id == profileId);

        if (profile != null)
        {
            compatible = _database.FilterCompatible(
                _database.GetForProfile(profile).ToList(), systemInfo);
        }

        // The +10 profile-alignment boost has to come from the same selection
        // apply would make. It used to read a per-tweak `profiles` array, which
        // nothing else consulted: 53 of 58 of those arrays disagreed with where
        // the engine actually put the tweak, so the boost was scoring a
        // membership that did not exist.
        var inProfile = profile == null
            ? null
            : _database.GetForProfile(profile).Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recommendations = compatible
            .Select(t => Score(t, systemInfo, inProfile?.Contains(t.Id) == true))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Tweak.Evidence)
            .ToList();

        _logger.Info($"Generated {recommendations.Count} recommendations for tier {systemInfo.OverallTier}", "recommend");

        return recommendations;
    }

    /// <summary>
    /// Score a single tweak for a given system.
    /// Score 0-100. Higher = more recommended.
    /// </summary>
    public TweakRecommendation Score(TweakDefinition tweak, SystemInfo systemInfo, bool inProfile = false)
    {
        double score = 0;
        var reasons = new List<string>();

        // === Evidence weight (0-25 points) ===
        score += tweak.Evidence * 5.0;
        if (tweak.Evidence >= 5) reasons.Add("Microsoft documented");

        // === Risk penalty (-20 to +10) ===
        score += tweak.Risk switch
        {
            RiskLevel.Safe => 10,
            RiskLevel.Recommended => 7,
            RiskLevel.Optional => 3,
            RiskLevel.Experimental => -5,
            RiskLevel.Risky => -15,
            RiskLevel.Dangerous => -20,
            RiskLevel.Deprecated => -20,
            RiskLevel.Myth => -25,
            _ => 0
        };

        // === Hardware tier alignment (+/- 15) ===
        var tierBoost = tweak switch
        {
            // Potato PC tweaks score higher on low-end systems
            _ when tweak.Tags.Contains("cleanup") && systemInfo.OverallTier <= HardwareTier.Low => +12,
            _ when tweak.Tags.Contains("cleanup") && systemInfo.OverallTier >= HardwareTier.High => +2,

            // Gaming tweaks score higher on mid+ systems
            _ when tweak.Tags.Contains("gaming") && systemInfo.OverallTier >= HardwareTier.Mid => +10,
            _ when tweak.Tags.Contains("gaming") && systemInfo.OverallTier < HardwareTier.Mid => +3,

            // Performance tweaks on high-end = more value
            _ when tweak.Tags.Contains("performance") && systemInfo.OverallTier >= HardwareTier.High => +8,
            _ when tweak.Tags.Contains("performance") && systemInfo.OverallTier <= HardwareTier.Low => +12,

            // Privacy tweaks are always relevant
            _ when tweak.Tags.Contains("privacy") => +8,

            // Visual effects — bigger impact on low-end
            _ when tweak.Tags.Contains("visual-effects") && systemInfo.OverallTier <= HardwareTier.Low => +10,
            _ when tweak.Tags.Contains("visual-effects") && systemInfo.OverallTier >= HardwareTier.High => +3,

            _ => 0
        };
        score += tierBoost;
        if (tierBoost > 5) reasons.Add($"High impact for {systemInfo.OverallTier} tier system");

        // === Profile alignment (+/- 10) ===
        if (inProfile)
        {
            score += 10;
            reasons.Add($"Selected by this profile");
        }

        // === Laptop-specific adjustments ===
        if (systemInfo.IsLaptop)
        {
            if (tweak.FormFactor == "laptop") { score += 8; reasons.Add("Laptop-specific"); }
            if (tweak.Tags.Contains("battery")) { score += 10; reasons.Add("Battery optimization"); }
            if (tweak.Id.Contains("power") && tweak.TargetValue.Contains("100")) { score -= 5; } // Full performance on battery = bad
        }

        // === Desktop-specific adjustments ===
        if (systemInfo.FormFactor == FormFactor.Desktop)
        {
            if (tweak.FormFactor == "desktop") { score += 5; }
            if (tweak.Id.Contains("battery")) { score -= 5; } // Battery tweaks on desktop = irrelevant
        }

        // === GPU vendor alignment ===
        if (!string.IsNullOrEmpty(tweak.GpuVendor))
        {
            if (string.Equals(tweak.GpuVendor, systemInfo.GpuVendor, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
                reasons.Add($"Matches {systemInfo.GpuVendor} GPU");
            }
            else
            {
                score -= 10; // Wrong vendor = don't apply
            }
        }

        // === Minimum score floor ===
        score = Math.Max(0, Math.Min(100, score));

        return new TweakRecommendation
        {
            Tweak = tweak,
            Score = Math.Round(score, 1),
            Reason = reasons.Count > 0 ? string.Join("; ", reasons) : "General recommendation",
            Priority = score switch
            {
                >= 70 => RecommendationPriority.High,
                >= 40 => RecommendationPriority.Medium,
                >= 20 => RecommendationPriority.Low,
                _ => RecommendationPriority.Info
            }
        };
    }

    /// <summary>
    /// Get the top N recommendations.
    /// </summary>
    public List<TweakRecommendation> TopN(SystemInfo systemInfo, int n, string? profileId = null)
        => Recommend(systemInfo, profileId).Take(n).ToList();

    /// <summary>
    /// Get recommendations grouped by category.
    /// </summary>
    public Dictionary<string, List<TweakRecommendation>> ByCategory(SystemInfo systemInfo, string? profileId = null)
    {
        return Recommend(systemInfo, profileId)
            .GroupBy(r => r.Tweak.Category)
            .ToDictionary(g => g.Key, g => g.ToList());
    }
}

public sealed class TweakRecommendation
{
    public TweakDefinition Tweak { get; init; } = default!;
    public double Score { get; init; }
    public string Reason { get; init; } = string.Empty;
    public RecommendationPriority Priority { get; init; }
}

public enum RecommendationPriority
{
    High,
    Medium,
    Low,
    Info
}
