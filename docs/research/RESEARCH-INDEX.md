# WinOpt Phase 0 Research Index

## Status: COMPLETE ✅

All research documents for WinOpt Phase 0 have been written. The project is ready to proceed to Phase 1 (Core Engine implementation).

---

## Architecture & Design (10 files)

| File | Lines | Description |
|------|-------|-------------|
| [core-engine.md](architecture/core-engine.md) | 232 | Engine architecture: process model, provider system, IPC protocol |
| [tweak-schema.md](architecture/tweak-schema.md) | 397 | JSON schema for tweak definitions |
| [snapshot-rollback.md](architecture/snapshot-rollback.md) | 361 | Snapshot JSON schema, 8 rollback strategies |
| [auto-optimize.md](architecture/auto-optimize.md) | 247 | Auto-apply requirements, 5 modes, safety guards |
| [presets-recommendations.md](architecture/presets-recommendations.md) | 314 | 12 preset definitions, scoring algorithm |
| [security-model.md](architecture/security-model.md) | 232 | Hard boundaries for protections never disabled |
| [cli-design.md](architecture/cli-design.md) | 354 | 14 CLI commands, output formats, exit codes |
| [implementation-roadmap.md](architecture/implementation-roadmap.md) | 484 | 8-phase roadmap |
| [logging-benchmarking.md](architecture/logging-benchmarking.md) | 342 | Log levels, benchmarking strategy |
| [optimization-profiles.md](architecture/optimization-profiles.md) | 1096 | 8 hardware-aware profiles with detection logic |

## Services & Features (1 file)

| File | Lines | Description |
|------|-------|-------------|
| [services-comprehensive.md](services-features/services-comprehensive.md) | 402 | 5-tier classification, safety rules, dependency chains, management commands |

## Network (2 files)

| File | Lines | Description |
|------|-------|-------------|
| [networking-optimization.md](network/networking-optimization.md) | 1411 | TCP/IP stack, Nagle, DNS, NIC properties, MTU, Delivery Optimization |
| [network-optimizer-repos.md](network/network-optimizer-repos.md) | 365 | 10 GitHub network optimizer projects analyzed |

## CPU & GPU (2 files)

| File | Lines | Description |
|------|-------|-------------|
| [cpu-power-management.md](cpu-gpu/cpu-power-management.md) | 739 | Power plans, CPU frequency tech, Desktop vs Laptop, risk assessment |
| [gpu-optimization.md](cpu-gpu/gpu-optimization.md) | 848 | GPU optimization: HAGS, Game Mode, NVIDIA/AMD specific settings |

## Memory & Storage (2 files)

| File | Lines | Description |
|------|-------|-------------|
| [memory-management.md](memory-storage/memory-management.md) | 783 | Pagefile myths, DisablePagingExecutive, memory compression, NUMA |
| [storage-optimization.md](memory-storage/storage-optimization.md) | 1313 | TRIM, write caching, NTFS, Storage Sense, NVMe power states |

## Privacy & Security (2 files)

| File | Lines | Description |
|------|-------|-------------|
| [privacy-telemetry-reference.md](privacy-security/privacy-telemetry-reference.md) | 1338 | Telemetry levels, advertising ID, activity history, Cortana |
| [security-boundaries.md](privacy-security/security-boundaries.md) | 342 | 25 never-disable protections with detection commands |

## Gaming (1 file)

| File | Lines | Description |
|------|-------|-------------|
| [gaming-optimization.md](gaming/gaming-optimization.md) | 1121 | Game Mode, Game DVR, GPU tweaks, timer resolution, vendor-specific |

## Cleanup & Debloat (2 files)

| File | Lines | Description |
|------|-------|-------------|
| [cleanup-targets.md](cleanup-debloat/cleanup-targets.md) | 1292 | 20 non-destructive cleanup targets with commands |
| [visual-effects-registry.md](cleanup-debloat/visual-effects-registry.md) | 960 | VisualFXSetting, UserPreferencesMask, 19 effects, DWM settings |

## Windows Internals (3 files)

| File | Lines | Description |
|------|-------|-------------|
| [wmi-cim-reference.md](windows/wmi-cim-reference.md) | 287 | CIM classes, detection patterns, hardware tier calculation |
| [features-appx.md](windows/features-appx.md) | 915 | DISM features, AppX packages, removal safety, edition differences |
| [appx-package-inventory.md](windows/appx-package-inventory.md) | 1277 | Complete AppX package inventory (47+ packages) with removal guidance |

## Scheduled Tasks (2 files)

| File | Lines | Description |
|------|-------|-------------|
| [powershell-scheduled-tasks.md](scheduled-tasks/powershell-scheduled-tasks.md) | 1300 | All 15 PowerShell scheduled task cmdlets |
| [scheduled-tasks-reference.md](scheduled-tasks/scheduled-tasks-reference.md) | 1639 | Task Scheduler API, schtasks, safety assessment, XML format |

## USB & Bluetooth (1 file)

| File | Lines | Description |
|------|-------|-------------|
| [usb-bluetooth-search.md](usb-bluetooth/usb-bluetooth-search.md) | 961 | USB selective suspend, Bluetooth power, Windows Search indexing |

## Reference Projects (1 file)

| File | Lines | Description |
|------|-------|-------------|
| [reference-projects-analysis.md](reference-projects/reference-projects-analysis.md) | 767 | 16 optimization tools analyzed, design insights |

---

## Summary

| Category | Files | Total Lines |
|----------|-------|-------------|
| Architecture & Design | 10 | 4,060 |
| Services & Features | 1 | 402 |
| Network | 2 | 1,776 |
| CPU & GPU | 2 | 1,587 |
| Memory & Storage | 2 | 2,096 |
| Privacy & Security | 2 | 1,680 |
| Gaming | 1 | 1,121 |
| Cleanup & Debloat | 2 | 2,252 |
| Windows Internals | 3 | 3,479 |
| Scheduled Tasks | 2 | 2,939 |
| USB & Bluetooth | 1 | 961 |
| Reference Projects | 1 | 767 |
| **TOTAL** | **30** | **~23,120** |

---

## Key Architecture Decisions (from research)

1. **Tech Stack**: Rust + Tauri (UI shell) + C#/.NET 8+ (deep Windows API helper) + Named Pipe IPC
2. **Tweak Schema**: JSON-based data-driven definitions with detection, apply, rollback, verification, evidence scoring (0-5), risk classification
3. **State Detection**: Always reads actual Windows state via registry, WMI/CIM, netsh, powercfg, sc.exe — never remembers what the app did
4. **Snapshot System**: JSON snapshots with SHA256 integrity, 8 rollback strategies
5. **Security Boundaries**: 25 protections classified as Never-Silently-Disable or Always-Warn
6. **Optimization Profiles**: 8 hardware-aware profiles (Gaming, Potato PC, Office, Daily, Streaming, Developer, Workstation, Battery Saver) with auto-detection
7. **Known Myths Excluded**: Disable pagefile (MYTH), Disable Superfetch on SSD (MYTH), Disable CPU parking for gaming (MYTH)
8. **No Telemetry, No Cloud** for core operation — local-first

---

## Next Phase

**Phase 1: Core Engine (C#/.NET 8+)** — See [implementation-roadmap.md](architecture/implementation-roadmap.md) for the full 8-phase plan.
