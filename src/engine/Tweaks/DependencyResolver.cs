using WinOpt.Core.Models;

namespace WinOpt.Engine.Tweaks;

/// <summary>
/// Works out which tweaks in a batch can actually run, in what order, and what
/// is holding the rest back.
///
/// Two kinds of coupling are handled:
/// <list type="bullet">
///   <item>declared — a tweak's <c>dependsOn</c> names tweaks it needs first,
///   and <c>conflictsWith</c> names peers it cannot coexist with;</item>
///   <item>inferred — two tweaks write the same target to different values, so
///   the second would silently undo the first. Nothing in the catalogue declares
///   these today, so they are derived from the specs rather than left to
///   hand-written lists that rot.</item>
/// </list>
///
/// The plan is computed before anything touches the system, so both the CLI and
/// the UI can show it as a preview and offer the user a choice.
/// </summary>
public sealed class DependencyResolver
{
    private readonly TweakDatabase _database;

    public DependencyResolver(TweakDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Resolve a requested set into an executable order, holding back anything
    /// whose dependency is absent or whose target another tweak in the run
    /// contradicts.
    /// </summary>
    public BatchPlan Resolve(IReadOnlyList<TweakDefinition> requested)
    {
        // A duplicated ID means the caller asked for the same tweak twice
        // (profile membership plus an explicit pick). Last one wins, matching
        // how TweakDatabase resolves a re-loaded definition.
        var byId = new Dictionary<string, TweakDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in requested)
            byId[t.Id] = t;

        var plan = new BatchPlan { RequestedCount = byId.Count };

        // First block wins, so the reported reason points at the root cause
        // rather than at a consequence of it.
        var held = new Dictionary<string, PlanEntry>(StringComparer.OrdinalIgnoreCase);
        void Hold(string id, PlanAction action, string reason, IEnumerable<string> by)
        {
            if (held.ContainsKey(id)) return;
            held[id] = new PlanEntry
            {
                TweakId = id,
                Action = action,
                Reason = reason,
                BlockedBy = by.Where(x => !string.IsNullOrEmpty(x))
                              .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Order = -1,
            };
        }

        ResolveDeclaredConflicts(byId, plan, Hold);
        ResolveResourceCollisions(byId, plan, Hold);
        ResolveDependencies(byId, held, plan, Hold);

        var runnable = byId.Values
            .Where(t => !held.ContainsKey(t.Id))
            .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ordered = TopologicalSort(runnable, plan, Hold);

        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Count; i++)
        {
            position[ordered[i].Id] = i;
            plan.OrderedTweakIds.Add(ordered[i].Id);
        }

        foreach (var t in byId.Values.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase))
        {
            plan.Entries.Add(held.GetValueOrDefault(t.Id) ?? new PlanEntry
            {
                TweakId = t.Id,
                Action = PlanAction.Apply,
                Order = position[t.Id],
            });
        }

        return plan;
    }

    private static void ResolveDeclaredConflicts(
        Dictionary<string, TweakDefinition> byId,
        BatchPlan plan,
        Action<string, PlanAction, string, IEnumerable<string>> hold)
    {
        // A declared conflict is symmetric even when only one side lists it, so
        // each unordered pair is recorded once and broken by ID. Otherwise a
        // pair that lists each other would hold both and run neither.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in byId.Values)
        {
            foreach (var otherId in t.ConflictsWith)
            {
                if (!byId.TryGetValue(otherId, out var other)) continue;
                if (!seen.Add(PairKey(t.Id, otherId))) continue;

                var keep = string.Compare(t.Id, otherId, StringComparison.OrdinalIgnoreCase) <= 0 ? t : other;
                var drop = ReferenceEquals(keep, t) ? other : t;
                var key = TweakTarget.Resource(keep);
                if (key.Length == 0) key = TweakTarget.Resource(drop);
                if (key.Length == 0) key = "declared";

                plan.Conflicts.Add(new TweakConflict
                {
                    A = keep.Id,
                    B = drop.Id,
                    Resource = key,
                    Reason = $"Conflict declared between '{keep.Id}' and '{drop.Id}' in the catalogue.",
                });

                hold(drop.Id, PlanAction.Conflict,
                    $"Conflicts with '{keep.Id}', which is mutually exclusive.",
                    new[] { keep.Id });
            }
        }
    }

    private static string PairKey(string a, string b)
    {
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0
            ? a.ToLowerInvariant() + "|" + b.ToLowerInvariant()
            : b.ToLowerInvariant() + "|" + a.ToLowerInvariant();
    }

    private static void ResolveResourceCollisions(
        Dictionary<string, TweakDefinition> byId,
        BatchPlan plan,
        Action<string, PlanAction, string, IEnumerable<string>> hold)
    {
        var groups = byId.Values
            .Select(t => (Resource: TweakTarget.Resource(t), Tweak: t))
            .Where(x => x.Resource.Length > 0)
            .GroupBy(x => x.Resource, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var members = group.Select(x => x.Tweak).ToList();

            // Two tweaks writing the same target to the same value are
            // idempotent, not a collision.
            if (members.Select(TweakTarget.Write).Distinct(StringComparer.Ordinal).Count() < 2)
                continue;

            // Stable tie-break, so the same batch resolves the same way every
            // time regardless of catalogue or profile ordering.
            var ordered = members.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToList();
            var winner = ordered[0];

            foreach (var loser in ordered.Skip(1))
            {
                plan.Conflicts.Add(new TweakConflict
                {
                    A = winner.Id,
                    B = loser.Id,
                    Resource = group.Key,
                    Reason = $"Both write {group.Key} to different values, so running both would leave only the first in effect.",
                });

                hold(loser.Id, PlanAction.Conflict,
                    $"'{winner.Id}' writes a different value to the same target ({group.Key}); running both would leave only the winner in effect.",
                    new[] { winner.Id });
            }
        }
    }

    /// <summary>
    /// Resolves <c>dependsOn</c> to a fixed point: a tweak held back for a
    /// missing dependency must not in turn hold back its dependants twice over,
    /// and the transitive set has to settle before ordering.
    /// </summary>
    private void ResolveDependencies(
        Dictionary<string, TweakDefinition> byId,
        Dictionary<string, PlanEntry> held,
        BatchPlan plan,
        Action<string, PlanAction, string, IEnumerable<string>> hold)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var t in byId.Values)
            {
                if (held.ContainsKey(t.Id)) continue;

                var missing = t.DependsOn.Where(d => !byId.ContainsKey(d)).ToList();
                if (missing.Count > 0)
                {
                    foreach (var dep in missing)
                    {
                        plan.MissingDependencies.Add(new MissingDependency
                        {
                            TweakId = t.Id,
                            RequiredId = dep,
                            RequiredExists = _database.Get(dep) != null,
                        });
                    }

                    hold(t.Id, PlanAction.MissingDependency,
                        $"Depends on {DescribeIds(missing)}, which " +
                        (missing.All(d => _database.Get(d) != null)
                            ? "is not part of this run."
                            : "does not exist in the catalogue."),
                        missing);
                    changed = true;
                    continue;
                }

                // A dependency that is itself blocked cannot be waited on, so
                // the blocker propagates down the chain exactly once each.
                var blockedDep = t.DependsOn.FirstOrDefault(held.ContainsKey);
                if (blockedDep != null)
                {
                    hold(t.Id, PlanAction.BlockedByDependency,
                        $"Depends on '{blockedDep}', which is itself blocked.",
                        new[] { blockedDep });
                    changed = true;
                }
            }
        } while (changed);
    }

    private static string DescribeIds(IReadOnlyList<string> ids)
        => ids.Count == 1
            ? $"'{ids[0]}'"
            : string.Join(", ", ids.Select(i => $"'{i}'"));

    /// <summary>
    /// Orders the runnable set so every tweak follows the ones it depends on.
    /// A cycle cannot be ordered, so it is reported rather than silently broken.
    /// </summary>
    private static List<TweakDefinition> TopologicalSort(
        List<TweakDefinition> runnable,
        BatchPlan plan,
        Action<string, PlanAction, string, IEnumerable<string>> hold)
    {
        var remaining = runnable.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        var order = new List<TweakDefinition>(runnable.Count);

        while (remaining.Count > 0)
        {
            // Emit everything whose in-run dependencies are already out. A
            // dependency outside this run was never a constraint here — it was
            // caught above as either missing or blocked.
            var ready = remaining.Values
                .Where(t => t.DependsOn.All(d => !remaining.ContainsKey(d)))
                .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ready.Count > 0)
            {
                foreach (var t in ready
                    // A scheme switch runs first so the settings tweaks that
                    // follow write into the scheme that will actually stay
                    // active. Applied in the other order they land in a scheme
                    // the run then switches away from, and pass verification
                    // only because it ran before the switch.
                    .OrderBy(t => TweakTarget.IsPowerPlanSwitch(t) ? 0 : 1)
                    .ThenBy(t => t.Id, StringComparer.OrdinalIgnoreCase))
                {
                    order.Add(t);
                    remaining.Remove(t.Id);
                }
                continue;
            }

            // Nothing can be emitted: what remains is a cycle. Report every
            // member rather than picking an arbitrary one to break.
            var stuck = remaining.Values
                .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var t in stuck)
            {
                hold(t.Id, PlanAction.DependencyCycle,
                    $"Circular 'dependsOn': {string.Join(" → ", t.DependsOn)}.",
                    t.DependsOn.Where(remaining.ContainsKey));
            }

            if (stuck.Count > 1)
            {
                plan.Conflicts.Add(new TweakConflict
                {
                    A = stuck[0].Id,
                    B = stuck[^1].Id,
                    Resource = "dependsOn",
                    Reason = "Circular dependency: " + string.Join(", ",
                        stuck.Select(t => $"{t.Id}({string.Join(",", t.DependsOn)})")),
                });
            }

            remaining.Clear();
        }

        return order;
    }
}
