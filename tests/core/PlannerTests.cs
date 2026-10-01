using WinOpt.Core.Models;
using WinOpt.Engine;
using WinOpt.Engine.Logging;
using WinOpt.Engine.Providers;
using WinOpt.Engine.Tweaks;
using WinOpt.Providers.NetSh;
using WinOpt.Providers.PowerShell;
using WinOpt.Providers.Registry;
using WinOpt.Providers.ScheduledTask;
using WinOpt.Providers.Service;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Tests for the batch planner: what a run will execute, in what order, and why
/// anything is held back.
/// </summary>
public class PlannerTests
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
    /// A minimal tweak that writes one registry value. Everything the planner
    /// reasons about is derived from these fields.
    /// </summary>
    private static TweakDefinition Registry(string id, string value = "1", string? valueName = null,
        IEnumerable<string>? dependsOn = null, IEnumerable<string>? conflictsWith = null)
        => new()
        {
            Id = id,
            Name = id,
            Category = "test",
            Method = TweakMethod.Registry,
            TargetValue = value,
            DependsOn = dependsOn?.ToList() ?? new List<string>(),
            ConflictsWith = conflictsWith?.ToList() ?? new List<string>(),
            Apply = new OperationSpec
            {
                RegistryKey = @"HKCU\Test\Key",
                RegistryValue = valueName ?? "Setting",
                RegistryData = value,
                RegistryType = "DWORD",
            },
        };

    private static BatchPlan Plan(TweakDatabase db, params TweakDefinition[] tweaks)
        => new DependencyResolver(db).Resolve(tweaks);

    // --- ordering ---

    [Fact]
    public void Resolve_RunsEverything_WhenNothingCouples()
    {
        var plan = Plan(Catalogue(), Registry("a"), Registry("b"), Registry("c"));

        Assert.Equal(3, plan.ApplicableCount);
        Assert.Equal(0, plan.BlockedCount);
        Assert.False(plan.HasIssues);
        Assert.Equal(3, plan.RequestedCount);
    }

    [Fact]
    public void Resolve_OrdersDependenciesFirst()
    {
        var db = Catalogue();
        var dependent = Registry("app.tweak", dependsOn: new[] { "base.tweak" });

        // Listed in the order a caller would naively hand them over.
        var plan = Plan(db, dependent, Registry("base.tweak"));

        Assert.Equal(0, plan.BlockedCount);
        Assert.Equal("base.tweak", plan.OrderedTweakIds[0]);
        Assert.Equal("app.tweak", plan.OrderedTweakIds[1]);
    }

    [Fact]
    public void Resolve_HoldsTweak_WhenDependencyNotInRun()
    {
        var db = Catalogue();
        var dependent = Registry("app.tweak", dependsOn: new[] { "absent.tweak" });

        var plan = Plan(db, dependent);

        Assert.Equal(0, plan.ApplicableCount);
        Assert.Equal(1, plan.BlockedCount);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(PlanAction.MissingDependency, entry.Action);
        Assert.Contains("absent.tweak", entry.Reason);
        Assert.Equal("absent.tweak", Assert.Single(entry.BlockedBy));

        // A missing dependency that does exist in the catalogue can be fixed by
        // adding it to the run; one that does not exist cannot.
        var missing = Assert.Single(plan.MissingDependencies);
        Assert.False(missing.RequiredExists);
    }

    [Fact]
    public void Resolve_MarksDependencyThatExistsElsewhere()
    {
        var db = Catalogue();
        var real = db.Tweaks.Keys.First();
        var dependent = Registry("app.tweak", dependsOn: new[] { real });

        var plan = Plan(db, dependent);

        Assert.Equal(PlanAction.MissingDependency, Assert.Single(plan.Entries).Action);
        Assert.True(Assert.Single(plan.MissingDependencies).RequiredExists);
    }

    [Fact]
    public void Resolve_BlocksDependants_WhenDependencyIsBlocked()
    {
        var db = Catalogue();

        // c -> b -> a, with a held back for a dependency that is absent.
        var a = Registry("a", dependsOn: new[] { "never" });
        var b = Registry("b", dependsOn: new[] { "a" });
        var c = Registry("c", dependsOn: new[] { "b" });

        var plan = Plan(db, a, b, c);

        Assert.Equal(0, plan.ApplicableCount);
        Assert.Equal(3, plan.BlockedCount);
        Assert.All(plan.Entries, e => Assert.NotEqual(PlanAction.Apply, e.Action));

        // The root cause is reported for the tweak that has one; the others
        // report the chain rather than repeating "dependency missing".
        Assert.Equal(PlanAction.MissingDependency, Entry(plan, "a").Action);
        Assert.Equal(PlanAction.BlockedByDependency, Entry(plan, "b").Action);
        Assert.Equal(PlanAction.BlockedByDependency, Entry(plan, "c").Action);
        Assert.Contains("a", Entry(plan, "b").Reason);
        Assert.Contains("b", Entry(plan, "c").Reason);
    }

    // --- conflicts ---

    [Fact]
    public void Resolve_HoldsOneOfADeclaredConflict_Pair()
    {
        var db = Catalogue();
        var a = Registry("a", conflictsWith: new[] { "b" });
        var b = Registry("b");

        var plan = Plan(db, a, b);

        Assert.Equal(1, plan.ApplicableCount);
        Assert.Equal(1, plan.BlockedCount);
        Assert.Equal(PlanAction.Conflict, Entry(plan, "b").Action);
        Assert.Contains("a", Entry(plan, "b").Reason);
        Assert.Single(plan.Conflicts);
    }

    [Fact]
    public void Resolve_DoesNotHoldBothSides_WhenConflictIsMutual()
    {
        var db = Catalogue();
        var a = Registry("a", conflictsWith: new[] { "b" });
        var b = Registry("b", conflictsWith: new[] { "a" });

        var plan = Plan(db, a, b);

        // Holding both would leave the run empty for no reason: a conflict means
        // "pick one", not "do neither".
        Assert.Equal(1, plan.ApplicableCount);
        Assert.Equal(1, plan.BlockedCount);
        Assert.Single(plan.Conflicts);
    }

    [Fact]
    public void Resolve_IgnoresConflictsWithTweaksOutsideTheRun()
    {
        var db = Catalogue();
        var a = Registry("a", conflictsWith: new[] { "not-in-this-run" });

        var plan = Plan(db, a);

        Assert.Equal(1, plan.ApplicableCount);
        Assert.Empty(plan.Conflicts);
    }

    // --- inferred resource collisions ---

    [Fact]
    public void Resolve_HoldsOneOfTwoTweaksWritingTheSameValue()
    {
        var db = Catalogue();
        var plan = Plan(db, Registry("zeta", "0"), Registry("alpha", "1"));

        Assert.Equal(1, plan.ApplicableCount);
        Assert.Equal(1, plan.BlockedCount);
        // Stable tie-break: the lower ID keeps the target regardless of input order.
        Assert.Equal(PlanAction.Apply, Entry(plan, "alpha").Action);
        Assert.Equal(PlanAction.Conflict, Entry(plan, "zeta").Action);
        Assert.Contains("registry", Assert.Single(plan.Conflicts).Resource);
    }

    [Fact]
    public void Resolve_DoesNotTreatIdenticalWritesAsAConflict()
    {
        var db = Catalogue();
        var plan = Plan(db, Registry("a", "1"), Registry("b", "1"));

        // Same target, same value: running both is a no-op, not a collision.
        Assert.Equal(2, plan.ApplicableCount);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Resolve_DistinguishesDifferentValueNamesOnTheSameKey()
    {
        var db = Catalogue();
        var plan = Plan(db,
            Registry("a", "1", "First"),
            Registry("b", "0", "Second"));

        Assert.Equal(2, plan.ApplicableCount);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Resolve_HoldsOneOfTheTwoPowerPlanSwitches()
    {
        // Both tweaks flip the single global active-scheme slot; applying both
        // would leave only the second in effect while both reported success.
        var db = Catalogue();
        var ultimate = db.Get("cpu-power.plan.ultimatePerformance");
        var high = db.Get("cpu-power.plan.highPerformance");
        Assert.NotNull(ultimate);
        Assert.NotNull(high);

        var plan = Plan(db, ultimate!, high!);

        Assert.Equal(1, plan.ApplicableCount);
        Assert.Equal(1, plan.BlockedCount);
        Assert.Contains(plan.Conflicts, c => c.Resource == "powerplan:active");
        Assert.Equal(PlanAction.Conflict,
            plan.Entries.Single(e => e.Action != PlanAction.Apply).Action);
    }

    // --- cycles ---

    [Fact]
    public void Resolve_ReportsDependencyCycle_InsteadOfOrderingArbitrarily()
    {
        var db = Catalogue();
        var a = Registry("a", dependsOn: new[] { "b" });
        var b = Registry("b", dependsOn: new[] { "a" });

        var plan = Plan(db, a, b);

        Assert.Equal(0, plan.ApplicableCount);
        Assert.Equal(2, plan.BlockedCount);
        Assert.All(plan.Entries, e => Assert.Equal(PlanAction.DependencyCycle, e.Action));
        Assert.Empty(plan.OrderedTweakIds);
        Assert.Single(plan.Conflicts);
    }

    [Fact]
    public void Resolve_ReportsSelfDependency()
    {
        var db = Catalogue();
        var a = Registry("a", dependsOn: new[] { "a" });

        var plan = Plan(db, a);

        Assert.Equal(PlanAction.DependencyCycle, Assert.Single(plan.Entries).Action);
    }

    // --- duplicates ---

    [Fact]
    public void Resolve_CollapsesDuplicateRequests()
    {
        var db = Catalogue();
        var plan = Plan(db, Registry("a"), Registry("a"));

        Assert.Equal(1, plan.RequestedCount);
        Assert.Equal(1, plan.ApplicableCount);
        Assert.Single(plan.Entries);
    }

    // --- catalogue integrity ---

    [Fact]
    public void Catalogue_CategoriesMatchThoseUsedByProfiles()
    {
        var db = Catalogue();
        var known = db.GetCategories();

        foreach (var profile in BuiltInProfiles.All)
        {
            foreach (var category in profile.IncludeCategories.Concat(profile.ExcludeCategories))
            {
                Assert.True(known.Contains(category, StringComparer.OrdinalIgnoreCase),
                    $"Profile '{profile.Id}' references category '{category}', which no tweak uses. " +
                    $"Known: {string.Join(", ", known)}");
            }
        }
    }

    [Fact]
    public void Catalogue_TweakIdsReferencedByProfilesExist()
    {
        var db = Catalogue();

        foreach (var profile in BuiltInProfiles.All)
        {
            foreach (var id in profile.IncludeTweaks.Concat(profile.ExcludeTweaks))
            {
                Assert.True(db.Get(id) != null,
                    $"Profile '{profile.Id}' references tweak '{id}', which is not in the catalogue.");
            }
        }
    }

    [Fact]
    public void Catalogue_ProfilesDoNotSelectBothPowerPlanSwitches()
    {
        // The two plan tweaks write the same slot. A profile that picks both
        // would report two successes and land on whichever ran last.
        var db = Catalogue();
        var conflictingPairs = new[]
        {
            ("cpu-power.plan.ultimatePerformance", "cpu-power.plan.highPerformance"),
        };

        foreach (var profile in BuiltInProfiles.All)
        {
            var selected = db.GetForProfile(profile);
            foreach (var (a, b) in conflictingPairs)
            {
                Assert.False(
                    selected.Any(t => t.Id == a) && selected.Any(t => t.Id == b),
                    $"Profile '{profile.Id}' selects both '{a}' and '{b}'; only one can hold.");
            }
        }
    }

    [Fact]
    public void Catalogue_DeclaredDependenciesAndConflictsResolve()
    {
        var db = Catalogue();

        foreach (var tweak in db.Tweaks.Values)
        {
            foreach (var id in tweak.DependsOn.Concat(tweak.ConflictsWith))
            {
                Assert.True(db.Get(id) != null,
                    $"'{tweak.Id}' names '{id}', which is not in the catalogue.");
            }
        }
    }

    [Fact]
    public void Catalogue_FullRunBlocksOnlyTheKnownPowerPlanCollision()
    {
        // Nothing in the catalogue declares coupling yet, so a full run is
        // expected to plan almost clean. The one real collision — two tweaks
        // flipping the same active-scheme slot — must be the only blocker, and
        // must name that resource. Any other blocker means a new tweak writes a
        // target another one already owns.
        var db = Catalogue();
        var plan = new DependencyResolver(db).Resolve(db.Tweaks.Values.ToList());

        Assert.Empty(plan.MissingDependencies);

        var blocked = plan.Entries.Where(e => e.Action != PlanAction.Apply).ToList();
        Assert.All(blocked, e => Assert.Equal(PlanAction.Conflict, e.Action));
        Assert.All(blocked, e =>
        {
            var conflict = plan.Conflicts.Single(c => c.B == e.TweakId);
            Assert.Equal("powerplan:active", conflict.Resource);
        });

        Assert.Equal(db.Count - blocked.Count, plan.ApplicableCount);
        Assert.Equal(db.Count, plan.ApplicableCount + plan.BlockedCount);
    }

    // --- providers ---

    [Fact]
    public void Providers_CoverEveryMethodTheCatalogueUses()
    {
        var db = Catalogue();
        var registry = new ProviderRegistry();
        registry.Register(new RegistryProvider());
        registry.Register(new ServiceProvider());
        registry.Register(new PowerShellProvider());
        registry.Register(new NetShProvider());
        registry.Register(new ScheduledTaskProvider());

        foreach (var method in db.Tweaks.Values.Select(t => t.Method).Distinct())
        {
            Assert.True(registry.GetProvider(method) != null,
                $"No provider is registered for method '{method}', used by the catalogue.");
        }
    }

    [Fact]
    public void Providers_ClaimAtLeastOneMethod()
    {
        // A provider claiming no method can never be dispatched to: the registry
        // keys on method, so an empty claim is unreachable code.
        var registry = new ProviderRegistry();
        registry.Register(new RegistryProvider());
        registry.Register(new ServiceProvider());
        registry.Register(new PowerShellProvider());
        registry.Register(new NetShProvider());
        registry.Register(new ScheduledTaskProvider());

        var providers = registry.GetAll();
        Assert.NotEmpty(providers);
        Assert.All(providers, p => Assert.NotEmpty(p.SupportedMethods));
    }

    // --- change journal ---

    [Fact]
    public void Journal_AppendsAndReadsBack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winopt-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var logger = new WinOptLogger(dir);
            var tweak = Registry("journaled.tweak");

            logger.AuditApply(tweak, @"HKCU\Test\Key::Setting", "0", "1",
                "reg add ...", "success", snapshotId: "snap-1");
            logger.AuditApply(tweak, @"HKCU\Test\Key::Setting", "1", "0",
                "reg add ...", "failure", "access denied");
            logger.AuditApply(tweak, @"HKCU\Test\Key::Setting", null, null,
                null, "blocked", "conflicts with something");
            logger.AuditRollback("journaled.tweak", TweakMethod.Registry,
                @"HKCU\Test\Key::Setting", "1", "0", "success", snapshotId: "snap-1");

            var entries = logger.ReadJournal(50);
            Assert.Equal(4, entries.Count);
            Assert.Equal(3, entries.Count(e => e.Operation == "apply"));
            Assert.Equal(1, entries.Count(e => e.Operation == "rollback"));
            Assert.Equal(2, entries.Count(e => e.Result == "success"));

            // The snapshot id has to land in the snapshot field, not the session one.
            Assert.Equal(2, entries.Count(e => e.SnapshotId == "snap-1"));
            Assert.All(entries, e => Assert.Null(e.SessionId));

            Assert.Single(logger.ReadJournal(50, operation: "rollback"));
            Assert.Single(logger.ReadJournal(50, result: "failure"));
            Assert.Equal(3, logger.ReadJournal(50, tweakId: "journaled.tweak", operation: "apply").Count);
            Assert.Single(logger.ReadJournal(50, tweakId: "journaled.tweak", result: "blocked"));
            Assert.Empty(logger.ReadJournal(50, tweakId: "some.other.tweak"));

            logger.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Journal_SurvivesATornLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winopt-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var logger = new WinOptLogger(dir);
            logger.AuditApply(Registry("good"), "target", "0", "1", "cmd", "success");

            // Simulate a crash mid-append.
            var file = Directory.GetFiles(Path.Combine(dir, "audit"), "*.jsonl").Single();
            File.AppendAllText(file, "{\"AuditId\":\"torn");

            var entries = logger.ReadJournal(50);
            Assert.Single(entries);
            Assert.Equal("good", entries[0].TweakId);

            logger.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static PlanEntry Entry(BatchPlan plan, string tweakId)
        => plan.Entries.Single(e => e.TweakId == tweakId);
}
