using System.Text.Json;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Tweaks;

/// <summary>
/// Loads and manages tweak definitions from JSON files.
/// Supports loading from a directory of JSON files or embedded resources.
/// </summary>
public sealed class TweakDatabase
{
    private readonly Dictionary<string, TweakDefinition> _tweaks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _tweaksDir;

    public IReadOnlyDictionary<string, TweakDefinition> Tweaks => _tweaks;

    public TweakDatabase(string? tweaksDir = null)
    {
        _tweaksDir = tweaksDir;
    }

    /// <summary>
    /// Load all tweak definitions from the configured directory.
    /// </summary>
    public async Task<int> LoadAsync()
    {
        if (_tweaksDir == null || !Directory.Exists(_tweaksDir))
            return 0;

        _tweaks.Clear();
        var count = 0;

        foreach (var file in Directory.GetFiles(_tweaksDir, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var defs = JsonSerializer.Deserialize<List<TweakDefinition>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (defs != null)
                {
                    foreach (var def in defs)
                    {
                        if (!string.IsNullOrEmpty(def.Id))
                        {
                            _tweaks[def.Id] = def;
                            count++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load tweaks from {file}: {ex.Message}");
            }
        }

        return count;
    }

    /// <summary>
    /// Load tweaks from an inline JSON string.
    /// </summary>
    public int LoadFromJson(string json)
    {
        var defs = JsonSerializer.Deserialize<List<TweakDefinition>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (defs == null) return 0;

        var count = 0;
        foreach (var def in defs)
        {
            if (!string.IsNullOrEmpty(def.Id))
            {
                _tweaks[def.Id] = def;
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Get a tweak by ID.
    /// </summary>
    public TweakDefinition? Get(string id)
        => _tweaks.TryGetValue(id, out var tweak) ? tweak : null;

    /// <summary>
    /// Get all tweaks in a category.
    /// </summary>
    public IReadOnlyList<TweakDefinition> GetByCategory(string category)
        => _tweaks.Values.Where(t =>
            string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList().AsReadOnly();

    /// <summary>
    /// Which of a profile's policy bars a tweak fails, or
    /// <see cref="ExclusionReason.None"/> when it passes.
    ///
    /// <para>
    /// Policy only — no hardware, no security guard. Those are decided
    /// separately because they are not the profile's opinion: a laptop-only
    /// tweak on a desktop is not something Battery Saver chose, and a
    /// security-blocked tweak is not something it is allowed to offer.
    /// </para>
    /// <para>
    /// Shared by <see cref="GetForProfile"/> and the profile selector so the
    /// two cannot answer the same question differently.
    /// </para>
    /// </summary>
    public static ExclusionReason ClassifyPolicy(OptimizationProfile profile, TweakDefinition t)
    {
        if (profile.ExcludeTweaks.Contains(t.Id, StringComparer.OrdinalIgnoreCase))
            return ExclusionReason.ExcludedById;

        if (profile.IncludeTweaks.Contains(t.Id, StringComparer.OrdinalIgnoreCase))
            return ExclusionReason.None;

        if (profile.ExcludeCategories.Contains(t.Category, StringComparer.OrdinalIgnoreCase))
            return ExclusionReason.ExcludedCategory;

        if (profile.IncludeCategories.Count > 0
            && !profile.IncludeCategories.Contains(t.Category, StringComparer.OrdinalIgnoreCase))
            return ExclusionReason.OutsideCategories;

        if (t.Risk > profile.MaxRisk)
            return ExclusionReason.HighRisk;

        if (t.Evidence < profile.MinEvidence)
            return ExclusionReason.LowEvidence;

        return ExclusionReason.None;
    }

    /// <summary>
    /// Get all tweaks for a specific profile.
    /// </summary>
    public IReadOnlyList<TweakDefinition> GetForProfile(OptimizationProfile profile)
    {
        return _tweaks.Values
            .Where(t => ClassifyPolicy(profile, t) == ExclusionReason.None)
            .ToList().AsReadOnly();
    }

    /// <summary>
    /// Tweaks the security guard refuses outright, on every machine and in
    /// every mode. These are a policy decision, not a compatibility question,
    /// so they are filtered alongside hardware gates rather than discovered
    /// one at a time as a failed apply.
    /// </summary>
    public static bool IsSecurityBlocked(TweakDefinition t)
        => t.Risk is RiskLevel.Myth or RiskLevel.Deprecated or RiskLevel.Dangerous;

    /// <summary>
    /// Whether this machine can actually run the tweak — build window, form
    /// factor, GPU vendor, and the power-plan alias the tweak drives.
    ///
    /// <para>
    /// One implementation, called from <see cref="FilterCompatible"/> and from
    /// the profile selector. These were two copies of the same four checks, and
    /// a second copy is exactly how the two-systems defect started.
    /// </para>
    /// </summary>
    public static bool IsHardwareCompatible(TweakDefinition t, SystemInfo systemInfo)
    {
        // Build check
        if (t.MinBuild > 0 && systemInfo.BuildNumber < t.MinBuild) return false;
        if (t.MaxBuild > 0 && systemInfo.BuildNumber > t.MaxBuild) return false;

        // Form factor check
        if (!string.IsNullOrEmpty(t.FormFactor)
            && !string.Equals(t.FormFactor, systemInfo.FormFactor.ToString(), StringComparison.OrdinalIgnoreCase))
            return false;

        // GPU vendor check
        if (!string.IsNullOrEmpty(t.GpuVendor)
            && !string.Equals(t.GpuVendor, systemInfo.GpuVendor, StringComparison.OrdinalIgnoreCase))
            return false;

        // Power setting check — the alias has to exist in the active
        // scheme, otherwise the tweak can never be applied on this machine.
        if (!string.IsNullOrEmpty(t.RequiresPowerSetting)
            && systemInfo.PowerSettings != null
            && !systemInfo.PowerSettings.Contains(t.RequiresPowerSetting))
            return false;

        return true;
    }

    /// <summary>
    /// Filter tweaks by system compatibility.
    /// </summary>
    public IReadOnlyList<TweakDefinition> FilterCompatible(
        IReadOnlyList<TweakDefinition> tweaks, SystemInfo systemInfo)
    {
        return tweaks.Where(t =>
        {
            // Security boundary — a blocked tweak is never attempted, so it
            // never appears in an apply run as a failure.
            if (IsSecurityBlocked(t)) return false;

            return IsHardwareCompatible(t, systemInfo);
        }).ToList().AsReadOnly();
    }

    /// <summary>
    /// Detect conflicts among a set of tweaks.
    /// </summary>
    public IReadOnlyList<(TweakDefinition A, TweakDefinition B, string Reason)> FindConflicts(
        IReadOnlyList<TweakDefinition> tweaks)
    {
        var conflicts = new List<(TweakDefinition, TweakDefinition, string)>();
        var idSet = new HashSet<string>(tweaks.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var tweak in tweaks)
        {
            foreach (var conflictId in tweak.ConflictsWith)
            {
                if (idSet.Contains(conflictId) && _tweaks.TryGetValue(conflictId, out var other))
                {
                    conflicts.Add((tweak, other, $"'{tweak.Id}' conflicts with '{conflictId}'"));
                }
            }
        }

        return conflicts.AsReadOnly();
    }

    /// <summary>
    /// Get all categories present in the database.
    /// </summary>
    public IReadOnlyList<string> GetCategories()
        => _tweaks.Values.Select(t => t.Category).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c).ToList().AsReadOnly();

    /// <summary>
    /// Total tweak count.
    /// </summary>
    public int Count => _tweaks.Count;
}
