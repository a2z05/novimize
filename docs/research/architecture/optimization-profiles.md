# WinOpt Optimization Profiles Architecture

## Overview

Optimization Profiles extend the existing preset system (`presets-recommendations.md`) with hardware-aware auto-detection, dynamic switching, and per-tier adjustments. Where presets are static collections of tweak IDs, profiles are living configurations that adapt their tweak selection, intensity, and safety thresholds based on detected hardware capabilities and real-time system context.

This document defines the eight system profiles, the hardware tier detection engine, the auto-selection algorithm, the profile configuration schema, and the auto-switching runtime.

---

## Relationship to Existing Systems

```
Profile Definitions -> Hardware Detection -> Tier Classification
  -> Profile Selection (auto or user) -> Tweak Resolution
    -> Core Engine (Apply/Verify/Rollback)
```

Profiles sit above the existing preset and auto-optimize systems. A profile selects a base preset, applies hardware-tier adjustments, then feeds the resolved tweak set into the core engine for application. The core engine, providers, snapshot system, and rollback machinery remain unchanged.

---

## 1. System Use Profiles

### 1.1 Gaming

**Purpose:** Minimize latency, maximize frame rates, and prioritize GPU/CPU throughput for real-time gaming workloads.

| Dimension | Configuration |
|---|---|
| **Power Plan** | Ultimate Performance (desktop); High Performance (laptop on AC) |
| **CPU Boost** | Aggressive — processor performance boost mode set to 2 (aggressive) for all core counts; C-states limited to C1 minimum; core parking disabled |
| **Visual Effects** | Best Performance — all animations, shadows, and transparency disabled; fade/slide transitions set to 0ms; Explorer set to Best Performance VisualFXSetting |
| **Services** | Aggressive disable: SysMain (Superfetch), DiagTrack, WSearch (temporarily), Xbox services (if not used), Print Spooler (if no printer), Fax, MapsBroker, RemoteRegistry; keep: WaaSMedicSvc (Windows Update repair), cryptsvc, RpcSs |
| **Network** | Nagle algorithm disabled (TcpAckFrequency=1, TCPNoDelay=1 per interface); MMSCSS NetworkThrottlingIndex=ffffffff; TCP auto-tuning normal; disable Wi-Fi power saving; disable flow control on Ethernet; disable energy-efficient Ethernet; disable interrupt moderation on supported NICs |
| **Storage** | TRIM scheduled weekly; disable HDD indexing; write caching enabled; disable last access time update; NTFS memory usage set to 512KB for gaming drives |
| **Cleanup** | Disable Delivery Optimization upload sharing; clear shader cache monthly; disable background disk optimization during gameplay |
| **Risk Level** | MODERATE — aggressive power plan increases heat; Nagle disable may affect some non-gaming TCP behavior; Wi-Fi power saving off increases battery drain on laptops |

**Profile-Specific Tweaks:**
- Game Mode: enabled
- Game DVR: disabled (reduces recording overhead)
- HAGS (Hardware-Accelerated GPU Scheduling): enabled on supported hardware
- Timer Resolution: set to 1ms via `timeBeginPeriod` (applied per-game, not system-wide)
- MMCSS SystemResponsiveness: 10 (reduces background CPU allocation during gaming)
- Fullscreen Optimizations: disabled per-app for known titles
- MPO (Multi-Plane Overlay): disabled if causing issues on AMD; enabled on NVIDIA

**Hardware Adjustments:**
- If GPU tier is Integrated: skip HAGS, skip MPO, reduce MMCSS aggressiveness, warn that gaming performance will be limited regardless of profile
- If GPU tier is Low: skip timer resolution tweaks, use High Performance instead of Ultimate
- If RAM tier is Low (4GB): disable SysMain but warn about disk usage increase; recommend 8GB minimum for this profile
- If storage tier is HDD: recommend SSD upgrade; disable defrag scheduling, run weekly TRIM equivalent if SSD detected in mixed config

### 1.2 Potato PC (Low-End)

**Purpose:** Maximize responsiveness on constrained hardware (4 cores or fewer, 4GB RAM or less, HDD or slow SSD).

| Dimension | Configuration |
|---|---|
| **Power Plan** | Balanced (prevents thermal throttling on limited cooling); minimum processor state 5%, maximum 100% |
| **CPU Boost** | Disabled — boost mode set to 0 (disabled) to prevent thermal throttling and reduce power spikes on limited VRM |
| **Visual Effects** | Best Performance — disable all visual effects, animations, shadows, transparency, font smoothing; set VisualFXSetting=2 in registry; disable acrylic, blur, and mica effects |
| **Services** | Aggressive disable: SysMain, DiagTrack, WSearch, Xbox services, Print Spooler, Fax, MapsBroker, RemoteRegistry, BITS (Background Intelligent Transfer), Wisvc, RetailDemo, WMPNetworkSvc, lfsvc (geolocation), TrkWks (distributed link tracking), dmwappushservice; keep: wuauserv (Windows Update), CryptSvc, RpcSs, DcomLaunch |
| **Network** | Minimal changes — TCP auto-tuning normal; disable Windows Update delivery optimization; reduce background network usage |
| **Storage** | TRIM enabled if SSD detected; disable Windows Search indexing; disable defrag on SSD; Storage Sense enabled with aggressive cleanup schedule (weekly temp cleanup, monthly large file notification); disable hibernation if RAM is 4GB or less to reclaim disk space |
| **Cleanup** | Aggressive temp cleanup; clear thumbnail cache; disable Windows Tips notifications; disable suggested apps; disable lock screen spotlight; clear Windows Update cache monthly |
| **Risk Level** | MODERATE — aggressive service disable may affect some features; disabling BITS may slow Windows Updates; WSearch disable removes file search functionality |

**Profile-Specific Tweaks:**
- Disable startup delay for all apps
- Set processor scheduling to optimize foreground apps
- Reduce reserved processor capacity for background tasks
- Disable Windows transparency effects at registry level
- Set explorer to open to "This PC" instead of Quick Access
- Disable Windows Tips and suggestions
- Disable lock screen apps and notifications
- Disable Activity History
- Disable Find My Device

**Hardware Adjustments:**
- If RAM tier is Very Low (<4GB): disable pagefile managed by system; set pagefile to fixed 1024MB; disable memory compression; disable SysMain
- If RAM tier is Low (4GB): set pagefile to 2048-4096MB fixed; disable memory compression
- If storage tier is HDD: enable scheduled defrag (weekly); disable Superfetch entirely; disable Windows Search indexing completely; set large NTFS memory allocation
- If CPU tier is Very Low (2 cores): disable all background indexing; set MMCSS to minimal; maximum background task restrictions

### 1.3 Office/Productivity

**Purpose:** Stable, pleasant daily work experience with battery consideration for laptops. Prioritize smooth animations and system reliability over raw performance.

| Dimension | Configuration |
|---|---|
| **Power Plan** | Balanced (desktop and laptop AC); Power Saver (laptop on battery) |
| **CPU Boost** | Default — processor performance boost mode at system default (typically 2 for AC, 0 for battery) |
| **Visual Effects** | Let Windows choose (default) — smooth animations, font smoothing, thumbnail previews, fade effects enabled; preserves visual quality for extended work sessions |
| **Services** | Minimal changes — only disable clearly unnecessary services: Fax, RemoteRegistry, RetailDemo, MapsBroker, lfsvc; keep: all print services (if printing), all audio services, BITS, WaaSMedicSvc, WSearch, SysMain |
| **Network** | Conservative — TCP auto-tuning normal; DNS caching optimized; disable unused adapter protocols; Wi-Fi power saving optimized for battery |
| **Storage** | Storage Sense enabled (monthly cleanup, auto-delete temp files, one month recycle bin retention); TRIM scheduled monthly; keep indexing enabled for file search |
| **Cleanup** | Light — Storage Sense handles routine cleanup; disable Windows Tips; disable suggested apps in Start menu |
| **Risk Level** | LOW — minimal changes, conservative approach, high reliability |

**Profile-Specific Tweaks:**
- Power plan: Balanced with optimized sleep timers (10 min display off, 30 min sleep on AC)
- Disable Game Bar and Game DVR (not needed for office work)
- Enable Clipboard History (productivity feature)
- Enable Focus Assist default schedule (work hours)
- Disable Startup delay for faster boot after updates
- Set Explorer to open to Quick Access (productivity default)

**Hardware Adjustments:**
- If laptop: enable power-saving Wi-Fi settings; enable USB selective suspend; enable PCI Express link state power management
- If RAM tier is Low: consider disabling SysMain with warning about increased disk reads
- If storage tier is HDD: enable Storage Sense with more aggressive cleanup; schedule weekly defrag

### 1.4 Daily/General Usage

**Purpose:** General-purpose profile for web browsing, media consumption, casual applications. Light optimizations that improve the default Windows experience without aggressive changes.

| Dimension | Configuration |
|---|---|
| **Power Plan** | Balanced (default Windows power plan, no modification) |
| **CPU Boost** | Default — no changes to boost behavior |
| **Visual Effects** | Default Windows settings — no modification |
| **Services** | Conservative — disable only: Fax, RemoteRegistry, RetailDemo; keep everything else at defaults |
| **Network** | Light optimization — TCP auto-tuning normal; DNS cache flush on cleanup; disable Windows Update P2P upload to other PCs |
| **Storage** | Storage Sense enabled with default settings; TRIM monthly; default indexing |
| **Cleanup** | Monthly temp cleanup; disable Windows Tips notifications; disable suggested apps |
| **Risk Level** | VERY LOW — near-default configuration, minimal risk of any side effects |

**Profile-Specific Tweaks:**
- Disable Windows Tips and suggestions
- Disable suggested apps in Start
- Disable lock screen spotlight ads
- Disable Activity History
- Optimize Windows Update delivery settings (reduce P2P upload)
- Enable Storage Sense with sensible defaults

**Hardware Adjustments:**
- Minimal — this profile is intentionally near-default and works across all hardware tiers without modification
- If system is detected as laptop: no additional changes (Balanced works for both AC and battery)

### 1.5 Content Creator/Streaming

**Purpose:** Stable video encoding, reliable streaming upload, good multitasking between recording software, browsers, and creative applications.

| Dimension | Configuration |
|---|---|
| **Power Plan** | High Performance (desktop); High Performance on AC / Balanced on battery (laptop) |
| **CPU Boost** | Aggressive when streaming/recording; default when idle (auto-switch via context detection) |
| **Visual Effects** | Best Performance — disable animations and transparency to free GPU resources for encoding |
| **Services** | Moderate disable: SysMain, DiagTrack, Xbox services, Fax, RemoteRegistry; keep: all audio services, BITS, WaaSMedicSvc, print services, Windows Audio Endpoint Builder |
| **Network** | Upload-optimized: disable Nagle algorithm (for low-latency streaming); QoS packet scheduling enabled; disable Wi-Fi power saving; set NIC send/receive buffers to maximum; disable flow control; disable energy-efficient Ethernet |
| **Storage** | TRIM weekly; disable indexing on project/scratch drives; enable write caching on recording drives; set separate scratch disk if available; NTFS memory allocation increased |
| **Cleanup** | Disable Delivery Optimization upload sharing; auto-clear rendering temp folders; disable Windows Tips |
| **Risk Level** | MODERATE — High Performance increases power/heat; Nagle disable may affect some applications; large buffer sizes use more memory |

**Profile-Specific Tweaks:**
- HAGS enabled (reduces encoding latency on supported GPUs)
- Game DVR disabled (prefer OBS/Streamlabs for recording)
- GPU scheduling prioritized for capture software
- MMCSS SystemResponsiveness: 10 (reduces background CPU during streaming)
- Network: disable Nagle, enable QoS, maximize NIC buffers
- Process priority: class for encoding software set to High
- Timer Resolution: 1ms during active streaming sessions

**Hardware Adjustments:**
- If GPU tier is Integrated: warn that hardware encoding may not be available; suggest software encoding presets; reduce visual effects aggressiveness
- If RAM tier is Low (4GB): warn about memory pressure during encoding; suggest closing background apps; consider Daily profile instead
- If network tier is low bandwidth (<10 Mbps upload): suggest reducing stream bitrate; disable high-bitrate presets

### 1.6 Developer/Power User

**Purpose:** Fast compilation, responsive IDE, reliable networking for containers/WSL/remote development, and no interference with development toolchains.

| Dimension | Configuration |
|---|---|
| **Power Plan** | High Performance (desktop); High Performance on AC / Balanced on battery (laptop) |
| **CPU Boost** | Aggressive — all cores boosted during builds; max processor state 100%; core parking disabled |
| **Visual Effects** | Balanced — keep font smoothing and basic animations for long coding sessions; disable only transparency and heavy effects |
| **Services** | Targeted disable: SysMain (prevents interference with large project file caching), DiagTrack, Xbox services, Fax, RemoteRegistry; keep: Wsl, docker, LxssManager (WSL), all network services, all audio services, BITS; enable Hyper-V services if detected |
| **Network** | Full optimization: TCP auto-tuning normal; Nagle disabled (essential for local dev servers and SSH); TCP timestamps enabled; window scaling enabled; disable Wi-Fi power saving; increase connection limits; DNS cache optimization; disable network throttling index |
| **Storage** | TRIM weekly; disable indexing on source code directories (add dev paths to exclusion list); write caching enabled; NTFS last access time disabled; large NTFS memory allocation (512KB-1MB); increase NTFS MFT zone |
| **Cleanup** | Disable Delivery Optimization upload sharing; keep build caches; clear only temp files monthly; do not touch node_modules, .venv, or other dependency caches |
| **Risk Level** | LOW-MODERATE — SysMain disable increases disk reads for large projects; Nagle disable is beneficial for development but may affect some downloads; service changes are targeted and reversible |

**Profile-Specific Tweaks:**
- Disable Windows Search indexing on development directories
- Set process priority scheduling for foreground applications
- Enable long path support (registry: LongPathsEnabled=1)
- Enable Developer Mode (sideloading, symlinks without elevation)
- Set DNS resolver cache to larger size
- Enable Hyper-V if available (for WSL2, Docker, VMs)
- Configure Windows Defender exclusions for common dev directories (warn user first)
- Disable Windows Error Reporting for faster crash recovery

**Hardware Adjustments:**
- If RAM tier is Ultra (32GB+): disable pagefile managed sizing; set fixed pagefile to RAM size; enable large pages for build workloads
- If RAM tier is Low: keep SysMain enabled; reduce NTFS memory allocation; increase pagefile
- If storage tier is NVMe: maximum NTFS optimization; disable defrag entirely; weekly TRIM
- If storage tier is HDD: strongly recommend SSD upgrade for development; enable scheduled defrag

### 1.7 Workstation/Professional

**Purpose:** Maximum sustained throughput for CAD, 3D rendering, video production, scientific computing, and other professional compute-heavy workloads.

| Dimension | Configuration |
|---|---|
| **Power Plan** | Ultimate Performance (desktop); High Performance (laptop on AC) |
| **CPU Boost** | Aggressive — all cores at maximum; boost mode 2; C-states C1 minimum; core parking disabled; processor performance increase policy maximum |
| **Visual Effects** | Best Performance — disable all visual effects to maximize GPU/CPU availability for professional applications |
| **Services** | Moderate disable: SysMain, DiagTrack, WSearch (on render drives), Xbox services, Fax, RemoteRegistry, MapsBroker; keep: all GPU/driver services, all network services, WaaSMedicSvc, print services |
| **Network** | Throughput-focused: TCP auto-tuning normal; large receive window; disable Nagle; disable Wi-Fi power saving; maximum NIC buffer sizes; disable flow control on high-speed NICs; Jumbo frames enabled if supported (9014 MTU); disable interrupt moderation where latency matters |
| **Storage** | TRIM weekly; write caching enabled; disable indexing on project/cache drives; NTFS large memory allocation (1MB); disable last access time; NTFS MFT reserved space increased; enable large pages for memory-intensive rendering; separate scratch/cache disks configured |
| **Cleanup** | Disable Delivery Optimization upload sharing; preserve all project/render caches; clear only system temp files; do not touch render output or project files |
| **Risk Level** | MODERATE — Ultimate Performance increases power consumption and heat; Jumbo frames require switch support; large pages require specific configuration; core parking disable may increase idle power |

**Profile-Specific Tweaks:**
- Large Pages enabled (SeLockMemoryPrivilege required)
- MMCSS SystemResponsiveness: 0 (allocate maximum resources to active applications)
- GPU scheduling: HAGS enabled; MPO enabled on supported hardware
- Process priority: foreground application class set to High
- Power plan: Ultimate Performance with minimum processor state 100% during active work
- Disable CPU core parking entirely
- Disable PCI Express link state power management (prevent bandwidth reduction)
- NUMA-aware scheduling for multi-socket workstations

**Hardware Adjustments:**
- If GPU tier is Integrated: skip HAGS; reduce GPU-related optimizations; warn that professional GPU workloads require discrete GPU
- If RAM tier is not Ultra (32GB+): recommend upgrade for professional workloads; set pagefile to 1.5x RAM size
- If storage tier is HDD: strongly recommend SSD upgrade for project drives; enable aggressive defrag for HDD working drives
- If CPU tier is Low/Mid: use High Performance instead of Ultimate; moderate boost aggressiveness

### 1.8 Battery Saver (Laptop)

**Purpose:** Maximum battery life for laptops when unplugged. Minimize all power consumption while maintaining basic usability.

| Dimension | Configuration |
|---|---|
| **Power Plan** | Power Saver; minimum processor state 5%, maximum 50%; aggressive sleep timers (5 min display, 10 min sleep) |
| **CPU Boost** | Disabled — processor performance boost mode 0; maximum processor state reduced to 50-70%; EPP (Energy Performance Preference) set to maximum power saving |
| **Visual Effects** | Best Performance — disable all animations, transparency, and visual effects; reduces GPU power draw |
| **Services** | Aggressive disable: SysMain, DiagTrack, WSearch, BITS (background), Xbox services, Print Spooler, Fax, MapsBroker, RemoteRegistry, lfsvc, TrkWks, dmwappushservice, RetailDemo, WMPNetworkSvc; keep: wuauserv, CryptSvc, RpcSs, DcomLaunch, network services, audio services |
| **Network** | Maximum power saving: enable Wi-Fi power saving; reduce NIC buffer sizes; disable background BITS transfers; disable Windows Update P2P; limit background network activity; disable Bluetooth when not needed |
| **Storage** | TRIM monthly; disable indexing; disable defrag on SSD; enable aggressive Storage Sense (weekly cleanup); disable hibernation fast startup to allow deeper sleep; reduce disk idle timeout |
| **Cleanup** | Aggressive: clear all temp files, caches, thumbnails, DNS cache, Windows Update cache, Delivery Optimization cache; disable all non-essential background processes |
| **Risk Level** | HIGH (for productivity) — significant performance reduction; delayed Windows Updates; reduced search functionality; aggressive sleep may lose network connections; maximum processor cap prevents burst performance |

**Profile-Specific Tweaks:**
- Disable hibernation fast startup (allow deeper sleep states)
- Enable adaptive brightness (if supported)
- Reduce display brightness to 40% default
- Disable keyboard backlight (if supported)
- Disable Bluetooth (with user toggle for re-enable)
- Disable location services
- Disable clipboard history sync
- Disable Windows Tips and suggestions
- Disable all toast notifications
- Enable USB selective suspend
- Enable PCI Express Link State Power Management (moderate)
- Reduce audio quality to save power
- Disable lock screen slideshow

**Hardware Adjustments:**
- If not a laptop: warn user that this profile is designed for laptops; suggest Balanced for desktop systems
- If battery health is low (detected via WMI BatteryStatus): suggest plugging in instead; warn about aggressive drain
- If RAM tier is Very Low: disable hibernation entirely to reclaim disk space; reduce pagefile
- If storage tier is HDD: disable defrag scheduling; hibernation file takes significant space on small HDDs

---

## 2. Hardware Tier Detection

### 2.1 Detection Methods

The hardware detection engine extends the `SystemDetector` class defined in `core-engine.md`. All detection uses WMI, CIM, and system APIs without requiring third-party dependencies.

```csharp
public class HardwareTierDetector
{
    // Detection sources
    // CPU:     Win32_Processor (Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed)
    // RAM:     Win32_ComputerSystem (TotalPhysicalMemory)
    // Storage: Win32_DiskDrive (MediaType, Model) + Win32_LogicalDisk (DriveType, FileSystem)
    // GPU:     Win32_VideoController (Name, AdapterRAM, VideoProcessor, DriverVersion)
    // Device:  Win32_Battery (presence), Win32_SystemEnclosure (ChassisTypes)
}
```

### 2.2 CPU Tiers

| Tier | Cores (Physical) | Clock Speed | Example Hardware |
|---|---|---|---|
| **Ultra** | 12+ | 5.0 GHz+ boost | AMD Ryzen 9 7950X, Intel i9-14900K |
| **High** | 8+ | 4.0 GHz+ boost | AMD Ryzen 7 7700X, Intel i7-14700K |
| **Mid** | 6 | 3.5 GHz+ boost | AMD Ryzen 5 7600, Intel i5-13600K |
| **Low** | 4 | Any clock | AMD Ryzen 3, Intel i3, older quad-cores |
| **Very Low** | 2 | Any clock | Intel Celeron, Pentium, older dual-cores |

**Detection Logic:**
```
1. Read NumberOfCores from Win32_Processor
2. Read MaxClockSpeed from Win32_Processor
3. Classify:
   - Cores >= 12 AND ClockSpeed >= 5000  -> Ultra
   - Cores >= 8  AND ClockSpeed >= 4000  -> High
   - Cores >= 6                         -> Mid
   - Cores >= 4                         -> Low
   - Cores < 4                          -> Very Low
4. Fallback: if clock speed unavailable, classify by cores only
```

### 2.3 RAM Tiers

| Tier | Capacity | Impact on Profile Selection |
|---|---|---|
| **Ultra** | 32 GB+ | All profiles fully supported; large pages available |
| **High** | 16 GB | All profiles fully supported |
| **Mid** | 8 GB | Most profiles supported; Potato PC adjustments for heavy profiles |
| **Low** | 4 GB | Potato PC recommended; aggressive memory management required |
| **Very Low** | <4 GB | Potato PC mandatory; disable paging file management; heavy service disable |

**Detection Logic:**
```
1. Read TotalPhysicalMemory from Win32_ComputerSystem (returns bytes)
2. Convert to GB: totalBytes / (1024^3)
3. Classify:
   - >= 32  -> Ultra
   - >= 16  -> High
   - >= 8   -> Mid
   - >= 4   -> Low
   - < 4    -> Very Low
```

### 2.4 Storage Tiers

| Tier | Detection Method | Profile Impact |
|---|---|---|
| **NVMe** | MediaType = "NVMe SSD" OR Model contains "NVMe" OR "NVM Express" | Full optimization; TRIM weekly; no defrag |
| **SATA SSD** | MediaType = "SSD" AND Model does not contain "NVMe" | Full optimization; TRIM monthly; no defrag |
| **HDD** | MediaType = "Fixed hard disk media" | Enable defrag; disable indexing aggressively; recommend upgrade |
| **Mixed** | Multiple drives with different types | Per-drive optimization; identify OS drive tier |

**Detection Logic:**
```
1. Enumerate Win32_DiskDrive where MediaType != "Removable media"
2. For each drive:
   a. Read MediaType and Model
   b. Check for NVMe in model string or bus type
   c. Classify as NVMe, SATA SSD, or HDD
3. Primary tier = OS drive tier (Win32_LogicalDisk where BootDevice = true)
4. If mixed: report per-drive tiers; apply optimizations per-drive
```

### 2.5 GPU Tiers

| Tier | Detection | Profile Impact |
|---|---|---|
| **High** | NVIDIA RTX 30xx/40xx/50xx, AMD RX 6xxx/7xxx/8xxx | Full GPU optimization; HAGS; MPO; gaming/streaming profiles |
| **Mid** | NVIDIA GTX 16xx/RTX 20xx, AMD RX 5xxx, Intel Arc | Most GPU optimization; HAGS where supported |
| **Low** | Older discrete GPUs, NVIDIA GT series, AMD R series | Basic GPU optimization; skip advanced features |
| **Integrated** | Intel UHD/Iris, AMD Radeon Vega/RDNA iGPU | Minimal GPU optimization; focus on CPU/RAM tuning |

**Detection Logic:**
```
1. Enumerate Win32_VideoController
2. Filter out Microsoft Basic Display Adapter
3. For primary adapter:
   a. Read Name, AdapterRAM (dedicated VRAM)
   b. Classify by vendor/model matching against known tiers
   c. If AdapterRAM > 8GB -> likely High; 4-8GB -> Mid; <4GB -> Low
   d. If Vendor = "Intel" AND no secondary adapter -> Integrated
4. For multi-GPU systems: use primary display adapter
```

### 2.6 Device Type Detection

| Type | Detection Method | Profile Impact |
|---|---|---|
| **Desktop** | No battery present; ChassisTypes 3, 4, 5, 6, 7 (Tower, Desktop, Mini-Tower, etc.) | Full power options available; no battery considerations |
| **Laptop** | Battery present; ChassisTypes 8, 9, 10, 14 (Portable, Laptop, Notebook, Sub-Notebook) | Battery-aware profiles; AC/DC switching; thermal management |
| **Tablet** | ChassisTypes 30; touch-first device | Touch-optimized; battery priority; limited port configuration |

### 2.7 Composite Hardware Score

The detection engine produces a composite hardware score used by the auto-selection algorithm:

```csharp
public class HardwareProfile
{
    public CpuTier Cpu { get; set; }           // Ultra=5, High=4, Mid=3, Low=2, VeryLow=1
    public RamTier Ram { get; set; }           // Ultra=5, High=4, Mid=3, Low=2, VeryLow=1
    public StorageTier Storage { get; set; }   // NVMe=4, SataSSD=3, Mixed=2, HDD=1
    public GpuTier Gpu { get; set; }          // High=4, Mid=3, Low=2, Integrated=1
    public DeviceType Device { get; set; }     // Desktop=3, Laptop=2, Tablet=1

    public int CompositeScore =>
        (int)Cpu * 3 +        // CPU weight: 30%
        (int)Ram * 2 +        // RAM weight: 20%
        (int)Storage * 2 +    // Storage weight: 20%
        (int)Gpu * 3;         // GPU weight: 30%
    // Max composite: (5*3)+(5*2)+(4*2)+(4*3) = 15+10+8+12 = 45
    // Min composite: (1*3)+(1*2)+(1*2)+(1*3) = 3+2+2+3 = 10

    public bool IsLaptop => Device == DeviceType.Laptop;
    public bool IsTablet => Device == DeviceType.Tablet;
    public bool IsDesktop => Device == DeviceType.Desktop;
}
```

---

## 3. Profile Auto-Selection Algorithm

### 3.1 Selection Flow

```
START
  |
  v
[1. Detect All Hardware] --> HardwareProfile
  |
  v
[2. Check User Preference]
  |-- User has pinned a profile? --> Apply pinned profile
  |-- User has profile history?  --> Weight toward frequently used profiles
  |-- No preference?             --> Continue to suggestion
  |
  v
[3. Compute Suggestion Scores]
  |
  v
[4. Apply Business Rules]
  |
  v
[5. Present Recommendation]
  |-- Show recommended profile with hardware summary
  |-- Show alternative profiles ranked by score
  |-- User can override or accept
  |
  v
[6. Apply Selected Profile]
  |
END
```

### 3.2 Suggestion Scoring

Each profile receives a score based on hardware and device type:

```csharp
public static ProfileSuggestion SuggestProfile(HardwareProfile hw)
{
    var scores = new Dictionary<ProfileType, double>();

    // Base scores by hardware capability
    scores[Gaming] = ScoreByGpuAndCpu(hw);
    scores[PotatoPC] = ScoreForLowEnd(hw);
    scores[Office] = ScoreForOffice(hw);
    scores[Daily] = ScoreForGeneral(hw);
    scores[ContentCreator] = ScoreByGpuAndCpu(hw);
    scores[Developer] = ScoreByRamAndStorage(hw);
    scores[Workstation] = ScoreForWorkstation(hw);
    scores[BatterySaver] = ScoreForLaptop(hw);

    // Business rules (hard adjustments)
    if (hw.CompositeScore <= 15)
    {
        scores[PotatoPC] += 40;     // Strong recommendation for low-end
        scores[Gaming] -= 30;       // Discourage gaming on weak hardware
        scores[Workstation] -= 20;  // Discourage workstation on weak hardware
    }

    if (hw.CompositeScore >= 35)
    {
        scores[Gaming] += 15;       // Encourage gaming on powerful hardware
        scores[Workstation] += 15;  // Encourage workstation on powerful hardware
    }

    if (hw.IsLaptop)
    {
        scores[BatterySaver] += 25; // Strong nudge toward battery saving
        scores[Daily] += 10;        // Daily is laptop-friendly
        scores[Workstation] -= 10;  // Workstation less suitable on laptop
    }

    if (hw.Gpu == GpuTier.Integrated)
    {
        scores[Gaming] -= 20;       // Gaming profile less effective
        scores[PotatoPC] += 5;      // May indicate lower-end system
    }

    if (hw.Gpu >= GpuTier.High && hw.Cpu >= CpuTier.High)
    {
        scores[Gaming] += 20;       // Gaming-capable hardware
        scores[ContentCreator] += 10;
    }

    if (hw.Ram >= RamTier.Ultra && hw.Storage == StorageTier.NVMe)
    {
        scores[Developer] += 15;    // Developer-friendly hardware
        scores[Workstation] += 10;
    }

    // Return top suggestion with all scores for alternatives
    return new ProfileSuggestion
    {
        Recommended = scores.OrderByDescending(s => s.Value).First(),
        AllScores = scores,
        HardwareProfile = hw
    };
}
```

### 3.3 Business Rules

Hard rules that override scoring:

| Rule | Condition | Action |
|---|---|---|
| **Low-end guard** | CompositeScore <= 15 | Always suggest Potato PC as primary recommendation |
| **Laptop nudge** | Device == Laptop | Always include Battery Saver as alternative; warn if user selects Workstation |
| **Tablet guard** | Device == Tablet | Suggest Battery Saver or Daily; discourage Gaming/Workstation |
| **Gaming capability** | GPU >= High AND CPU >= High | Gaming is a viable option |
| **Developer detection** | (Wsl installed) OR (Docker installed) OR (VS Code / VS installed) | Suggest Developer profile |
| **Workstation detection** | CPU >= High AND RAM >= Ultra AND GPU >= Mid | Workstation is a viable option |
| **Battery warning** | Battery remaining < 20% AND profile requires High Performance | Warn user; suggest Battery Saver |

### 3.4 Presentation Format

When presenting the recommendation to the user:

```
Hardware Summary:
  CPU:    AMD Ryzen 7 7700X (8 cores, 4.5 GHz)      [High]
  RAM:    32 GB DDR5-5600                              [Ultra]
  Storage: Samsung 990 Pro 1TB (NVMe)                 [NVMe]
  GPU:    NVIDIA RTX 4070 Ti (12GB)                   [High]
  Device: Desktop

Recommended Profile:  Gaming
  Reason: High-end GPU and CPU detected. This system
  is well-suited for gaming optimizations including
  HAGS, aggressive boost, and low-latency networking.

Alternative Profiles:
  2. Content Creator/Streaming  (score: 72)
  3. Developer/Power User       (score: 65)
  4. Workstation/Professional   (score: 58)
  5. Daily/General Usage        (score: 30)
  6. Office/Productivity        (score: 25)
  7. Battery Saver              (score: 5)
  8. Potato PC (Low-End)        (score: 2)

[Accept Recommendation]  [Choose Different Profile]
```

---

## 4. Profile Configuration Schema

### 4.1 Profile Definition

Each profile is a JSON object that defines the complete optimization configuration, including base settings, hardware-tier adjustments, inclusion/exclusion rules, and override values.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "id": "string — unique profile identifier (kebab-case)",
  "name": "string — human-readable profile name",
  "description": "string — one-line description of the profile's purpose",
  "icon": "string — UI icon identifier",
  "color": "string — hex color for UI display",
  "riskLevel": "string — VERY LOW | LOW | MODERATE | HIGH",
  "version": "string — schema version (semver)",

  "hardwareRequirements": {
    "minCompositeScore": "number — minimum hardware composite score",
    "recommendedCompositeScore": "number — recommended score for full benefit",
    "deviceTypes": ["desktop", "laptop", "tablet"],
    "notes": "string — hardware requirement notes"
  },

  "basePreset": "string — ID of the base preset from presets-recommendations.md",

  "categories": {
    "<category>": {
      "enabled": "boolean — whether this category is active in this profile",
      "intensity": "string — conservative | moderate | aggressive",
      "tweakOverrides": {
        "<tweakId>": {
          "forceEnable": "boolean — include this tweak even if hardware doesn't match",
          "forceDisable": "boolean — exclude this tweak even if hardware matches",
          "customValue": "any — override the tweak's default value"
        }
      }
    }
  },

  "alwaysInclude": [
    "string — tweak IDs that are always included in this profile"
  ],

  "neverInclude": [
    "string — tweak IDs that are never included in this profile"
  ],

  "hardwareAdjustments": [
    {
      "id": "string — adjustment rule identifier",
      "description": "string — what this adjustment does",
      "conditions": {
        "cpuTier": ["string | null — apply when CPU is this tier"],
        "ramTier": ["string | null — apply when RAM is this tier"],
        "storageTier": ["string | null — apply when storage is this tier"],
        "gpuTier": ["string | null — apply when GPU is this tier"],
        "deviceType": ["string | null — apply on this device type"]
      },
      "adjustments": {
        "addTweaks": ["string — tweak IDs to add"],
        "removeTweaks": ["string — tweak IDs to remove"],
        "modifyTweaks": {
          "<tweakId>": { "customValue": "any" }
        },
        "changeCategoryIntensity": {
          "<category>": "string — new intensity level"
        }
      },
      "warning": "string | null — warning message to show user"
    }
  ],

  "powerConfig": {
    "plan": "string — ultimate | highPerformance | balanced | powerSaver",
    "minimumProcessorPct": "number — 0-100",
    "maximumProcessorPct": "number — 0-100",
    "boostMode": "string — aggressive | efficient | disabled | default",
    "coreParking": "boolean — disable core parking when true",
    "sleepTimers": {
      "displayOffMinutes": "number",
      "sleepMinutes": "number"
    },
    "laptopOverrides": {
      "planOnAC": "string",
      "planOnDC": "string",
      "boostModeOnDC": "string"
    }
  },

  "autoSwitchTriggers": [
    {
      "id": "string — trigger identifier",
      "type": "string — gameDetected | powerSourceChange | meetingDetected | workloadChange",
      "action": "string — switchTo | recommend",
      "targetProfile": "string — profile ID to switch to or recommend",
      "conditions": { }
    }
  ],

  "cleanupConfig": {
    "aggressiveness": "string — minimal | light | moderate | aggressive",
    "autoCleanupInterval": "string — daily | weekly | monthly | never",
    "targets": ["string — cleanup target categories"],
    "preserveList": ["string — paths/patterns to never clean"]
  },

  "metadata": {
    "author": "string",
    "createdDate": "ISO-8601",
    "lastModified": "ISO-8601",
    "testedOn": ["string — hardware configs tested"],
    "notes": "string — development/implementation notes"
  }
}
```

### 4.2 Example: Gaming Profile Definition

```json
{
  "id": "gaming",
  "name": "Gaming",
  "description": "Low latency, high FPS, minimal background interference",
  "icon": "gamepad",
  "color": "#FF6B35",
  "riskLevel": "MODERATE",
  "version": "1.0.0",

  "hardwareRequirements": {
    "minCompositeScore": 18,
    "recommendedCompositeScore": 30,
    "deviceTypes": ["desktop", "laptop"],
    "notes": "Beneficial on any gaming-capable hardware. Laptop users should consider battery impact."
  },

  "basePreset": "gaming",

  "categories": {
    "cpu": { "enabled": true, "intensity": "aggressive" },
    "gpu": { "enabled": true, "intensity": "aggressive" },
    "network": { "enabled": true, "intensity": "aggressive" },
    "storage": { "enabled": true, "intensity": "moderate" },
    "visual": { "enabled": true, "intensity": "aggressive" },
    "services": { "enabled": true, "intensity": "aggressive" },
    "power": { "enabled": true, "intensity": "aggressive" },
    "gaming": { "enabled": true, "intensity": "aggressive" },
    "privacy": { "enabled": false, "intensity": "conservative" },
    "cleanup": { "enabled": true, "intensity": "light" },
    "debloat": { "enabled": false, "intensity": "conservative" }
  },

  "alwaysInclude": [
    "gaming.gameMode.enable",
    "gaming.gameDvr.configure",
    "gpu.hags.enable",
    "cpu.powerPlan.ultimatePerformance",
    "network.tcpip.nagleDisable",
    "network.mmscss.networkThrottling",
    "visual.effects.bestPerformance",
    "cpu.boost.aggressive"
  ],

  "neverInclude": [
    "security.defender.disable",
    "services.windowsUpdate.disable",
    "features.hyperV.disable"
  ],

  "hardwareAdjustments": [
    {
      "id": "gaming-integrated-gpu",
      "description": "Skip GPU-specific optimizations on integrated graphics",
      "conditions": { "gpuTier": ["Integrated"] },
      "adjustments": {
        "removeTweaks": ["gpu.hags.enable", "gpu.mpo.configure"],
        "changeCategoryIntensity": { "gpu": "conservative" }
      },
      "warning": "Integrated GPU detected. GPU-specific optimizations have been skipped. Gaming performance will be limited regardless of profile."
    },
    {
      "id": "gaming-low-ram",
      "description": "Adjust memory management for low RAM systems",
      "conditions": { "ramTier": ["Low", "VeryLow"] },
      "adjustments": {
        "removeTweaks": ["services.sysMain.disable"],
        "modifyTweaks": {
          "memory.pagefile.size": { "customValue": "2048" }
        }
      },
      "warning": "Low RAM detected. SysMain has been kept enabled to prevent excessive disk swapping. 8GB+ RAM recommended for gaming."
    },
    {
      "id": "gaming-hdd-storage",
      "description": "Adjust storage optimizations for HDD systems",
      "conditions": { "storageTier": ["HDD"] },
      "adjustments": {
        "modifyTweaks": {
          "storage.defrag.schedule": { "customValue": "disabled" }
        }
      },
      "warning": "HDD detected. SSD strongly recommended for gaming. Defrag scheduling has been disabled to reduce background I/O."
    }
  ],

  "powerConfig": {
    "plan": "ultimate",
    "minimumProcessorPct": 50,
    "maximumProcessorPct": 100,
    "boostMode": "aggressive",
    "coreParking": false,
    "sleepTimers": {
      "displayOffMinutes": 15,
      "sleepMinutes": 0
    },
    "laptopOverrides": {
      "planOnAC": "highPerformance",
      "planOnDC": "balanced",
      "boostModeOnDC": "efficient"
    }
  },

  "cleanupConfig": {
    "aggressiveness": "light",
    "autoCleanupInterval": "monthly",
    "targets": ["shaderCache", "deliveryOptimization"],
    "preserveList": ["gameInstallDirs", "saveGameDirs"]
  }
}
```

---

## 5. Auto-Switching System

### 5.1 Overview

Auto-switching detects runtime context changes and transitions between profiles without user intervention. It operates as a background monitor that fires events consumed by the profile manager.

```
┌─────────────────────────────────────────────┐
│           Context Monitors                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐    │
│  │  Game     │ │  Power   │ │ Meeting  │    │
│  │  Detector │ │  Source  │ │ Detector │    │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘    │
│       │             │            │           │
│  ┌────▼─────────────▼────────────▼─────┐    │
│  │      Switch Event Bus               │    │
│  └────────────────┬────────────────────┘    │
│                   │                         │
│  ┌────────────────▼────────────────────┐    │
│  │      Profile Manager                │    │
│  │  - Validates switch is safe         │    │
│  │  - Creates snapshot before switch   │    │
│  │  - Applies new profile              │    │
│  │  - Rolls back if apply fails        │    │
│  └─────────────────────────────────────┘    │
└─────────────────────────────────────────────┘
```

### 5.2 Switch Triggers

| Trigger | Detection Method | Target Profile | Confidence |
|---|---|---|---|
| **Game detected** | Monitor foreground window class/title against known game processes; check for DirectX/Vulkan overlay injection; check `GetForegroundWindow` process against game database | Gaming | High — game process is unambiguous |
| **AC to DC (laptop)** | `WM_POWERBROADCAST` / `SystemEvents.PowerModeChanged`; WMI `Win32_Battery` Status property; `powercfg /getactivescheme` change | Battery Saver (from current profile) | Definitive — power source change is deterministic |
| **DC to AC (laptop)** | Same detection as above, reversed | Return to previous profile or user-selected profile | Definitive |
| **Video conference detected** | Detect active window class for Zoom, Teams, Meet, WebEx; check for camera/microphone in use via `media.devices`; detect browser tab with active video call | Office/Productivity (with network priority adjustments) | Medium — may be false positive for video playback |
| **Workload change** | Monitor CPU utilization pattern over 5-minute window: sustained >80% on all cores may indicate rendering/build; sustained <10% indicates idle | Adjust boost behavior and core parking | Low — heuristic, recommend rather than auto-switch |

### 5.3 Auto-Switch Behavior

```json
{
  "autoSwitch": {
    "enabled": true,
    "switchMode": "recommend",
    "snapshotBeforeSwitch": true,
    "cooldownSeconds": 30,
    "maxSwitchesPerHour": 10,

    "triggers": [
      {
        "id": "game-detected",
        "enabled": true,
        "type": "gameDetected",
        "action": "recommend",
        "targetProfile": "gaming",
        "conditions": {
          "currentProfileIsNot": "gaming",
          "hardwareScoreAbove": 18,
          "gameProcessInForegroundForSeconds": 10
        },
        "notification": "Game detected: {gameName}. Switch to Gaming profile for optimal performance?"
      },
      {
        "id": "laptop-unplugged",
        "enabled": true,
        "type": "powerSourceChange",
        "action": "switchTo",
        "targetProfile": "batterySaver",
        "conditions": {
          "deviceType": "laptop",
          "powerSource": "DC",
          "batteryPercentageBelow": 80,
          "currentProfileIsNot": "batterySaver"
        },
        "notification": "Unplugged from power. Switched to Battery Saver profile."
      },
      {
        "id": "laptop-plugged-in",
        "enabled": true,
        "type": "powerSourceChange",
        "action": "switchTo",
        "targetProfile": "previousProfile",
        "conditions": {
          "deviceType": "laptop",
          "powerSource": "AC",
          "currentProfileIs": "batterySaver",
          "previousProfileExists": true
        },
        "notification": "Plugged in. Returned to {previousProfile} profile."
      },
      {
        "id": "meeting-detected",
        "enabled": false,
        "type": "meetingDetected",
        "action": "recommend",
        "targetProfile": "office",
        "conditions": {
          "videoConferenceActive": true,
          "currentProfileIsNot": "office",
          "conferenceDurationAboveMinutes": 2
        },
        "notification": "Video conference detected. Switch to Office profile for stable network and audio?"
      }
    ]
  }
}
```

### 5.4 Switch Safety

Every automatic profile switch follows the same safety protocol as manual application:

1. **Pre-switch snapshot** — full system state snapshot before any changes
2. **Conflict check** — verify new profile does not conflict with active critical processes
3. **Graceful transition** — apply changes in provider execution order; do not interrupt active workloads
4. **Verification** — post-switch verification of all applied changes
5. **Rollback on failure** — if any critical tweak fails verification, rollback entire switch
6. **Logging** — log switch event with before/after profiles, trigger, timestamp, and result
7. **User notification** — system tray notification for every auto-switch with reason

### 5.5 Switch Log Entry

```json
{
  "switchId": "uuid",
  "timestamp": "ISO-8601",
  "trigger": {
    "type": "gameDetected",
    "details": "Foreground process: Cyberpunk2077.exe"
  },
  "fromProfile": "daily",
  "toProfile": "gaming",
  "mode": "auto",
  "snapshotId": "snapshot-uuid",
  "result": "success",
  "duration": "12.3s",
  "tweaksChanged": 47,
  "tweaksUnchanged": 23
}
```

---

## 6. Integration Points

### 6.1 With Core Engine

Profiles feed into the core engine as resolved tweak sets. The profile manager calls:

```
1. ProfileManager.ResolveProfile(profileId, hardwareProfile)
   -> returns ResolvedTweakSet (list of tweak IDs with hardware-adjusted values)

2. CoreEngine.ApplyProfile(resolvedTweakSet)
   -> uses existing apply/verify/rollback infrastructure
   -> creates snapshot with profile metadata
   -> applies in provider execution order
```

### 6.2 With Auto-Optimize

Profiles and auto-optimize serve different purposes:
- **Auto-optimize** (`auto-optimize.md`) applies universally safe tweaks based on safety criteria
- **Profiles** apply a comprehensive, themed optimization set based on use case and hardware

The two can be combined: a profile defines the optimization theme, and auto-optimize's safety criteria gate individual tweak application within that theme.

### 6.3 With Recommendation Engine

The recommendation engine (`presets-recommendations.md`) scores individual tweaks. Profiles provide the preset alignment scores that the recommendation engine uses:

```
profile.basePreset -> preset alignment scoring
profile.alwaysInclude -> override scoring to maximum
profile.neverInclude -> override scoring to zero
profile.hardwareAdjustments -> modify hardware multipliers
```

### 6.4 With Snapshot/Rollback

Every profile application creates a snapshot tagged with the profile ID:

```json
{
  "snapshotId": "uuid",
  "trigger": "profileApply",
  "profileId": "gaming",
  "hardwareProfile": { "cpu": "High", "ram": "Ultra", "storage": "NVMe", "gpu": "High" },
  "timestamp": "ISO-8601",
  "tweaksApplied": [...],
  "previousState": {...}
}
```

Rollback from a profile restores the entire previous state, not just individual tweaks.

---

## 7. Storage and Persistence

### 7.1 File Locations

```
%LOCALAPPDATA%\WinOpt\
├── profiles\
│   ├── profiles.json           — all profile definitions
│   ├── active-profile.json     — currently active profile
│   ├── user-overrides.json     — user customizations to profiles
│   └── switch-history.json     — auto-switch event log
├── hardware\
│   ├── last-detection.json     — most recent hardware detection
│   └── tier-cache.json         — cached hardware tiers (refresh on hardware change)
├── snapshots\
│   └── profile-*.json          — snapshots tagged with profile application
└── logs\
    └── profile-switches.json   — switch audit trail
```

### 7.2 User Override Schema

Users can customize any profile without modifying the base definition:

```json
{
  "profileId": "gaming",
  "overrides": {
    "alwaysInclude": ["tcp.ip.forwarding.enable"],
    "neverInclude": ["network.nagle.disable"],
    "categories": {
      "network": { "intensity": "conservative" }
    },
    "powerConfig": {
      "minimumProcessorPct": 30
    }
  }
}
```

Overrides are applied on top of the base profile definition at resolution time.

---

## 8. Implementation Notes

### 8.1 Dependency on Existing Systems

This architecture depends on and extends:
- **`core-engine.md`** — SystemDetector, Provider Registry, Apply/Rollback/Verify engines
- **`presets-recommendations.md`** — Preset definitions, recommendation scoring, conflict engine
- **`auto-optimize.md`** — Safety criteria, dry-run system, execution guards
- **`tweak-schema.md`** — Tweak definition schema (each profile references tweak IDs from this schema)
- **`security-model.md`** — neverInclude lists enforce security boundaries; profiles cannot bypass protected services

### 8.2 Phase Integration

Based on the implementation roadmap (`implementation-roadmap.md`):

| Roadmap Phase | Profile Integration |
|---|---|
| Phase 1 (Core Engine) | Hardware detection in SystemDetector produces HardwareProfile |
| Phase 2 (Providers) | Providers consume resolved tweak sets from profile resolution |
| Phase 3 (Recommendation) | Profile definitions stored alongside presets; profile manager added |
| Phase 5 (Diagnostics) | Hardware tier detection uses diagnostic capabilities |
| Phase 6 (Tauri UI) | Profile selector UI, auto-switch notifications, profile customization |
| Phase 7 (CLI) | `winopt profile list`, `winopt profile apply`, `winopt profile detect` |

### 8.3 Testing Strategy

Each profile must be validated against its hardware requirements:

- **Gaming:** Test on Ultra/High hardware with discrete GPU; verify on Integrated GPU with degraded mode; test on laptop with AC/DC switching
- **Potato PC:** Test on Very Low hardware (2-core, 4GB, HDD); verify all aggressive disables are safe; verify feature functionality is acceptable
- **Office:** Test on Mid hardware laptop; verify battery impact; verify smooth animation performance
- **Daily:** Test on any hardware; verify near-default behavior; no regressions
- **Content Creator:** Test with OBS streaming; verify encoding stability; test network under load
- **Developer:** Test with WSL2, Docker, VS Code; verify build performance; test service detection
- **Workstation:** Test with CAD/rendering workload; verify large pages; test sustained throughput
- **Battery Saver:** Test battery drain improvement on laptop; verify usability; test wake-from-sleep

---

## Appendix A: Profile Comparison Matrix

| Dimension | Gaming | Potato PC | Office | Daily | Creator | Developer | Workstation | Battery Saver |
|---|---|---|---|---|---|---|---|---|
| **Power Plan** | Ultimate | Balanced | Balanced | Balanced | High Perf | High Perf | Ultimate | Power Saver |
| **CPU Boost** | Aggressive | Disabled | Default | Default | Aggressive | Aggressive | Aggressive | Disabled |
| **Visual FX** | Best Perf | Best Perf | Default | Default | Best Perf | Balanced | Best Perf | Best Perf |
| **Services** | Aggressive | Aggressive | Minimal | Minimal | Moderate | Targeted | Moderate | Aggressive |
| **Network** | Full Opt | Minimal | Conservative | Light | Upload Opt | Full Opt | Throughput | Power Save |
| **Storage** | Moderate | Aggressive | Storage Sense | Storage Sense | Moderate | Full Opt | Full Opt | Aggressive |
| **Cleanup** | Light | Aggressive | Light | Light | Moderate | Light | Light | Aggressive |
| **Risk Level** | Moderate | Moderate | Low | Very Low | Moderate | Low-Mod | Moderate | High |
| **Best HW** | High+ GPU | Any | Any | Any | High GPU | Ultra RAM | Ultra all | Low End Laptop |

## Appendix B: Hardware Tier Quick Reference

| Tier | CPU Cores | RAM | Storage | GPU | Composite Range |
|---|---|---|---|---|---|
| **Ultra** | 12+ @ 5GHz+ | 32GB+ | NVMe | RTX 40xx/RX 7xxx | 35-45 |
| **High** | 8+ @ 4GHz+ | 16GB | SSD | RTX 30xx/RX 6xxx | 25-34 |
| **Mid** | 6 | 8GB | SSD | GTX 16xx/Arc | 18-24 |
| **Low** | 4 | 4GB | Mixed | Older discrete | 12-17 |
| **Very Low** | 2 | <4GB | HDD | Integrated | 10-11 |
