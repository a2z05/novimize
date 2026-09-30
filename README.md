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

- **58 tweaks across 9 categories** — CPU power, GPU, network, privacy, services, startup, storage, visual effects, cleanup
- **Hardware detection** — CPU, RAM, GPU and storage tier drive which tweaks are suggested to you
- **8 profiles** — Gaming, Daily Driver, Office, Streaming, Developer, Workstation, Battery Saver, Potato PC
- **Snapshot before every change** — one command rolls a tweak or a whole session back
- **Dry-run everywhere** — `--dry-run` shows exactly what would change without touching the system
- **Capability filtering** — a tweak your machine can't take (wrong power plan, laptop-only, no matching GPU), or one the security guard refuses, is filtered out of scans and lists rather than failing halfway through an apply
- **CLI and GUI share one engine** — the desktop app is a shell around the same .NET CLI, so what you see in the UI is what the command line does
- **Local-first** — no accounts, no network calls, no telemetry

## Tweak categories

| Category | Tweaks | What it covers |
|----------|-------:|----------------|
| ⚡ CPU Power | 13 | Core parking, minimum/maximum processor state, turbo boost, cooling policy, Speed Shift, power plans |
| 🎮 GPU Gaming | 8 | Hardware-accelerated GPU scheduling, fullscreen optimisations, shader cache, GameDVR, driver-level latency |
| 🌐 Network | 5 | TCP auto-tuning, RSC, window-scaling heuristics, DNS, DCA |
| 🔒 Privacy | 8 | Telemetry level, activity history, Cortana, cloud suggestions, advertising ID |
| ⚙️ Services | 5 | Print Spooler, DiagTrack and other background services |
| 🚀 Startup | 4 | Fast startup, OneDrive, startup delay |
| 💾 Storage | 6 | TRIM, NTFS last-access, defragment scheduling |
| 🎨 Visual Effects | 3 | Transparency, animations, taskbar effects |
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

## Command line

The CLI is the engine. Build it once and you can script Novimize like anything else.

```bash
WinOpt.Cli system --summary          # one-line hardware summary
WinOpt.Cli scan                      # current state of every compatible tweak
WinOpt.Cli list --category cpu-power # what's available in a category
WinOpt.Cli list --profile gaming     # what a profile would touch on THIS machine
WinOpt.Cli recommend --top 10        # ranked by evidence and impact for your tier
WinOpt.Cli profile                   # the eight built-in profiles
WinOpt.Cli apply --profile daily     # apply a profile
WinOpt.Cli apply gaming.hags         # apply one tweak
WinOpt.Cli apply all                 # apply every compatible tweak
WinOpt.Cli apply all --dry-run       # ...but only tell me what would happen
WinOpt.Cli rollback <tweak-id>       # undo one tweak from its snapshot
WinOpt.Cli rollback --all            # undo everything
WinOpt.Cli snapshots                 # list snapshots
WinOpt.Cli doctor bench              # health | network | startup | bench
```

`--json` works on every command and is what the desktop app consumes.

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
| 1 | Something you asked for did not happen — see `tweaksFailed`, `tweaksNeedElevation`, or the message on stderr |

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

A single apply runs: **apply → detect → apply → snapshot → verify**. Verification re-runs detection after the change; if the new value didn't stick, the result is `VerificationFailed` rather than a quiet success.

Tweaks are dispatched to providers by their `method` field — `Registry`, `Service`, `PowerCfg`, `NetSh`, `PowerShell`, `Dism`, `AppX`, `TaskScheduler`, `Script`. PowerShell apply and rollback runs are wrapped in per-statement exit guards so one rejected `powercfg` argument fails the command instead of scrolling past unnoticed; detection runs stay lenient so probes that legitimately return "not present" aren't reported as errors.

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

## Safety

- **Snapshots** — created automatically before every apply, listed with `snapshots`
- **Rollback** — per-tweak or everything, from any snapshot
- **Dry run** — `--dry-run` on any apply
- **Risk badges and evidence scores** — on every tweak, in the CLI and the UI
- **Security boundaries** — deprecated, myth-class and dangerous tweaks are filtered out of every apply run rather than attempted and failed
- **Elevation is explicit** — without admin rights, privileged tweaks are reported as `RequiresElevation`, never as a silent failure

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
