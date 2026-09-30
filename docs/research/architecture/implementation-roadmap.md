# WinOpt Implementation Roadmap

## Phase 0: Research (Current)
**Status:** IN PROGRESS (~80% complete)

### Deliverables
- [x] Master research index
- [x] Core engine architecture
- [x] Tweak definition schema
- [x] Snapshot & rollback system design
- [x] Auto-optimize rules & safety criteria
- [x] Preset & recommendation engine design
- [x] Security model
- [x] CLI design
- [ ] Windows internals research
- [ ] Reference project analysis
- [ ] Networking research (deep)
- [ ] CPU/GPU research
- [ ] Memory/Storage research
- [ ] Services research
- [ ] Privacy/Security research
- [ ] Gaming research
- [ ] Cleanup/Debloat research
- [ ] USB/Bluetooth/PCIe research
- [ ] Diagnostics research
- [ ] Tweak taxonomy & evidence framework

---

## Phase 1: Core Engine (C#/.NET 8+)
**Estimated:** 2-3 weeks
**Depends on:** Phase 0 complete

### 1.1 Project Setup
- Create .NET 8 class library project
- Solution structure: WinOpt.Core, WinOpt.Providers, WinOpt.Engine
- NuGet dependencies: System.Management (WMI), Microsoft.Win32.Registry
- Build configuration for single-file publish
- Unit test project setup

### 1.2 System Detection Module
- Windows version/build/edition detection
- Hardware inventory (CPU, GPU, RAM, disks, NICs)
- Active power plan detection
- Network adapter enumeration
- Running services snapshot
- Installed AppX packages
- Startup items enumeration
- Current system state baseline

### 1.3 Tweak Definition Loader
- JSON schema validation
- Tweak database loader
- Tweak filtering by compatibility
- Dependency resolver
- Conflict detector

### 1.4 State Engine
- Registry state reader
- Service state reader
- powercfg state reader
- netsh state reader
- PowerShell state reader
- WMI/CIM state reader
- Composite state detector per tweak

### 1.5 Apply Engine
- Registry writer with backup
- Service modifier with backup
- powercfg executor
- netsh executor
- PowerShell script executor
- DISM executor
- AppX manager
- Ordered execution with dependency resolution
- Atomic operations with rollback-on-failure

### 1.6 Rollback Engine
- Per-tweak rollback
- Category rollback
- Session rollback
- Full restore
- Registry rollback (create vs modify vs delete)
- Service rollback (start type + running state)
- netsh rollback
- powercfg rollback

### 1.7 Verification Engine
- Post-apply state re-detection
- Comparison with expected state
- Verification result reporting
- Auto-rollback trigger on failure

### 1.8 Snapshot Engine
- Snapshot creation (JSON serialization)
- Snapshot storage (%LOCALAPPDATA%\WinOpt\snapshots\)
- Snapshot loading and parsing
- Snapshot integrity checking (SHA256)
- Snapshot retention policy
- Index management

### 1.9 Logging Engine
- Structured logging (JSON)
- Log levels (DEBUG, INFO, WARN, ERROR)
- File rotation
- Export capability
- Operation audit trail

### 1.10 IPC Server
- Named pipe server for Tauri communication
- JSON message protocol
- Request/response handling
- Error propagation
- Elevation request protocol

---

## Phase 2: Tweak Providers (C#/.NET)
**Estimated:** 4-6 weeks (parallelizable)
**Depends on:** Phase 1 complete

### 2.1 Registry Provider
- Registry read/write/delete operations
- Value type handling (DWORD, SZ, QWORD, BINARY, MULTI_SZ, EXPAND_SZ)
- Original state capture
- Rollback via value restoration or deletion

### 2.2 Power Provider
- Power plan enumeration and selection
- Processor power settings (min/max, boost, EPP, parking)
- Sleep/hibernate configuration
- USB selective suspend
- PCI Express link state
- Power button behavior
- Lid close behavior (laptop)

### 2.3 Network Provider
- TCP/IP stack configuration (netsh)
- NIC advanced properties
- DNS configuration
- Wi-Fi power management
- Wi-Fi advanced properties
- Delivery Optimization
- MMSCSS network throttling
- Network profile management
- Interface metrics

### 2.4 Services Provider
- Service state detection
- Service start type modification
- Service start/stop control
- Dependency analysis
- Reverse dependency check
- Service recovery configuration

### 2.5 Startup Provider
- Registry Run key management
- Startup folder enumeration
- Scheduled task startup detection
- Startup impact assessment

### 2.6 Storage Provider
- TRIM verification
- Write caching configuration
- NTFS optimization
- Storage Sense configuration
- Disk cleanup automation
- Optimize-Volume (defrag/TRIM)

### 2.7 Privacy Provider
- Telemetry level configuration
- Advertising ID management
- Activity history settings
- Cortana configuration
- Cloud content settings
- Diagnostic data viewer

### 2.8 Visual Effects Provider
- VisualFXSetting configuration
- UserPreferencesMask management
- Individual visual effect toggles
- Menu animation delay
- Explorer visual effects

### 2.9 Windows Features Provider
- DISM feature enumeration
- Feature enable/disable
- Feature removal
- PowerShell equivalent operations

### 2.10 AppX Provider
- AppX package enumeration
- Package removal (user and all-users)
- Provisioned package management
- Package reinstallation
- System app protection

### 2.11 Cleanup Provider
- Temp folder cleanup
- Thumbnail cache cleanup
- DNS cache flush
- Windows Update cache cleanup
- Delivery Optimization cache
- Crash dump cleanup
- Recycle bin management

### 2.12 Graphics Provider
- HAGS configuration
- Fullscreen optimizations
- Game Mode / Game DVR
- VRR settings
- Per-app GPU preferences
- Shader cache management
- MPO configuration
- GPU power management

### 2.13 Gaming Provider
- Game Mode system settings
- Game Bar configuration
- MMSSS task profiles
- Timer resolution (where safe)

### 2.14 Bluetooth Provider
- Bluetooth adapter power management
- Bluetooth audio settings

### 2.15 USB Provider
- USB selective suspend
- USB hub power management

### 2.16 Scheduled Tasks Provider
- Task enumeration
- Task disable/enable
- Task condition modification

---

## Phase 3: Recommendation & Preset Engine
**Estimated:** 1-2 weeks
**Depends on:** Phase 2 complete

### 3.1 Recommendation Engine
- Hardware-aware scoring algorithm
- OS compatibility filtering
- State-based filtering (skip already-applied)
- Evidence-weighted ranking
- Preset alignment scoring
- Conflict penalty calculation

### 3.2 Preset Definitions
- Define all presets (Safe, Balanced, Gaming, Max Performance, Low Latency, Max Throughput, Laptop, Workstation, Developer, Streaming, Privacy, Cleanup)
- Map tweaks to presets
- Preset conflict rules
- Preset presets (meta-presets)

### 3.3 Auto-Optimize Engine
- Safety criteria evaluator
- Mode-specific filtering
- Dry-run generation
- Execution orchestration
- Post-execution verification
- System health check

### 3.4 Conflict Engine
- Direct contradiction detection
- Resource conflict detection
- Security vs performance conflict detection
- Dependency chain validation
- Resolution recommendation

---

## Phase 4: App Installer
**Estimated:** 1 week
**Depends on:** Phase 1 complete (can run in parallel with Phase 2)

### 4.1 Winget Integration
- Winget availability detection
- Package search and listing
- Package installation
- Package upgrade
- Package uninstallation
- Package verification

### 4.2 Package Registry
- Curated app catalog (JSON)
- Categories: Browsers, Gaming, Utilities, Media, Dev, etc.
- Package metadata (name, publisher, winget ID, description)
- Install detection logic

### 4.3 Installation Engine
- Silent install support
- Progress tracking
- Rollback on failure
- Post-install verification

---

## Phase 5: Diagnostics
**Estimated:** 1-2 weeks
**Depends on:** Phase 1 complete (can run in parallel with Phase 2)

### 5.1 System Health Check
- Critical service status
- Disk health (SMART)
- Memory health
- CPU thermal status
- Event log errors
- System file integrity (sfc/DISM)

### 5.2 Network Diagnostics
- Adapter status
- IP configuration
- DNS resolution
- Connectivity test (ping, tracert)
- DNS latency measurement
- Network speed estimation
- TCP configuration audit

### 5.3 Startup Diagnostics
- Startup item inventory
- Impact assessment
- Slow startup identification
- Recommended startup changes

### 5.4 Benchmarking
- Pre/post measurement framework
- Boot time estimation
- Application launch time
- Network throughput measurement
- Disk performance measurement
- Idle CPU/RAM measurement

---

## Phase 6: Tauri Frontend (Rust + Web UI)
**Estimated:** 4-6 weeks
**Depends on:** Phase 1-3 complete

### 6.1 Tauri Project Setup
- Rust + Tauri 2.x project
- Web UI framework selection (React/Svelte/Vue)
- IPC bridge to .NET engine
- Elevation handling

### 6.2 Dashboard
- System overview
- Quick actions
- Health status
- Applied tweaks count

### 6.3 Optimization Browser
- Category navigation
- Tweak cards with state indicators
- Search and filter
- Dry-run preview
- Apply/rollback controls

### 6.4 Preset Selector
- Visual preset cards
- Preset preview
- One-click apply

### 6.5 Auto-Optimize UI
- Mode selection
- Preview changes
- Confirmation dialog
- Progress tracking
- Results summary

### 6.6 Network Section
- Adapter overview
- NIC properties editor
- TCP/IP configuration
- Network diagnostics

### 6.7 Cleanup UI
- Cleanup target selection
- Size estimation
- Cleanup progress

### 6.8 App Installer UI
- App catalog browser
- Search
- Install/uninstall controls
- Installed apps management

### 6.9 Diagnostics UI
- System health dashboard
- Network diagnostics
- Startup manager
- Benchmark results

### 6.10 History & Snapshots
- Change log viewer
- Snapshot browser
- Rollback controls

### 6.11 Settings
- App configuration
- Auto-optimize scheduling
- Log management
- Theme/appearance

---

## Phase 7: CLI
**Estimated:** 1 week
**Depends on:** Phase 1-3 complete

### 7.1 CLI Framework
- Rust CLI (clap)
- Command routing
- Output formatting (human, JSON, table)
- Exit codes

### 7.2 Commands Implementation
- All commands from CLI design document
- IPC bridge to .NET engine

---

## Phase 8: Polish & Release
**Estimated:** 2-3 weeks
**Depends on:** All phases complete

### 8.1 Testing
- Unit tests for all providers
- Integration tests
- Windows 10/11 compatibility testing
- Home/Pro/Enterprise testing
- Desktop/Laptop testing
- Intel/AMD/NVIDIA testing

### 8.2 Documentation
- User documentation
- Technical documentation
- CLI help pages
- Tweak database documentation

### 8.3 Packaging
- Windows installer (MSI or Inno Setup)
- Portable mode
- Auto-update mechanism
- Code signing

### 8.4 Release
- GitHub release
- Documentation site
- User feedback collection

---

## Parallel Execution Opportunities

### Track A (Engine)
Phase 1 → Phase 2 → Phase 3 → Phase 6/7

### Track B (Installer + Diagnostics)
Phase 4 → (independent)
Phase 5 → (independent)

### Track C (Research)
Phase 0 → (completes before Phase 1)

---

## Risk Areas

1. **Windows Version Diversity** — Registry paths, PowerShell commands, and available settings vary across Windows 10/11 builds. Extensive testing required.

2. **Driver Vendor Differences** — NIC advanced properties, GPU settings, and power management vary by vendor. Fallback to generic detection needed.

3. **.NET + Rust IPC** — Cross-language IPC adds complexity. Named pipe protocol must be robust and well-tested.

4. **Elevation Handling** — Many operations require admin. The Tauri → .NET elevation flow must be seamless.

5. **Rollback Reliability** — Some changes are hard to perfectly reverse (AppX removal, feature removal). Must be clearly documented.

6. **Windows Update Reversion** — Some tweaks may be reset by Windows Update. Must detect and warn.

7. **Security Boundary** — Must NEVER accidentally weaken security. Hard-coded protection lists needed.

8. **Testing Matrix** — Windows 10/11 × Home/Pro/Enterprise × Desktop/Laptop × Intel/AMD/NVIDIA = massive test matrix. Prioritize most common configurations.
