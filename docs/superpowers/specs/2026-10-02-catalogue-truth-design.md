# Catalogue Truth — profile membership, one system instead of two

**Date:** 2026-10-02
**Scope:** Sub-project 3 of the Novimize rework, scoped down to *fixing the two-systems
defect before adding the 78 validated tweaks.*

## Problem

A tweak's membership in a profile was recorded twice.

1. `TweakDefinition.Profiles` — a `string[]` written into all 58 JSON definitions.
2. `TweakDatabase.GetForProfile(profile)` — a computed filter over category, risk,
   evidence, and explicit include/exclude lists.

Only (2) was ever used to decide what `apply --profile` runs. (1) was read in exactly
one place, `RecommendationEngine.Score`, which added +10 to any tweak whose array
mentioned the profile. Measured across the catalogue:

- **53 of 58** declared arrays disagreed with where the engine actually put the tweak.
- **11** declared a `privacy` profile that does not exist.
- **`cpu-power.turbo.disabled`** was declared for `battery-saver` while the profile's
  `MaxRisk = Safe` structurally excludes it — Battery Saver reported success while
  never disabling turbo.

The user-visible consequence: the recommendation score and the apply run were
answering the same question from two different sources, and the source the user could
see was the one that was wrong.

Two adjacent dead fields were read by nothing and therefore misbehaved:
`OptimizationProfile.FormFactor` (Battery Saver applied laptop power policy to a
desktop and said nothing) and the per-tweak `profiles` array.

## Decisions

Three, taken with the user:

| Question | Decision |
|----------|----------|
| Membership system | **Category policy only.** Delete `Profiles` from the model and all 58 JSON files. |
| Profile list UI | **Split into "applies by default" and "available if you want them."** |
| Unusable / low-evidence tweaks | **Add them at low evidence, filtered out of profiles** — reachable, not automatic. |
| Risky tweaks | Reach opt-in; do not ask. |

## Design

### One classifier

`TweakDatabase.ClassifyPolicy(profile, tweak) → ExclusionReason` is now the only
implementation of the membership question. Order matters and is asserted:

```
ExcludedById → (IncludeTweaks short-circuit → None) → ExcludedCategory
→ OutsideCategories → HighRisk → LowEvidence → None
```

`GetForProfile` is `Where(ClassifyPolicy == None)`. The recommendation boost reads the
same call, so the score and the run can no longer disagree.

Two guards are deliberately *outside* policy, because they are not the profile's
opinion:

- `IsHardwareCompatible` — build window, form factor, GPU vendor, power-plan alias.
  One shared implementation, called from `FilterCompatible` and from the selector.
  (The selector initially carried a second copy — the same defect this change exists
  to remove — and was refactored immediately.)
- `IsSecurityBlocked` — `Myth | Deprecated | Dangerous`. Absolute, and checked
  **before** policy.

### The read model: `ProfileSelection`

`ProfileSelector.Select(profile, systemInfo) → ProfileSelection` returns three
buckets plus notices:

| Bucket | Contents | Offered as opt-in? |
|--------|----------|--------------------|
| `defaultSet` | `Reason == None` | n/a — this is what apply runs unasked |
| `optIn` | failed only `LowEvidence` or `HighRisk` | **yes** |
| `excluded` | `ExcludedById`, `ExcludedCategory`, `OutsideCategories`, `SecurityBlocked` | **no** |

Hardware-incompatible tweaks get **no bucket at all** — offering to opt into a tweak
this machine cannot take is offering something that will fail.

Opt-in is limited to the two soft bars because those are judgement calls the profile
is entitled to hand the user. A named exclusion, a dropped category, or a security
refusal is the author's decision, not a menu item.

`Notices` covers things true of the *profile on this machine* rather than of a
particular tweak:

- **Form-factor mismatch** — stated, deliberately **not** acted on. Refusing would take
  Battery Saver away from a desktop that wants it; a warning the user can ignore beats
  a silent assumption.
- **Count of tweaks held back by the evidence bar** — counted with
  `ClassifyPolicy == LowEvidence` so the number matches the section it points at.
  (The first implementation counted `== None`, which is every tweak that *passed* —
  caught by reading the output before shipping.)

Ranking (`Rank`) lives on the model, not the caller: evidence desc, then risk asc, then
id. Init-only properties cannot be reassigned post-construction, so both CLI and UI get
one order without re-sorting.

**Invariant:** `selector.DefaultSet(profile, machine) ≡
FilterCompatible(GetForProfile(profile), machine)`. Asserted for all 8 built-in
profiles. If these diverge, the app shows a list it does not apply.

### Making opt-in real

A split where the second list is not actionable is the same silence under a new
heading. `--include <comma ids>` was added to `list`, `plan`, and `apply`:

```
plan   --profile battery-saver --include "visual.menuDelay,visual animations"
```

Each opt-in still passes the security guard and the hardware gate. A refusal —
blocked, unknown, or incompatible — **aborts before anything runs** with exit 1 and a
message naming every problem, rather than dropping it. A checkbox that does nothing is
the defect this change exists to fix.

`SelectTweaks` is now "resolve, then add includes" so an opt-in can never alter which
tweaks the profile itself chose.

### Surfaces

- **CLI:** `profile-selector <id> [--json]` prints notices first, then the three
  buckets. The excluded list is **grouped by reason** — one row per tweak was 35
  near-identical lines that buried the two exclusions that were deliberate.
- **Rust:** `profile_selector`, plus optional `include` on `apply_profile`,
  `plan_tweak`, and `list_tweaks`.
- **UI:** `ProfileTweakList` renders the split, notices, and a collapsible excluded
  section; the risk bar measures default + ticked opt-ins, so the cost of the choice
  is visible when it is made. `Profiles.tsx` passes the same opt-ins to the confirm
  fetch, the plan, and the apply — one list, three uses.

## Contract changes

- **`TweakDef.profiles` is gone** from `list --json`. It was already ignored by
  everything except the stale recommendation boost; the UI block that rendered it was
  reading `undefined` the moment the field left the C# model.
- `profile-selector` is a new command.

## Testing

57 tests, 0 failing. New coverage in `ProfileSelectorTests`:

- the default-set invariant across all 8 profiles; non-empty default set for each;
  every hardware-compatible tweak in exactly one bucket
- each opt-in and non-opt-in reason; security block never offered; hardware-incompatible
  in no bucket
- form-factor notice on mismatch, silence on match
- evidence-notice counts only what the evidence bar held back
- policy order (`ClassifyPolicy_ReportsTheFirstBarFailed`); explicit include beats all
- no `profiles` array survives in any of the 58 JSON files (raw `JsonDocument` scan)

Verified end-to-end against the published CLI: battery-saver → 9 default / 4 opt-in /
35 excluded; daily → 31 / 16 / 1; `--include` widens a plan 9→11; a security-blocked
opt-in exits 1.

## Not in scope

The 78-item backlog, Gaming Center, App Installer, appearance, blocker, debloat,
health, activation, and the updater. Several backlog entries already exist in the
catalogue (41, 42, 43, 51, 63, 65, 66) and must be de-duplicated before Phase 4.
