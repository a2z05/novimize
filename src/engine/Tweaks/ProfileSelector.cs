using WinOpt.Core.Models;

namespace WinOpt.Engine.Tweaks;

/// <summary>
/// Decides, for one profile on one machine, which tweaks it applies unasked,
/// which it merely reaches, and which it never touches — and says why for
/// every one of them.
///
/// <para>
/// This exists because a profile is a policy, and a policy that silently drops
/// things is indistinguishable from a policy that applied them. Battery Saver
/// declares <c>MaxRisk = Safe</c>, so <c>cpu-power.turbo.disabled</c> is never
/// part of it — but its JSON once claimed otherwise, and nothing in the UI said
/// why. The reason is now the product.
/// </para>
/// </summary>
public sealed class ProfileSelector
{
    private readonly TweakDatabase _database;

    public ProfileSelector(TweakDatabase database) => _database = database;

    /// <summary>
    /// Build the selection for a profile. <paramref name="systemInfo"/> gates
    /// on hardware — a tweak the machine cannot take is out of every bucket,
    /// because offering to opt into it would be offering something that fails.
    /// </summary>
    public ProfileSelection Select(OptimizationProfile profile, SystemInfo systemInfo)
    {
        var defaultSet = new List<ProfileTweakVerdict>();
        var optIn = new List<ProfileTweakVerdict>();
        var excluded = new List<ProfileTweakVerdict>();
        var notices = new List<string>();

        // Form factor was declared on profiles and read by nothing, so a
        // laptop-only profile reported itself as available on a desktop and
        // applied its power policy to a machine that has no battery to spare.
        // It is read here, and a mismatch is stated rather than acted on: a
        // warning the user can ignore is better than a silent assumption, and
        // refusing outright would take Battery Saver away from a desktop that
        // wants it.
        if (!string.IsNullOrEmpty(profile.FormFactor)
            && !string.Equals(profile.FormFactor, systemInfo.FormFactor.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            notices.Add(
                $"This profile is written for {profile.FormFactor} machines. " +
                $"You are on a {systemInfo.FormFactor}. " +
                "It will still apply — read the list below before you agree.");
        }

        if (profile.MinEvidence > 0)
        {
            var weak = _database.Tweaks.Values
                .Where(t => TweakDatabase.IsHardwareCompatible(t, systemInfo)
                         && !TweakDatabase.IsSecurityBlocked(t)
                         && TweakDatabase.ClassifyPolicy(profile, t) == ExclusionReason.LowEvidence)
                .ToList();

            if (weak.Count > 0)
                notices.Add(
                    $"{weak.Count} tweak(s) this profile's categories reach are held back for low evidence " +
                    $"(below {profile.MinEvidence}/5) and are listed under \"available if you want them\".");
        }

        foreach (var tweak in _database.Tweaks.Values)
        {
            // Hardware gates first. A tweak this machine cannot run is not a
            // policy decision, so it gets no bucket and no opinion.
            if (!TweakDatabase.IsHardwareCompatible(tweak, systemInfo)) continue;

            var verdict = Classify(profile, tweak);
            if (verdict.Reason == ExclusionReason.None)
                defaultSet.Add(verdict);
            else if (verdict.OptInAvailable)
                optIn.Add(verdict);
            else
                excluded.Add(verdict);
        }

        // Best-supported first inside each bucket, so a list a user reads top
        // to bottom leads with what is best evidenced.
        return new ProfileSelection
        {
            ProfileId = profile.Id,
            MaxRisk = profile.MaxRisk,
            MinEvidence = profile.MinEvidence,
            TargetFormFactor = profile.FormFactor,
            DefaultSet = ProfileSelection.Rank(defaultSet),
            OptIn = ProfileSelection.Rank(optIn),
            Excluded = ProfileSelection.Rank(excluded),
            Notices = notices,
        };
    }

    /// <summary>
    /// The set a profile applies without being asked. This is the input to
    /// <c>apply --profile</c> and must stay identical to what
    /// <see cref="TweakDatabase.GetForProfile"/> selects, or the profile page
    /// and the run would disagree.
    /// </summary>
    public IReadOnlyList<TweakDefinition> DefaultSet(OptimizationProfile profile, SystemInfo systemInfo)
        => Select(profile, systemInfo).DefaultSet.Select(v => v.Tweak).ToList().AsReadOnly();

    private ProfileTweakVerdict Classify(OptimizationProfile profile, TweakDefinition tweak)
    {
        ProfileTweakVerdict Make(bool inDefault, bool optIn, ExclusionReason reason, string? detail)
            => new() { Tweak = tweak, InDefaultSet = inDefault, OptInAvailable = optIn, Reason = reason, Detail = detail };

        // The security guard is absolute and comes before policy: a tweak the
        // app refuses to run is not an opt-in choice, it is off the table.
        if (TweakDatabase.IsSecurityBlocked(tweak))
            return Make(false, false, ExclusionReason.SecurityBlocked,
                "The security guard refuses this on every machine.");

        // The policy bars themselves come from TweakDatabase, so this view and
        // GetForProfile can never disagree about which tweaks a profile has.
        var reason = TweakDatabase.ClassifyPolicy(profile, tweak);

        switch (reason)
        {
            case ExclusionReason.None:
                return Make(true, false, ExclusionReason.None, null);

            // Two soft bars, and the only two that produce an opt-in offer. The
            // profile's categories reach these tweaks; it just is not entitled
            // to apply them on the user's behalf.
            case ExclusionReason.LowEvidence:
                return Make(false, true, ExclusionReason.LowEvidence,
                    $"Evidence {tweak.Evidence}/5 is below this profile's minimum of {profile.MinEvidence}. " +
                    "It is listed, not recommended.");

            case ExclusionReason.HighRisk:
                return Make(false, true, ExclusionReason.HighRisk,
                    $"{tweak.Risk} risk is above this profile's ceiling of {profile.MaxRisk}.");

            // An explicit exclusion, a category the profile does not want, or
            // one it names outright: the author's decision, not the user's to
            // override from a profile screen.
            case ExclusionReason.ExcludedById:
                return Make(false, false, ExclusionReason.ExcludedById,
                    $"'{profile.Id}' excludes this tweak by name.");

            case ExclusionReason.ExcludedCategory:
                return Make(false, false, ExclusionReason.ExcludedCategory,
                    $"'{profile.Id}' excludes the '{tweak.Category}' category.");

            default:
                return Make(false, false, ExclusionReason.OutsideCategories,
                    $"Not in '{profile.Id}'s categories.");
        }
    }
}