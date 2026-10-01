using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>
/// Why a tweak that a profile's categories reach is not in that profile's
/// default set. Every exclusion is attributed, so a profile can answer "why is
/// this not being applied?" without the user reverse-engineering the filter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExclusionReason
{
    /// <summary>Nothing. The tweak is in the default set.</summary>
    None,

    /// <summary>Excluded from the default set by its own evidence score.</summary>
    LowEvidence,

    /// <summary>Above the profile's risk ceiling.</summary>
    HighRisk,

    /// <summary>Named in the profile's exclude list.</summary>
    ExcludedById,

    /// <summary>In a category the profile excludes.</summary>
    ExcludedCategory,

    /// <summary>In a category the profile does not include.</summary>
    OutsideCategories,

    /// <summary>The security guard refuses it on every machine.</summary>
    SecurityBlocked,
}

/// <summary>
/// One tweak a profile could apply, with the verdict on whether it does.
/// </summary>
public sealed class ProfileTweakVerdict
{
    [JsonPropertyName("tweak")]
    public TweakDefinition Tweak { get; init; } = new();

    /// <summary>True when applying the profile applies this without being asked.</summary>
    [JsonPropertyName("inDefaultSet")]
    public bool InDefaultSet { get; init; }

    /// <summary>
    /// True when the user can still opt in. Opt-in is offered for tweaks that
    /// failed only the evidence or risk bar — the profile's categories reach
    /// them, so they are the profile's business, but they are not something it
    /// does to you unasked. Anything held by an explicit exclusion, an excluded
    /// category, or the security guard is not offered at all: those are not
    /// judgement calls the profile is entitled to hand the user.
    /// </summary>
    [JsonPropertyName("optInAvailable")]
    public bool OptInAvailable { get; init; }

    /// <summary>Why this tweak is not in the default set.</summary>
    [JsonPropertyName("reason")]
    public ExclusionReason Reason { get; init; }

    /// <summary>The bar it failed, in words, for the UI.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}

/// <summary>
/// What applying a profile actually does on this machine, split into the set
/// it does unasked and the set a user has to opt into.
/// </summary>
public sealed class ProfileSelection
{
    [JsonPropertyName("profileId")]
    public string ProfileId { get; init; } = string.Empty;

    /// <summary>The policy, so the UI can show what it is working from.</summary>
    [JsonPropertyName("maxRisk")]
    public RiskLevel MaxRisk { get; init; }

    [JsonPropertyName("minEvidence")]
    public int MinEvidence { get; init; }

    /// <summary>Applied by the profile without the user asking for each one.</summary>
    [JsonPropertyName("defaultSet")]
    public List<ProfileTweakVerdict> DefaultSet { get; init; } = new();

    /// <summary>Reachable by the profile, but not applied unless chosen.</summary>
    [JsonPropertyName("optIn")]
    public List<ProfileTweakVerdict> OptIn { get; init; } = new();

    /// <summary>
    /// Best-supported first inside a bucket, so a list a user reads top to
    /// bottom leads with what is best evidenced. Ranked in the model so both
    /// the CLI and the UI get the same order without re-sorting.
    /// </summary>
    public static List<ProfileTweakVerdict> Rank(List<ProfileTweakVerdict> items)
        => items
            .OrderByDescending(v => v.Tweak.Evidence)
            .ThenBy(v => v.Tweak.Risk)
            .ThenBy(v => v.Tweak.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Outside the profile entirely — wrong category, explicitly excluded, or
    /// security-blocked. Kept so the UI can explain an absence, not to offer it.
    /// </summary>
    [JsonPropertyName("excluded")]
    public List<ProfileTweakVerdict> Excluded { get; init; } = new();

    /// <summary>
    /// Things to know about this profile on this machine that are not about a
    /// particular tweak — most importantly that the profile targets a form
    /// factor this machine is not.
    /// </summary>
    [JsonPropertyName("notices")]
    public List<string> Notices { get; init; } = new();

    /// <summary>
    /// The form factor this profile is written for, if it says. Null means any.
    /// </summary>
    [JsonPropertyName("targetFormFactor")]
    public string? TargetFormFactor { get; init; }
}