# Foundation Hardening — Design

**Date:** 2026-10-01
**Status:** Implemented
**Scope:** Phase 1 of the Novimize rework — the correctness layer under the existing
tweak system. No new tweaks are added here; this makes the existing 58 trustworthy
before any are added on top.

---

## Problem

The optimiser had a working engine and a complete-looking UI, but three things
underneath were wrong in ways the user could not see:

1. **Profiles named categories that do not exist.** `BuiltInProfiles` referenced
   `cpu`, `gpu`, `power`, `apps`, `memory`, `gaming`. The catalogue's real
   categories are `cpu-power`, `gpu-gaming`, `network`, `privacy`, `services`,
   `startup`, `storage`, `visual-effects`, `cleanup`. A `Gaming` profile
   therefore selected zero CPU tweaks, zero GPU tweaks and zero power tweaks —
   the categories the profile exists for. `Battery Saver` selected zero power
   tweaks. `Office` excluded a category called `gaming` that nothing uses. The
   app reported these profiles as working.

2. **Two tweaks fought over one power slot, both reporting success.**
   `cpu-power.plan.ultimatePerformance` and `cpu-power.plan.highPerformance` both
   run `powercfg /setactive <GUID>`, which flips the single global active-scheme
   slot. `gaming` and `workstation` between them selected both. Each apply
   returned `Success` and each verification passed — because the second one
   verified the state the first one had already written, before the second
   changed it. The user got a success screen and whichever plan ran last.

3. **Nothing recorded what was actually blocked.** A multi-select apply spawned
   one CLI process per tweak. Each got its own snapshot, no cross-tweak
   conflict detection, and the result tally was re-implemented in Rust, where
   it could drift from the CLI's own definition of success.

None of these are visible in a screenshot. They are the class of defect that
makes an optimiser untrustworthy: it reports work it did not do, or does not do.

---

## Design

### 1. A batch planner that runs before anything touches the system

`DependencyResolver.Resolve(requested) → BatchPlan` answers one question: given
this set of tweaks, what would run, in what order, and what is held back and why.

It handles two kinds of coupling:

- **Declared** — `dependsOn` (must run first) and `conflictsWith` (cannot
  coexist), both currently empty in the catalogue but honoured if populated.
- **Inferred** — two tweaks writing the same target to different values. The
  second would silently undo the first. Nothing declares these, so they are
  derived from the specs by `TweakTarget`, not hand-maintained in a list that
  rots.

Rules that are not obvious and were decided deliberately:

- **First block wins.** Once a tweak is held, later rules cannot overwrite the
  reason, so the user sees the root cause rather than a consequence of it.
  `b → a`, `a` missing: `a` reports the missing dependency, `b` reports
  *"Depends on 'a', which is itself blocked"* — not a second "missing".
- **A conflict holds one side, not both.** A conflict means "pick one", not "do
  neither". Holding both would empty the run for no reason. Ties break on
  lowercase ID so the same batch resolves identically every time.
- **Same target + same value is not a conflict.** Two tweaks writing the same
  value are idempotent, not colliding.
- **A cycle is reported, not broken.** Picking an arbitrary member to break
  would be an invisible, arbitrary decision. Every member is held as
  `DependencyCycle` and named.
- **A power-scheme switch runs first.** A settings tweak that flips the scheme
  and then writes values into it must be ordered scheme-switch-first, or the
  values land in a scheme the run then switches away from — and pass
  verification only because verification ran before the switch.

`ApplyBatchAsync` now calls the planner first, journals every held entry, and
returns **before creating a snapshot** when nothing is runnable. A snapshot of
"nothing was attempted" is noise in the rollback list.

### 2. One shared description of a target

`TweakTarget` gives the planner and the result path the same vocabulary for
"where does this write, and to what":

- `Resource` — `registry:HKCU\...::ValueName`, `service:Name`, `powerplan:active`
- `Write` — the value left behind; identical writes are not collisions
- `IsPowerPlanSwitch` — **only** `/setactive <GUID>`. `/setactive SCHEME_CURRENT`
  is what powercfg requires after a value change so the change takes effect; it
  does not move the active scheme. Treating it as a switch made all ~13
  powercfg tweaks look like they were fighting over one slot, which is how this
  bug was found in the first place.
- `Describe` — `Resource` or the method name, so no log row is ever blank

### 3. The journal becomes a first-class read path

An append-only audit log already existed. Rather than add a second, parallel
record type, it was **fixed and exposed**:

- `target` now carries the resource, not a generic label
- `snapshotId` is populated on rollback (it was writing the session id there)
- every terminal outcome is journalled — `success`, `failure`, `skipped`,
  `blocked` — including the ones that never reached a provider
- `ReadJournal` / `ReadJournalJson` read every `audit-*.jsonl` newest-first and
  **skip torn lines**, so a crash mid-append does not make the whole history
  unreadable
- `CleanupJournal(retentionDays)` bounds growth

`winopt-cli journal [--limit N] [--tweak ID] [--operation apply|rollback]
[--result ...]` reads it.

### 4. Preview and apply share one selection

`SelectTweaks` resolves the selection arguments for **both** `plan` and
`apply`, so a plan and the apply it previews can never answer different
questions. Comma-separated IDs are one batch, so the planner sees all members
at once.

Guards that were missing and were added: an empty catalogue, an unknown
profile, a profile that selects nothing compatible, an unknown category (which
now lists the real ones), and a category with nothing compatible. Each fails
loudly with a non-zero exit instead of reporting "0 of 0" and exiting 0.

### 5. Conflicts are surfaced before the user commits, with a fix

`plan_tweak` is a new Tauri command. The confirm modal shows, before any
command runs:

- every tweak that will be held back, with the action and the reason
- for a **missing dependency** that exists in the catalogue: *Include X*
- for a **conflict**: *Keep A* / *Keep B*

The left side of each before/after now shows what the **scan measured** on this
machine, not the catalogue's guess at the default. A `null` renders `?` rather
than a default, because the scan ran and could not read the value — printing a
default there would be a claim about the machine that nobody made.

### 6. Held back is not failed

`ApplySummary` gains a `blocked` bucket. A tweak held back by the planner is
untouched and reported as such: "3 held back — they are untouched, not failed."
Folding it into failures made the real failures harder to find, which is the
opposite of what a result card is for.

### 7. One process, one snapshot, one tally

`apply_tweak` in the Tauri shell now issues **one** CLI call for the whole list.
The Rust-side tally is gone; the CLI's own is used. N processes meant N
snapshots, no cross-tweak conflict detection, and two implementations of
"success" that could disagree.

### 8. Dead code removed, duplicated truth deleted

- Five providers that claimed no `TweakMethod` were deleted. The registry
  dispatches on method, so a provider claiming none is unreachable — not
  "available for future use".
- The Profiles page kept its own hard-coded copy of the profile table, with the
  same broken category names as the C# original. It now loads from
  `list_profiles`, which reads `BuiltInProfiles`. Two copies of a policy table
  is two places for it to be wrong.
- `ScanOptimize`'s `specialCategories` held `'gpu'` and `'power'` — categories
  that do not exist, so the branch never fired. Corrected to the real ones.

---

## Testing

`tests/core/PlannerTests.cs` — 34 tests, all passing. Beyond the ordering and
cycle cases, the catalogue-integrity tests fail the build when the data drifts:

| Test | Guards against |
|---|---|
| `Catalogue_CategoriesMatchThoseUsedByProfiles` | the original bug — a profile naming a category no tweak uses |
| `Catalogue_TweakIdsReferencedByProfilesExist` | a profile excluding a tweak ID that is not in the catalogue |
| `Catalogue_ProfilesDoNotSelectBothPowerPlanSwitches` | the original bug 2 — a profile selecting both scheme switches |
| `Catalogue_DeclaredDependenciesAndConflictsResolve` | a `dependsOn`/`conflictsWith` naming a nonexistent ID |
| `Catalogue_FullRunBlocksOnlyTheKnownPowerPlanCollision` | any *new* target collision appearing in the catalogue |
| `Providers_CoverEveryMethodTheCatalogueUses` | a tweak whose method has no provider |
| `Providers_ClaimAtLeastOneMethod` | a provider that claims no method (unreachable code) |
| `Journal_SurvivesATornLine` | a crash mid-append making history unreadable |

---

## Known limitations

- **`OptimizationProfile.FormFactor` is never read.** `battery-saver` declares
  `"laptop"` and nothing consumes the field. Left as-is and flagged rather than
  silently "fixed", because deciding what a form-factor mismatch should do —
  refuse, or warn — is a product call, not a refactor.
- **`TweakDefinition.Profiles` lists a `privacy` profile** that is not in
  `BuiltInProfiles`; it is only consumed as a boost in `RecommendationEngine`.
- **Powercfg GUIDs are opaque.** The planner knows two scheme switches collide;
  it does not know that the Ultimate GUID is a hidden scheme that has to be
  duplicated first on some machines. That is a provider concern, not a
  planning one.

## Not in this phase

Tweak catalogue expansion, Gaming Center, App Installer, appearance, blocker
management, DNS/network/power/startup/services editors, debloat, maintenance,
health reports, activation, updater, theming. Each is a separate spec.
