using System.Text.Json;
using WinOpt.Core.Models;
using WinOpt.Engine.Tweaks;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Tests for profile selection: which tweaks a profile applies unasked, which
/// it merely reaches, and why anything is left out.
/// </summary>
public class ProfileSelectorTests
{
    private static string TweakDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tweaks");

    private static TweakDatabase Catalogue()
    {
        var db = new TweakDatabase(TweakDir);
        db.LoadAsync().GetAwaiter().GetResult();
        return db;
    }

    /// <summary>
    /// A machine that gates nothing, so the hardware filter is not what is
    /// under test. <paramref name="build"/> is adjustable for the build-window
    /// cases.
    /// </summary>
    private static SystemInfo Machine(int build = 26200) => new()
    {
        BuildNumber = build,
        FormFactor = FormFactor.Desktop,
        GpuVendor = "nvidia",
        OverallTier = HardwareTier.Mid,
    };

    private static OptimizationProfile Profile(
        string id = "test",
        RiskLevel maxRisk = RiskLevel.Optional,
        int minEvidence = 3,
        string[]? includeCategories = null,
        string[]? excludeCategories = null,
        string[]? excludeTweaks = null,
        string? formFactor = null) => new()
    {
        Id = id,
        Name = id,
        IncludeCategories = includeCategories?.ToList() ?? new List<string> { "test" },
        ExcludeCategories = excludeCategories?.ToList() ?? new List<string>(),
        ExcludeTweaks = excludeTweaks?.ToList() ?? new List<string>(),
        MaxRisk = maxRisk,
        MinEvidence = minEvidence,
        FormFactor = formFactor,
    };

    private static TweakDefinition Tweak(
        string id,
        RiskLevel risk = RiskLevel.Safe,
        int evidence = 5,
        string category = "test",
        int minBuild = 0,
        string? gpuVendor = null,
        string? formFactor = null) => new()
    {
        Id = id,
        Name = id,
        Category = category,
        Risk = risk,
        Evidence = evidence,
        MinBuild = minBuild,
        GpuVendor = gpuVendor,
        FormFactor = formFactor,
        Method = TweakMethod.Registry,
    };

    private static ProfileSelection Select(OptimizationProfile profile, TweakDatabase db, SystemInfo? machine = null)
        => new ProfileSelector(db).Select(profile, machine ?? Machine());

    private static TweakDatabase DatabaseOf(params TweakDefinition[] tweaks)
    {
        var db = new TweakDatabase();
        db.LoadFromJson(JsonSerializer.Serialize(tweaks));
        return db;
    }

    // --- the default set is what apply will actually do ---

    [Fact]
    public void DefaultSet_IsExactlyWhatGetForProfileSelects()
    {
        // The invariant the whole feature rests on: what the profile page calls
        // "applied by default" and what `apply --profile` runs must be the same
        // set. If these two ever diverge, the app shows a list it does not apply.
        var db = Catalogue();
        var selector = new ProfileSelector(db);
        var machine = Machine();

        foreach (var profile in BuiltInProfiles.All)
        {
            var defaultSet = selector.DefaultSet(profile, machine).Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // The reference implementation: the filter apply has always used.
            var reference = db.FilterCompatible(db.GetForProfile(profile), machine)
                .Select(t => t.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Equal(reference, defaultSet);
        }
    }

    [Fact]
    public void Catalogue_EveryProfileHasANonEmptyDefaultSet()
    {
        // A profile that selects nothing on this machine is either a policy bug
        // or a hardware gate nobody checked. Either way the user sees an empty
        // list and an Apply button that does nothing.
        var db = Catalogue();
        var selector = new ProfileSelector(db);
        var machine = Machine();

        foreach (var profile in BuiltInProfiles.All)
        {
            var selection = selector.Select(profile, machine);
            Assert.True(selection.DefaultSet.Count > 0,
                $"Profile '{profile.Id}' would apply nothing on this machine.");
        }
    }

    [Fact]
    public void EveryTweakIsAccountedForExactlyOnce()
    {
        // Nothing may vanish between the catalogue and the explanation. A tweak
        // that is in no bucket is a tweak the user cannot find the answer for.
        var db = Catalogue();
        var selector = new ProfileSelector(db);

        foreach (var profile in BuiltInProfiles.All)
        {
            var selection = selector.Select(profile, Machine());
            var seen = selection.DefaultSet.Concat(selection.OptIn).Concat(selection.Excluded)
                .Select(v => v.Tweak.Id).ToList();

            Assert.Equal(seen.Count, seen.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var hardwareCompatible = db.Tweaks.Values
                .Where(t => TweakDatabase.IsHardwareCompatible(t, Machine()))
                .Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Equal(hardwareCompatible.OrderBy(x => x),
                seen.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }
    }

    // --- the split ---

    [Fact]
    public void LowEvidence_LeavesTheDefaultSetButStaysReachable()
    {
        var db = DatabaseOf(Tweak("a.good"), Tweak("a.shaky", evidence: 1));
        var selection = Select(Profile(minEvidence: 3), db);

        Assert.Equal(new[] { "a.good" }, selection.DefaultSet.Select(v => v.Tweak.Id));
        Assert.Equal(new[] { "a.shaky" }, selection.OptIn.Select(v => v.Tweak.Id));

        var verdict = selection.OptIn[0];
        Assert.Equal(ExclusionReason.LowEvidence, verdict.Reason);
        Assert.Contains("Evidence 1/5", verdict.Detail);
    }

    [Fact]
    public void HighRisk_LeavesTheDefaultSetButStaysReachable()
    {
        var db = DatabaseOf(
            Tweak("a.safe", RiskLevel.Safe),
            Tweak("a.careful", RiskLevel.Experimental));

        var selection = Select(Profile(maxRisk: RiskLevel.Optional), db);

        Assert.Equal(new[] { "a.safe" }, selection.DefaultSet.Select(v => v.Tweak.Id));
        Assert.Equal(new[] { "a.careful" }, selection.OptIn.Select(v => v.Tweak.Id));
        Assert.Equal(ExclusionReason.HighRisk, selection.OptIn[0].Reason);
    }

    [Fact]
    public void Risky_ReachesOptInRatherThanVanishing()
    {
        // A risky tweak inside the profile's categories is exactly what "listed,
        // not recommended" means. It must not disappear — the user is allowed
        // to make this call themselves.
        var db = DatabaseOf(Tweak("a.risky", RiskLevel.Risky));
        var selection = Select(Profile(maxRisk: RiskLevel.Safe), db);

        Assert.Empty(selection.DefaultSet);
        var verdict = Assert.Single(selection.OptIn);
        Assert.Equal(ExclusionReason.HighRisk, verdict.Reason);
    }

    // --- things that are not opt-in ---

    [Theory]
    [InlineData(RiskLevel.Dangerous)]
    [InlineData(RiskLevel.Deprecated)]
    [InlineData(RiskLevel.Myth)]
    public void SecurityBlocked_IsNeverOfferedAtAll(RiskLevel risk)
    {
        // The security guard is absolute. Presenting these as a choice would be
        // asking the user to tick a box next to something the app has already
        // decided it will not run.
        var db = DatabaseOf(Tweak("a.blocked", risk, evidence: 5));
        var selection = Select(Profile(maxRisk: RiskLevel.Myth, minEvidence: 0), db);

        Assert.Empty(selection.DefaultSet);
        Assert.Empty(selection.OptIn);
        Assert.Equal(ExclusionReason.SecurityBlocked, Assert.Single(selection.Excluded).Reason);
    }

    [Fact]
    public void ExcludedById_IsNotOfferedAsOptIn()
    {
        // A named exclusion is the profile author's decision. Letting the user
        // pick it back would invert who the policy belongs to.
        var db = DatabaseOf(Tweak("a.pinned"), Tweak("a.pinnedOff"));
        var selection = Select(Profile(excludeTweaks: new[] { "a.pinnedOff" }), db);

        Assert.Equal(new[] { "a.pinned" }, selection.DefaultSet.Select(v => v.Tweak.Id));
        Assert.Empty(selection.OptIn);
        Assert.Equal(ExclusionReason.ExcludedById, Assert.Single(selection.Excluded).Reason);
    }

    [Fact]
    public void ExcludedCategory_IsNotOfferedAsOptIn()
    {
        var db = DatabaseOf(
            Tweak("a.keep", category: "test"),
            Tweak("b.drop", category: "test", evidence: 1));

        var selection = Select(Profile(excludeCategories: new[] { "test" }), db);

        Assert.Empty(selection.DefaultSet);
        Assert.Empty(selection.OptIn);
        Assert.All(selection.Excluded, v => Assert.Equal(ExclusionReason.ExcludedCategory, v.Reason));
    }

    [Fact]
    public void OutsideCategories_IsNotOfferedAsOptIn()
    {
        var db = DatabaseOf(
            Tweak("a.in", category: "test"),
            Tweak("b.out", category: "other", evidence: 1, risk: RiskLevel.Risky));

        var selection = Select(Profile(includeCategories: new[] { "test" }), db);

        Assert.Equal(new[] { "a.in" }, selection.DefaultSet.Select(v => v.Tweak.Id));
        Assert.Empty(selection.OptIn);
        Assert.Equal(ExclusionReason.OutsideCategories, Assert.Single(selection.Excluded).Reason);
    }

    // --- hardware gates ---

    [Fact]
    public void HardwareIncompatible_IsInNoBucketAtAll()
    {
        // Offering to opt into a tweak this machine cannot take is offering
        // something that will fail, so hardware gating runs before policy.
        var db = DatabaseOf(
            Tweak("a.ok"),
            Tweak("a.tooNew", evidence: 5, minBuild: 99999),
            Tweak("a.nvidiaOnly", gpuVendor: "amd"),
            Tweak("a.laptopOnly", formFactor: "laptop"));

        var selection = Select(Profile(), db);

        Assert.Equal(new[] { "a.ok" }, selection.DefaultSet.Select(v => v.Tweak.Id));
        Assert.Empty(selection.OptIn);
        Assert.Empty(selection.Excluded);
    }

    // --- ordering ---

    [Fact]
    public void DefaultSet_LeadsWithTheBestEvidencedTweaks()
    {
        var db = DatabaseOf(
            Tweak("z.weak", evidence: 3),
            Tweak("a.strong", evidence: 5),
            Tweak("m.mid", evidence: 4));

        var selection = Select(Profile(), db);

        Assert.Equal(
            new[] { "a.strong", "m.mid", "z.weak" },
            selection.DefaultSet.Select(v => v.Tweak.Id));
    }

    // --- the classifier itself ---

    [Fact]
    public void ClassifyPolicy_ReportsTheFirstBarFailed()
    {
        // The order matters: a tweak excluded by name should not be reported as
        // "too risky", because the name is the actual reason and the actionable
        // one.
        var profile = Profile(
            excludeTweaks: new[] { "x.excludedById" },
            excludeCategories: new[] { "excluded" },
            includeCategories: new[] { "wanted" },
            maxRisk: RiskLevel.Safe,
            minEvidence: 4);

        Assert.Equal(ExclusionReason.ExcludedById,
            TweakDatabase.ClassifyPolicy(profile, Tweak("x.excludedById", RiskLevel.Myth, 0, "excluded")));
        Assert.Equal(ExclusionReason.ExcludedCategory,
            TweakDatabase.ClassifyPolicy(profile, Tweak("x.byCat", RiskLevel.Myth, 0, "excluded")));
        Assert.Equal(ExclusionReason.OutsideCategories,
            TweakDatabase.ClassifyPolicy(profile, Tweak("x.otherCat", RiskLevel.Myth, 0, "elsewhere")));
        Assert.Equal(ExclusionReason.HighRisk,
            TweakDatabase.ClassifyPolicy(profile, Tweak("x.risky", RiskLevel.Risky, 5, "wanted")));
        Assert.Equal(ExclusionReason.LowEvidence,
            TweakDatabase.ClassifyPolicy(profile, Tweak("x.shaky", RiskLevel.Safe, 1, "wanted")));
        Assert.Equal(ExclusionReason.None,
            TweakDatabase.ClassifyPolicy(profile, Tweak("x.fine", RiskLevel.Safe, 5, "wanted")));
    }

    [Fact]
    public void ClassifyPolicy_AnExplicitIncludeBeatsEveryOtherBar()
    {
        // `includeTweaks` is how a profile pins something on regardless of its
        // risk or evidence. It has to win, or there is no way to name a single
        // exception.
        var profile = Profile(maxRisk: RiskLevel.Safe, minEvidence: 5);
        profile.IncludeTweaks.Add("x.pinned");

        Assert.Equal(ExclusionReason.None,
            TweakDatabase.ClassifyPolicy(profile, Tweak("x.pinned", RiskLevel.Myth, 0)));
    }

    [Fact]
    public void Catalogue_NoTweakCarriesADeadProfilesArray()    {
        // A per-tweak profile list is what this whole change removed: nothing
        // consulted it, so 53 of 58 disagreed with where the engine actually put
        // the tweak. The model no longer has the field; this asserts the JSON
        // that feeds it does not carry a leftover either.
        var db = Catalogue();
        var files = Directory.GetFiles(TweakDir, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var raw = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(raw);
            foreach (var tweak in doc.RootElement.EnumerateArray())
            {
                if (tweak.TryGetProperty("profiles", out _))
                    offenders.Add($"{Path.GetFileName(file)}: {tweak.GetProperty("id").GetString()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These tweaks still declare a profiles array, which nothing reads: "
            + string.Join(", ", offenders));
    }

    // --- notices: the things that are true of the profile, not a tweak ---

    [Fact]
    public void FormFactorMismatch_IsStatedRatherThanActedOn()
    {
        // A laptop-only profile reporting itself available on a desktop, and
        // quietly applying a power policy to a machine with no battery to
        // spare, was a silent assumption. Refusing outright would take Battery
        // Saver away from a desktop that wants it — so it is said, not acted
        // on, and the user still gets the list to read before agreeing.
        var db = DatabaseOf(Tweak("a.ok"));
        var selection = Select(Profile(formFactor: "laptop"), db, Machine());

        Assert.Equal("laptop", selection.TargetFormFactor);
        Assert.Contains(selection.Notices, n => n.Contains("laptop") && n.Contains("Desktop"));
        Assert.Single(selection.DefaultSet);
    }

    [Fact]
    public void FormFactorMatch_DoesNotRaiseANotice()
    {
        var db = DatabaseOf(Tweak("a.ok"));
        var selection = Select(Profile(formFactor: "Desktop"), db, Machine());

        Assert.Empty(selection.Notices);
    }

    [Fact]
    public void LowEvidenceNotice_CountsOnlyWhatTheEvidenceBarHeldBack()
    {
        // The notice points the reader at "available if you want them", so its
        // count has to be that section's count. Counting every tweak below the
        // minimum — including ones the risk bar or a category already took off
        // the table — would send someone looking for rows that are not there.
        var db = DatabaseOf(
            Tweak("a.shaky", evidence: 1),                            // evidence bar only
            Tweak("a.shakyButRisky", evidence: 1, risk: RiskLevel.Risky), // risk bar first
            Tweak("a.strong", evidence: 5),
            Tweak("b.outside", category: "elsewhere", evidence: 1));

        var selection = Select(Profile(minEvidence: 3), db);

        // Both shaky tweaks reach opt-in — one is entitled, one is merely
        // reachable — but only the first failed the evidence bar, and only
        // that is what the notice counts.
        var notice = Assert.Single(selection.Notices, n => n.Contains("held back for low evidence"));
        Assert.Contains("1 tweak(s)", notice);

        Assert.Equal(ExclusionReason.LowEvidence, Assert.Single(
            selection.OptIn.Where(v => v.Tweak.Id == "a.shaky")).Reason);
        Assert.Equal(ExclusionReason.HighRisk, Assert.Single(
            selection.OptIn.Where(v => v.Tweak.Id == "a.shakyButRisky")).Reason);
    }

    [Fact]
    public void NoticeCount_MatchesTheOptInSectionItPointsAt()
    {
        var db = DatabaseOf(
            Tweak("a.shaky", evidence: 1),
            Tweak("b.shaky", evidence: 2),
            Tweak("a.high", evidence: 5, risk: RiskLevel.Risky));
        var selection = Select(Profile(maxRisk: RiskLevel.Optional, minEvidence: 3), db);

        // Opt-in holds the evidence misses AND the risk misses; only the former
        // the notice claims, and only the former it counts.
        Assert.Equal(2, selection.OptIn.Count(v => v.Reason == ExclusionReason.LowEvidence));
        var notice = Assert.Single(selection.Notices, n => n.Contains("held back for low evidence"));
        Assert.Contains("2 tweak(s)", notice);
    }

    // --- the shape of a selection ---

    [Fact]
    public void Selection_ReportsThePolicyItWorkedFrom()
    {
        // The UI shows what it is working from. If this is not carried through,
        // a reader cannot check the lists against the numbers on the card.
        var db = DatabaseOf(Tweak("a.ok"));
        var selection = Select(Profile(maxRisk: RiskLevel.Safe, minEvidence: 4), db);

        Assert.Equal(RiskLevel.Safe, selection.MaxRisk);
        Assert.Equal(4, selection.MinEvidence);
        Assert.Null(selection.TargetFormFactor);
    }

    [Fact]
    public void EachBucketIsRanked_EvidenceFirstThenRiskThenId()
    {
        // The model ranks, not the caller, so the CLI and the UI cannot end up
        // with two different orders for the same selection.
        var db = DatabaseOf(
            Tweak("z.risky", risk: RiskLevel.Risky, evidence: 3),
            Tweak("a.careful", risk: RiskLevel.Optional, evidence: 4),
            Tweak("m.ok", risk: RiskLevel.Safe, evidence: 5),
            Tweak("a.strongest", risk: RiskLevel.Risky, evidence: 5));

        var selection = Select(Profile(maxRisk: RiskLevel.Safe), db);

        // Evidence leads: the Risky tweak with a 5 outranks the Optional with
        // a 4. At equal evidence the gentler risk sorts first, so a reader
        // meeting the list top to bottom meets the easier call first.
        Assert.Equal(
            new[] { "a.strongest", "a.careful", "z.risky" },
            selection.OptIn.Select(v => v.Tweak.Id));
        Assert.Equal(new[] { "m.ok" }, selection.DefaultSet.Select(v => v.Tweak.Id));
    }
}