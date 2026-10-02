<div align="center">

# ⚡ Novimize

**A Windows PC optimizer you can actually read.**

*No cloud. No telemetry. Every change is a line of JSON you can open before you run it.*

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Tauri](https://img.shields.io/badge/Tauri-v2-orange)](https://tauri.app)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue?logo=windows)]()
[![.NET](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)]()

</div>

---

## Why another optimizer?

Most Windows "tweakers" are a wall of toggles backed by a script you'll never see. You click *Optimize*, something runs, and you find out what it did afterwards — if ever.

Novimize is built the other way around. The catalogue of changes lives in plain JSON files in [`tweaks/`](tweaks/). Read them. Edit them. Delete one you don't trust. Nothing runs that isn't written down, and every apply takes a snapshot first so you can put it back.

### What you get

- **85 tweaks across 10 categories** — CPU power, GPU, network, privacy, services, startup, storage, visual effects, explorer, cleanup
- **Hardware detection** — CPU, RAM, GPU and storage tier drive which tweaks are suggested to you
- **8 profiles** — Gaming, Daily Driver, Office, Streaming, Developer, Workstation, Battery Saver, Potato PC
- **Snapshot before every change** — one command rolls a tweak or a whole session back
- **Dry-run everywhere** — `--dry-run` shows exactly what would change without touching the system
- **Capability filtering** — a tweak your machine can't take (wrong power plan, laptop-only, no matching GPU), or one the security guard refuses, is filtered out of scans and lists rather than failing halfway through an apply
- **Batch planner** — a run is ordered, and anything that would fight another member of the same run is held back with the reason, before a single command executes
- **Profiles that show their working** — every tweak a profile leaves out is attributed: the risk bar, the evidence bar, an excluded category, or the security guard. `profile-selector` prints the split; opt into the rest by name
- **Change journal** — an append-only record of what was actually changed, readable with `journal`, with a result for every outcome including the ones held back
- **CLI and GUI share one engine** — the desktop app is a shell around the same .NET CLI, so what you see in the UI is what the command line does
- **Local-first** — no accounts, no network calls, no telemetry

## Tweak categories

| Category | Tweaks | What it covers |
|----------|-------:|----------------|
| ⚡ CPU Power | 13 | Core parking, minimum/maximum processor state, turbo boost, cooling policy, Speed Shift, power plans |
| 🎮 GPU Gaming | 9 | Hardware-accelerated GPU scheduling, fullscreen optimisations, shader cache, GameDVR, driver-level latency, USB selective suspend |
| 🌐 Network | 5 | TCP auto-tuning, RSC, window-scaling heuristics, DNS, DCA |
| 🔒 Privacy | 18 | Telemetry level, activity history, Cortana, cloud suggestions, advertising ID, search suggestions, feedback and tips prompts, tailored experiences, location, clipboard sync, background apps, silent installs, third-party suggestions |
| ⚙️ Services | 5 | Print Spooler, DiagTrack and other background services |
| 🚀 Startup | 4 | Fast startup, OneDrive, startup delay |
| 💾 Storage | 7 | TRIM, NTFS last-access, defragment scheduling, long path support |
| 📁 Explorer | 11 | File extensions, hidden files, recent documents, launch-to, compact mode, sync notifications, thumbnail previews, widgets, News and Interests, taskbar alignment, classic context menu |
| 🎨 Visual Effects | 7 | Transparency, animations, taskbar effects, tooltip delay, font smoothing |
| 🧹 Cleanup | 6 | Temp files, thumbnails, Windows Update cache, Delivery Optimization, recycle bin, DNS cache |

Every tweak carries a **risk level** (`Safe`, `Recommended`, `Optional`, `Experimental`, `Risky`, `Dangerous`, `Deprecated`, `Myth`) and an **evidence score from 0–5** — 5 means Microsoft documents the behaviour, 0 means it's folklore. The evidence score is what keeps a myth-laden tweak out of the safe profiles.

## Profiles

A profile is just a filter over the same catalogue — categories included, categories excluded, a maximum risk level, and a hardware tier window. Because of that, a profile on your machine applies whatever subset of it is actually compatible.

| Profile | Idea | Risk ceiling |
|---------|------|--------------|
| 🎮 Gaming | Max FPS, lowest latency | Optional |
| 🖥️ Daily Driver | Balanced, breaks nothing | Safe |
| 💼 Office / Productivity | Snappy UI, reliable updates | Safe |
| 📺 Streaming | Encoding headroom, upload focus | Optional |
| 👨‍💻 Developer | Fast builds, RAM left alone | Optional |
| 🏢 Workstation | CAD/rendering/VM resources | Optional |
| 🔋 Battery Saver | Uptime over speed | Safe |
| 🥔 Potato PC | Old hardware, background reduction | Safe |

On the machine this README was written against (desktop, mid tier), the compatible subset runs out to 7–31 tweaks per profile and 47 in total. The other eleven are filtered out: ten because the active power plan doesn't expose the setting they drive — `PROCCORES`, `PERFBOOSTMODE`, `PERFENERGYPERF`, `SYSFANPOL`, `SPEEDSTEP` — so they could never be applied there anyway, and one because `cleanup.deliveryOptimization` is deprecated, which the security guard refuses on every machine. Applying any of them blindly would just fail with `Invalid Parameters` from `powercfg`, or be blocked outright.

### What a profile will and won't do

A profile's categories often reach tweaks it is not entitled to apply on your behalf. Battery Saver, for example, is capped at `Safe` risk — so a `Recommended` tweak in its categories is not applied by default, and never silently applied either. Ask:

```bash
WinOpt.Cli profile-selector battery-saver
```

You get three lists: **applied by default**, **available if you want them**, and **not part of this profile** — grouped by the bar each tweak failed, so you can see whether a tweak was left out by the profile's policy or by the security guard. Tweak your way into the second list and pass it along:

```bash
WinOpt.Cli plan  --profile battery-saver --include "visual.menuDelay,visual animations"
WinOpt.Cli apply --profile battery-saver --include "visual.menuDelay,visual animations"
```

`--include` also works on `list`. An opt-in is still checked against the security guard and your hardware — if one cannot run, the command stops and tells you which, rather than quietly applying the rest.

The same split is what the Profiles page shows in the app.


## Command line

The CLI is the engine. Build it once and you can script Novimize like anything else.

```bash
WinOpt.Cli system --summary          # one-line hardware summary
WinOpt.Cli scan                      # current state of every compatible tweak
WinOpt.Cli list --category cpu-power # what's available in a category
WinOpt.Cli list --profile gaming     # what a profile would touch on THIS machine
WinOpt.Cli recommend --top 10        # ranked by evidence and impact for your tier
WinOpt.Cli profile                   # the eight built-in profiles
WinOpt.Cli profile-selector gaming   # what it applies, what it reaches, and why the rest is out
WinOpt.Cli plan all                  # what `apply all` would do, and what it would hold back
WinOpt.Cli plan --profile gaming     # ...for a profile
WinOpt.Cli plan id.a,id.b,id.c       # ...for an explicit list
WinOpt.Cli apply --profile daily     # apply a profile
WinOpt.Cli apply --profile daily --include id.a  # ...plus the opt-ins you ticked
WinOpt.Cli apply gaming.hags         # apply one tweak
WinOpt.Cli apply id.a,id.b           # apply a list as one planned batch
WinOpt.Cli apply all                 # apply every compatible tweak
WinOpt.Cli apply all --dry-run       # ...but only tell me what would happen
WinOpt.Cli journal --limit 20        # what this machine actually changed, newest first
WinOpt.Cli journal --result blocked  # only the things the planner held back
WinOpt.Cli rollback <tweak-id>       # undo one tweak from its snapshot
WinOpt.Cli rollback --all            # undo everything
WinOpt.Cli snapshots                 # list snapshots
WinOpt.Cli doctor bench              # health | network | startup | bench
```

`--json` works on every command and is what the desktop app consumes.

> **Breaking change to the `list --json` contract:** the per-tweak `profiles: string[]`
> key is gone. It was written into all 58 definitions that existed at the time and read
> by exactly one line — a recommendation boost — which disagreed with where the engine
> actually put the tweak in 53 of those 58. Membership is computed from category policy
> alone; use `profile-selector <id> --json` for the authoritative split.

### `plan` — preview before you touch anything

`plan` takes exactly the same selection arguments as `apply`, because it answers
"what would apply do to this selection" rather than a different question with
similar wording. It never runs a command.

A batch is resolved in three stages: declared `dependsOn` first, then target
collisions found by reading the specs, then an execution order. Anything held
back is listed with the reason — and `plan` exits `1`, so a script stops before
running an apply it did not expect.

```bash
$ WinOpt.Cli plan cpu-power.plan.ultimatePerformance,cpu-power.plan.highPerformance

  1 of 2 requested tweaks would run.

      1. cpu-power.plan.highPerformance

  Held back (1):
    Conflict              cpu-power.plan.ultimatePerformance
      'cpu-power.plan.highPerformance' writes a different value to the same target
      (powerplan:active); running both would leave only the winner in effect.
```

Two tweaks writing the same target to the same value are *not* a conflict —
that is idempotent, not a collision. A conflict means pick one, so exactly one
side is held. Cycles are reported rather than broken arbitrarily, because
choosing which member to cut would be an invisible decision.

<details>
<summary><code>WinOpt.Cli scan --json</code> — what a run looks like</summary>

```json
{
  "tweakId": "gpu.gaming.hags",
  "state": "NotApplied",
  "currentValue": null,
  "message": null,
  "detectionSucceeded": true
}
```

</details>

<details>
<summary><code>WinOpt.Cli apply gpu.gaming.hags --json</code> — run without admin rights</summary>

```json
{
  "tweaksAttempted": 1,
  "tweaksSucceeded": 0,
  "tweaksSkipped": 0,
  "tweaksFailed": 0,
  "tweaksNeedElevation": 1,
  "results": [
    {
      "tweakId": "gpu.gaming.hags",
      "status": "RequiresElevation",
      "message": "Elevation required to apply this tweak."
    }
  ]
}
```

`tweaksNeedElevation` is deliberately separate from `tweaksFailed`: the tweak is fine, your shell just isn't elevated. The command still exits `1` (something you asked for did not happen), and the reason is in the JSON so scripts can branch on it.

</details>

### Exit codes

| Code | Meaning |
|-----:|---------|
| 0 | Everything requested was applied (or was already applied) |
| 1 | Something you asked for did not happen — see `tweaksFailed`, `tweaksNeedElevation`, `tweaksBlocked`, or the message on stderr |

`tweaksBlocked` is deliberately separate from `tweaksFailed`: a held-back tweak
was never attempted, so it is untouched rather than broken. The JSON carries all
three counts, and `plan` on its own exits `1` whenever `hasIssues` is set —
before anything has run at all.


## Getting started

### Download

Grab the installer from [Releases](https://github.com/a2z05/novimize/releases). It needs no dependencies — .NET, WebView2 and the CLI sidecar are all bundled.

### Build it

Prerequisites: [Node.js](https://nodejs.org/) 18+, [Rust](https://rustup.rs/) (latest stable), [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/a2z05/novimize.git
cd novimize

# 1. CLI sidecar
cd src/cli
dotnet publish -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o bin/Release/net8.0/win-x64/publish
cd ../..

# 2. Desktop app
cd src/ui
npm install
npm run build          # tsc + vite
npx tauri build --bundles nsis
```

The NSIS installer and the portable folder land under `src/ui/src-tauri/target/release/bundle/` and `src/ui/src-tauri/target/release/`.

> **Note:** Tauri's bundler downloads NSIS and WebView2 tooling from GitHub on first build. If your network blocks that, set `http_proxy` and `https_proxy` before running `npx tauri build`.

### Run tests

```bash
dotnet test -c Release
```

### Try the CLI without building

```bash
src/cli/bin/Release/net8.0/win-x64/publish/WinOpt.Cli.exe scan
```

## How it works

```
┌──────────────────────────────────────────────────────┐
│                     Novimize                         │
├────────────────┬────────────────┬────────────────────┤
│  React UI      │  Tauri shell   │  .NET 8 CLI        │
│  TypeScript    │  Rust          │  C#                │
│                │                │                     │
│  Dashboard     │  Commands      │  System detection   │
│  Scan          │  IPC bridge    │  Tweak engine       │
│  Profiles      │  Sidecar spawn │  Providers          │
│  Snapshots     │  Permissions   │  Snapshots          │
│  Diagnostics   │                │  Recommendations    │
└────────────────┴────────────────┴────────────────────┘
                                     │
                              tweaks/*.json
```

A batch apply runs: **select → plan → snapshot → apply in order → verify per tweak**. The plan is computed before anything touches the system, and no snapshot is taken at all if everything in the run is held back — a snapshot of "nothing was attempted" is just noise in the rollback list. Verification re-runs detection after the change; if the new value didn't stick, the result is `VerificationFailed` rather than a quiet success.

Tweaks are dispatched to providers by their `method` field. The catalogue uses `Registry` (27), `PowerShell` (22), `Service` (5) and `NetSh` (4), each with a provider; `Script` and `TaskScheduler` are also wired up for when the catalogue grows into them. A method with no registered provider is a `Failure` naming that, never a silent skip — `Providers_CoverEveryMethodTheCatalogueUses` fails the build if a tweak is added for a method nothing handles. PowerShell apply and rollback runs are wrapped in per-statement exit guards so one rejected `powercfg` argument fails the command instead of scrolling past unnoticed; detection runs stay lenient so probes that legitimately return "not present" aren't reported as errors.

## Editing tweaks

Tweak definitions are plain JSON in [`tweaks/`](tweaks/). A minimal one:

```json
{
  "id": "category.subcategory.name",
  "name": "Human Readable Name",
  "description": "What this changes, and why you might want it",
  "category": "category",
  "risk": "Safe",
  "evidence": 4,
  "method": "Registry",
  "targetValue": "1",
  "defaultValue": "0",
  "profiles": ["gaming", "daily"],
  "detect": { "command": "...", "extractPattern": "..." },
  "apply": { "command": "..." },
  "rollback": { "command": "..." }
}
```

Optional fields worth knowing:

- `requiresPowerSetting` — the power-plan alias the tweak needs (e.g. `PERFBOOSTMODE`). If the active plan doesn't expose it, the tweak is filtered out instead of failing on apply.
- `minBuild` / `maxBuild` / `formFactor` / `gpuVendor` — hardware gates checked during filtering.
- `conflictsWith` / `dependsOn` — surfaced as warnings before a batch apply.

Detection and apply must read **the same setting**: same alias, same AC/DC row, same units. That sounds obvious and is the single most common source of a tweak that reports `PartiallyApplied` forever.

Research notes backing individual decisions live in [`docs/research/`](docs/research/).
The triage that produced the current catalogue — which of the 78 candidate additions
were built, which already existed, and which were declined with the evidence for each —
is in [`docs/superpowers/specs/2026-10-02-catalogue-expansion-triage.md`](docs/superpowers/specs/2026-10-02-catalogue-expansion-triage.md).

## Safety

- **Snapshots** — created automatically before every apply, listed with `snapshots`
- **Rollback** — per-tweak or everything, from any snapshot
- **Dry run** — `--dry-run` on any apply
- **Risk badges and evidence scores** — on every tweak, in the CLI and the UI
- **Security boundaries** — deprecated, myth-class and dangerous tweaks are filtered out of every apply run rather than attempted and failed
- **Elevation is explicit** — without admin rights, privileged tweaks are reported as `RequiresElevation`, never as a silent failure
- **Conflicts are held, not raced** — two tweaks writing the same target to different values cannot both report success; one is held back and the reason names the other. `plan` shows this before anything runs, and the confirm dialog offers which side to keep.
- **Every outcome is recorded** — `journal` shows successes, failures, skips and held-back entries alike, with the old and new value where the target is a registry value or a service start type. Torn lines from a crash mid-write are skipped rather than breaking the file.

Novimize changes real system settings. Nothing here is risk-free; that's why snapshots, dry runs and risk labels exist. Read the tweak you're about to apply.

## Project layout

```
winopt/
├── tweaks/                  # Tweak definitions (JSON — edit these)
├── src/
│   ├── cli/                 # .NET CLI entry point
│   ├── core/                # Models and interfaces
│   ├── engine/              # Detection, apply, snapshots, recommendations
│   ├── providers/           # Registry / Service / PowerShell / NetSh / …
│   └── ui/                  # React app + Tauri shell
├── tests/core/              # xUnit smoke tests
└── docs/research/           # Research notes behind the tweak catalogue
```

## Tech stack

- **Frontend** — React 19, TypeScript, Tailwind CSS v4, Vite
- **Shell** — Tauri v2 (Rust)
- **Engine** — C# on .NET 8, shipped as a self-contained single-file CLI
- **Tests** — xUnit
- **Installer** — NSIS

## Contributing

Open an issue or a PR. The highest-value contributions are usually:

1. **Corrections to a detect/apply pair** that reports the wrong state on your hardware
2. **A citation** that raises (or honestly, lowers) a tweak's evidence score
3. **A new tweak** — with a detect command that provably reads the same thing the apply command writes

## License

MIT — see [LICENSE](LICENSE).

## Credits

**Built by [a2z](https://github.com/a2z05)**

---

<div align="center">

*Novimize — because you should know exactly what's happening to your PC.*

</div>
