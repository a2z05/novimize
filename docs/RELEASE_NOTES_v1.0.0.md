# Novimize v1.0.0

The first version worth putting in front of people who didn't build it.

Novimize is a local-first Windows optimizer you can read: the catalogue of
changes is plain JSON in [`tweaks/`](https://github.com/a2z05/novimize/tree/main/tweaks),
every apply takes a snapshot first, and `--dry-run` tells you exactly what
would change without touching anything. What follows is what's in the box.

## The catalogue

- **85 tweaks across 10 categories** — CPU power, GPU gaming, network,
  privacy, services, startup, storage, explorer, visual effects, cleanup
- Every tweak carries a **risk level** and an **evidence score from 0–5** —
  5 means Microsoft documents the behaviour, 0 means folklore. That score is
  what keeps a myth-laden tweak out of the safe profiles.
- Tweaks research showed to be obsolete, placebo or disputed are **labelled,
  not quietly shipped as "performance"**. None claims a frame-rate win it
  cannot demonstrate.
- **8 profiles** — Gaming, Daily Driver, Office, Streaming, Developer,
  Workstation, Battery Saver, Potato PC — each with a risk ceiling. A
  profile is a filter, not a script, so it applies whatever subset is
  actually compatible with your machine.
- **Hardware detection** — CPU, RAM, GPU and storage tier drive which
  tweaks are suggested to you.
- **Batch planner** — a run is ordered, and anything that would fight
  another member of the same run is held back with the reason, before a
  single command executes.
- **Snapshot before every change**, and a before/after diff per entry with
  Keep and Undo. The values on screen are the values rollback reads.
- **Change journal** — append-only, with a result for every outcome
  including the ones held back. It's a tab in Diagnostics now, and exports
  with the active filter applied.

## Surfaces over Windows

Nine sections that read and change settings directly rather than through a
preset. Each has its own safety contract, and none of them is a one-click
"optimize".

| Section | What it does | The contract |
|---------|--------------|--------------|
| **Blocker** | Hosts rules, firewall rules, six blocklist sources | Only bytes between two markers are touched in the hosts file; only rules carrying `Novimize.Block.` are touched in the firewall; the original is backed up once, before the first write |
| **Network** | Per-adapter DNS, ping, traceroute, lookup, routes, Fix Network | The previous DNS is recorded before the first change, so revert puts back what was there; every reset shows its command lines first |
| **Power Center** | The active plan, eleven settings behind it, four plans | Reads from `powercfg`, not the registry, so an unset key means "inherits"; Ultimate Performance is never selected automatically on a laptop |
| **Startup** | What runs at sign-in — Run keys, both Startup folders, logon tasks | Writes the `StartupApproved` byte, the one Task Manager writes. Nothing is ever deleted |
| **Services** | Every service, its start mode, what depends on it | Twenty-two services are refused with the reason; stopping one others wait on names them first; there is no mass-disable |
| **Scheduled Tasks** | Trigger, last run, next run, HRESULT | Disabled, never deleted; a task's folder and name travel together because a name alone is ambiguous |
| **Debloat** | Installed Store packages with a verdict | Frameworks, Windows' own non-removable flag and a waiting dependent are refused by the engine; [`debloat/policy.json`](https://github.com/a2z05/novimize/tree/main/debloat/policy.json) is an opinion and cannot override them |
| **Maintenance** | Caches, DISM, SFC | Every action shows its size, what it deletes and the exact commands before it runs |
| **Windows Update** | State, history, what is owed | Nothing writes update policy. The only writes are opening Settings and scheduling a restart |

## Gaming, apps, appearance

- **Gaming Center** — launcher and game detection, plus a temporary game mode
  that restores in reverse order, and Defender exclusions with a restore path.
- **App Installer** — a winget catalogue in 8 categories where every package
  ID was checked against winget before it was written down. `winget: false`
  means official-site only, and the CLI refuses to run winget against those
  IDs rather than guessing.
- **Appearance** — a catalogue of 20 customization tools. Tiles are generated
  monograms and links point at official sites and repos only. No bundled
  third-party artwork.

## Everything else

- **Health report** — one pass over security, storage, updates, memory,
  Defender, firewall profiles, drive health, activation, power plan and
  startup count. Export as JSON, plain text or HTML from the same code the
  page shows.
- **Ctrl+K** — one box over pages, actions, apps and tweaks. It finds things
  and opens them; it never applies a tweak.
- **Every error names an action and a cause.** "Something went wrong" does
  not appear anywhere in the source.
- **Updater** — checks the published release and verifies what it fetches
  against GitHub's own SHA-256. Check-only by default; it never executes a
  download.
- **CLI and GUI share one engine.** The desktop app is a shell around the same
  .NET CLI, so what you see in the UI is what the command line does.

```bash
Novimize scan                    # current state of every compatible tweak
Novimize apply --profile gaming  # apply a profile
Novimize plan --dry-run          # what a run would do, touching nothing
Novimize rollback                # put a session back
Novimize health status           # one report over security, storage, updates
Novimize blocker sources         # blocklists Novimize knows how to fetch
```

## Install

| | |
|---|---|
| **Installer** | `Novimize_1.0.0_x64-setup.exe` — no dependencies, .NET/WebView2/CLI sidecar all bundled |
| **Portable** | `Novimize_1.0.0_portable.zip` — unzip and run `Novimize\Novimize.exe` |

Windows 10/11 x64.

**Upgrading from v0.1.0:** run `uninstall.exe` from your install folder
first, then install v1.0.0 over it. Installing the same version number over
an existing copy can leave stale files behind.

## On the network

"No network calls" was too strong a claim, and this release says what is
actually true. Four things reach the network, and each one says so before it
does:

- fetching a blocklist, from the address in [`blocklists/sources.json`](https://github.com/a2z05/novimize/blob/main/blocklists/sources.json)
- checking GitHub for a newer Novimize
- Windows Update's own search
- the public-IP lookup — which only happens when you press it

No accounts, no telemetry, nothing else. Every external source is named,
explicit and verifiable.

## What v1.0.0 is not

A licence to trust it blindly. It's 85 tweaks and a set of surfaces that
change system state, and the point of the design is that you can read every
one of them before it runs. `plan --dry-run` first. Nothing here will
disable Windows Update, Defender, UAC, SmartScreen or the firewall as a
default preset, and nothing will present itself as an activation bypass —
that isn't a feature, it's a liability.

---