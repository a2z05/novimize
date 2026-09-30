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
    /// Get all tweaks for a specific profile.
    /// </summary>
    public IReadOnlyList<TweakDefinition> GetForProfile(OptimizationProfile profile)
    {
        return _tweaks.Values.Where(t =>
        {
            // Exclude if in exclude list
            if (profile.ExcludeTweaks.Contains(t.Id)) return false;

            // Include if in explicit include list
            if (profile.IncludeTweaks.Contains(t.Id)) return true;

            // Exclude categories
            if (profile.ExcludeCategories.Contains(t.Category)) return false;

            // Include categories (if any specified, must match)
            if (profile.IncludeCategories.Count > 0 && !profile.IncludeCategories.Contains(t.Category))
                return false;

            // Risk check
            if (t.Risk > profile.MaxRisk) return false;

            // Evidence check
            if (t.Evidence < profile.MinEvidence) return false;

            return true;
        }).ToList().AsReadOnly();
    }

    /// <summary>
    /// Tweaks the security guard refuses outright, on every machine and in
    /// every mode. These are a policy decision, not a compatibility question,
    /// so they are filtered alongside hardware gates rather than discovered
    /// one at a time as a failed apply.
    /// </summary>
    private static bool IsSecurityBlocked(TweakDefinition t)
        => t.Risk is RiskLevel.Myth or RiskLevel.Deprecated or RiskLevel.Dangerous;

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

            // Build check
            if (t.MinBuild > 0 && systemInfo.BuildNumber < t.MinBuild) return false;
            if (t.MaxBuild > 0 && systemInfo.BuildNumber > t.MaxBuild) return false;

            // Form factor check
            if (!string.IsNullOrEmpty(t.FormFactor))
            {
                var ff = t.FormFactor.ToLowerInvariant();
                var actual = systemInfo.FormFactor.ToString().ToLowerInvariant();
                if (ff != actual) return false;
            }

            // GPU vendor check
            if (!string.IsNullOrEmpty(t.GpuVendor))
            {
                if (!string.Equals(t.GpuVendor, systemInfo.GpuVendor, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Power setting check — the alias has to exist in the active
            // scheme, otherwise the tweak can never be applied on this machine.
            if (!string.IsNullOrEmpty(t.RequiresPowerSetting) &&
                systemInfo.PowerSettings != null &&
                !systemInfo.PowerSettings.Contains(t.RequiresPowerSetting))
                return false;

            return true;
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
