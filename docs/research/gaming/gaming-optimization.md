# Windows Gaming Optimization Research

> Comprehensive reference for Windows gaming performance tuning.
> Each section includes registry paths, commands, evidence scores, and caveats.

**Evidence Score Key:**
- 5/5 = Extensively documented with reproducible benchmarks
- 4/5 = Well-documented with measurable results
- 3/5 = Microsoft documented or vendor-confirmed, limited independent testing
- 2/5 = Anecdotal or inconsistent results
- 1/5 = No reliable evidence

---

## 1. Game Mode (Windows Built-in)

**Evidence Score: 3/5** -- Microsoft documented; limited independent benchmarks show variable results depending on system configuration and game title.

### What It Does

Windows Game Mode (introduced in Windows 10 1703) is a system-level feature that:

- **Prioritizes game processes** -- Gives the foreground game higher CPU and GPU scheduling priority.
- **Suspends background tasks** -- Pauses Windows Update installations, driver installations, and certain background app activities while a game is running.
- **Allocates system resources** -- Directs more CPU cycles and memory bandwidth to the active game process.
- **Stabilizes frame delivery** -- Reduces frame time variance by minimizing system-level interruptions.

### Registry Configuration

```
[HKEY_CURRENT_USER\SOFTWARE\Microsoft\GameBar]
"AllowAutoGameMode"=dword:00000001
"AutoGameModeEnabled"=dword:00000001
```

| Value | Type | Description |
|-------|------|-------------|
| `AllowAutoGameMode` | DWORD | Master switch: 1 = enabled, 0 = disabled |
| `AutoGameModeEnabled` | DWORD | Auto-activation: 1 = Windows detects games automatically, 0 = manual only |

### PowerShell Commands

```powershell
# Enable Game Mode
Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\GameBar" -Name "AllowAutoGameMode" -Value 1 -Type DWord
Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\GameBar" -Name "AutoGameModeEnabled" -Value 1 -Type DWord

# Verify Game Mode status
Get-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\GameBar" -Name "AllowAutoGameMode","AutoGameModeEnabled"
```

### GUI Path

Settings > Gaming > Game Mode > Toggle On

### Limitations and Notes

- Game Mode has minimal measurable impact on high-end systems with ample resources. Its benefits are most pronounced on systems with 4 cores or fewer, or systems with significant background load.
- Some users report occasional stuttering with Game Mode enabled in certain titles. Microsoft has acknowledged this and patched behavior over time.
- Game Mode cannot override in-game CPU affinity or priority settings set by the game engine.
- Windows 11 refines Game Mode behavior compared to Windows 10, with better scheduling heuristics.

---

## 2. Game DVR / Game Bar

**Evidence Score: 4/5** -- Measurable CPU and GPU overhead when Game DVR is active; disabling produces quantifiable gains in frame rates and frame time consistency.

### What It Does

The Xbox Game Bar and Game DVR provide screen recording, screenshot capture, streaming, and overlay functionality. When enabled (even if not actively recording), these features carry overhead:

- **Game DVR recording buffer** maintains a rolling capture buffer that consumes GPU encoding resources and disk I/O.
- **Game Bar overlay** hooks into the rendering pipeline, adding compositor overhead.
- **Xbox services** maintain persistent background connections for authentication, save syncing, and network features.

### Registry Configuration

```
[HKEY_CURRENT_USER\System\GameConfigStore]
"GameDVR_Enabled"=dword:00000000

[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR]
"AllowGameDVR"=dword:00000000

[HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR]
"AppCaptureEnabled"=dword:00000000
```

| Value | Path | Description |
|-------|------|-------------|
| `GameDVR_Enabled` | HKCU\System\GameConfigStore | Disables Game DVR capture buffer globally |
| `AllowGameDVR` | HKLM (Group Policy) | Enterprise-level Game DVR disable; overrides user settings |
| `AppCaptureEnabled` | HKCU...\GameDVR | Disables the Game Bar app capture subsystem |

### Services to Disable

```powershell
# Xbox Game Input Protocol Service
Stop-Service -Name "XboxGipSvc" -Force
Set-Service -Name "XboxGipSvc" -StartupType Disabled

# Xbox Live Auth Manager
Stop-Service -Name "XblAuthManager" -Force
Set-Service -Name "XblAuthManager" -StartupType Disabled

# Xbox Live Game Save
Stop-Service -Name "XblGameSave" -Force
Set-Service -Name "XblGameSave" -StartupType Disabled

# Xbox Live Networking Service
Stop-Service -Name "XboxNetApiSvc" -Force
Set-Service -Name "XboxNetApiSvc" -StartupType Disabled
```

**Service Descriptions:**

| Service Name | Display Name | Impact of Disabling |
|-------------|-------------|---------------------|
| `XboxGipSvc` | Xbox Accessory Management Service | Disables Xbox controller accessory management (LED, firmware) |
| `XblAuthManager` | Xbox Live Auth Manager | Blocks Xbox Live authentication; no impact on non-Xbox games |
| `XblGameSave` | Xbox Live Game Save | Disables cloud save sync for Xbox Play Anywhere titles |
| `XboxNetApiSvc` | Xbox Live Networking Service | Disables Xbox Live network features and multiplayer |

### PowerShell Commands

```powershell
# Disable Game DVR and Game Bar
Set-ItemProperty -Path "HKCU:\System\GameConfigStore" -Name "GameDVR_Enabled" -Value 0 -Type DWord
Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR" -Name "AppCaptureEnabled" -Value 0 -Type DWord

# Apply Group Policy override
New-Item -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR" -Force
Set-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR" -Name "AllowGameDVR" -Value 0 -Type DWord

# Disable all Xbox services
$services = @("XboxGipSvc", "XblAuthManager", "XblGameSave", "XboxNetApiSvc")
foreach ($svc in $services) {
    Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
    Set-Service -Name $svc -StartupType Disabled -ErrorAction SilentlyContinue
    Write-Host "Disabled: $svc"
}
```

### Performance Impact

- Enabling Game DVR background recording typically costs 2-5% GPU utilization and 1-3% CPU on modern hardware.
- The Game Bar overlay adds approximately 0.5-1% GPU overhead even when not actively recording.
- On CPU-limited systems, the overhead from Xbox services and the overlay can cause frame time spikes of 1-3ms.

### Notes

- If you use Xbox Game Pass or Play Anywhere titles, disabling `XblAuthManager` and `XblGameSave` will break cloud saves and authentication for those games.
- For Steam/Epic/GOG-only setups, disabling all four Xbox services is safe and recommended.
- The Game Bar can also be disabled via: Settings > Gaming > Game Bar > Toggle Off.

---

## 3. GPU Optimizations

**Evidence Score: 4/5** -- HAGS, fullscreen optimizations, and GPU scheduling profiles are well-documented by Microsoft and GPU vendors with measurable latency and throughput improvements.

### 3.1 Hardware-Accelerated GPU Scheduling (HAGS)

**What It Does:** Moves GPU memory management from the CPU to a dedicated hardware scheduler on the GPU. This reduces CPU overhead for scheduling GPU work, potentially lowering input latency and improving frame pacing. Requires Windows 10 2004+ and a compatible GPU driver.

```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers]
"HwSchMode"=dword:00000002
```

| Value | Description |
|-------|-------------|
| 1 | Disabled (default on older hardware) |
| 2 | Enabled |

```powershell
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" -Name "HwSchMode" -Value 2 -Type DWord
```

**Requirements:**
- Windows 10 version 2004 (build 19041) or later
- NVIDIA: Driver 450.80+ (Turing/Ada/Ampere/Blackwell)
- AMD: RDNA 2+ (RX 6000 series and newer) with Adrenalin 21.9.1+
- Intel: Arc series (not supported on integrated graphics)

**Note:** HAGS can occasionally cause instability in older titles. If you experience crashes or artifacts, revert to value 1.

### 3.2 Fullscreen Optimizations

**What It Does:** Windows 10/11 defaults to "optimized fullscreen" which is a borderless fullscreen mode that gives the Desktop Window Manager (DWM) compositor control over frame presentation. Disabling this forces exclusive fullscreen, which bypasses DWM for lower latency.

```
[HKEY_CURRENT_USER\System\GameConfigStore]
"GameDVR_FSEBehaviorMode"=dword:00000002
"GameDVR_HonorUserFSEBehaviorMode"=dword:00000001
```

| FSEBehaviorMode Value | Behavior |
|----------------------|----------|
| 0 | Let Windows decide (default) |
| 1 | Force fullscreen optimizations ON (borderless) |
| 2 | Force fullscreen optimizations OFF (exclusive fullscreen) |

```powershell
Set-ItemProperty -Path "HKCU:\System\GameConfigStore" -Name "GameDVR_FSEBehaviorMode" -Value 2 -Type DWord
Set-ItemProperty -Path "HKCU:\System\GameConfigStore" -Name "GameDVR_HonorUserFSEBehaviorMode" -Value 1 -Type DWord
```

**Per-Game Override:**
Some games respect per-application compatibility settings. Right-click game executable > Properties > Compatibility > "Disable fullscreen optimizations."

**When to Use:**
- Competitive/twitch games (FPS, fighting): Disable for lowest input latency
- Story/casual games: Optimized fullscreen (borderless) is fine and allows faster Alt-Tab

### 3.3 GPU Priority and System Profile

**What It Does:** The Windows multimedia system profile allocates GPU and CPU scheduling priority for games. These values tell the scheduler to treat game workloads as high-priority.

```
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Games]
"GPU Priority"=dword:00000008
"Priority"=dword:00000006
"SFIO Priority"="High"
"Scheduling Category"="High"
"Background Only"="False"
```

| Value | Type | Range | Description |
|-------|------|-------|-------------|
| `GPU Priority` | DWORD | 1-31 | GPU scheduling priority for game processes (8 recommended) |
| `Priority` | DWORD | 1-6 | CPU thread priority level (6 = High) |
| `SFIO Priority` | String | Low/Normal/High | Scheduler I/O priority for game processes |
| `Scheduling Category` | String | Low/Medium/High | MMCSS scheduling category |
| `Background Only` | String | True/False | Whether the profile applies only to background tasks |

```powershell
$path = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Games"
Set-ItemProperty -Path $path -Name "GPU Priority" -Value 8 -Type DWord
Set-ItemProperty -Path $path -Name "Priority" -Value 6 -Type DWord
Set-ItemProperty -Path $path -Name "SFIO Priority" -Value "High" -Type String
Set-ItemProperty -Path $path -Name "Scheduling Category" -Value "High" -Type String
Set-ItemProperty -Path $path -Name "Background Only" -Value "False" -Type String
```

### 3.4 NVIDIA-Specific GPU Settings

**Evidence Score: 4/5** -- NVIDIA documents these settings; real-world impact varies by game and system configuration.

```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000]
"PerfLevelSrc"=dword:00008888
"PowerMizerEnable"=dword:00000001
"PowerMizerLevel"=dword:00000001
"PowerMizerLevelAC"=dword:00000001
```

| Value | Description |
|-------|-------------|
| `PerfLevelSrc` | 0x8888 = force maximum performance state |
| `PowerMizerEnable` | 1 = enable PowerMizer control |
| `PowerMizerLevel` | 1 = Prefer Maximum Performance |
| `PowerMizerLevelAC` | 1 = Prefer Maximum Performance (when on AC power) |

**Low Latency Mode (Reflex):**
```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000]
"RLagQl"=dword:00000004
```

| Value | Description |
|-------|-------------|
| 0 | Off |
| 1 | On |
| 4 | Ultra (minimum render queue) |

### 3.5 AMD-Specific GPU Settings

**Evidence Score: 3/5** -- AMD documents Anti-Lag and related features; independent testing shows 5-15ms latency reduction in supported titles.

**Radeon Anti-Lag:**
```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000]
"EnableUlps"=dword:00000000
"EnableUlps_NA"=dword:00000000
```

- Disable Ultra Low Power State (ULPS) to prevent GPU from entering deep sleep states that cause micro-stutter.
- Anti-Lag is best configured through AMD Software: Adrenalin Edition rather than registry.

**AMD Power Settings:**
```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000]
"PP_SclkDeepSleepDisable"=dword:00000001
"PP_ThermalAutoThrottlingEnable"=dword:00000000
```

| Value | Description |
|-------|-------------|
| `PP_SclkDeepSleepDisable` | 1 = Prevent GPU clock from dropping to deep sleep |
| `PP_ThermalAutoThrottlingEnable` | 0 = Disable thermal throttling (USE WITH CAUTION -- risk of overheating) |

**Warning:** Disabling thermal throttling can cause permanent GPU damage. Only use if you have adequate cooling and monitoring.

---

## 4. Timer Resolution

**Evidence Score: 3/5** -- MMCSS scheduling is well-documented by Microsoft. Timer resolution benefits are real but often overstated; impact is most measurable in CPU-bound scenarios.

### What It Does

Windows uses a timer interrupt to schedule threads. The default system timer resolution is approximately 15.6ms (60Hz). Lowering this to 1ms improves:

- **Frame pacing** -- More precise scheduling reduces frame time variance.
- **Input latency** -- Mouse and keyboard input is polled at higher frequency.
- **Audio latency** -- Reduces audio buffer underruns in real-time audio engines.

### MMCSS Task Profiles for Games

```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation]
```

The `Win32PrioritySeparation` value controls how the Windows scheduler handles foreground applications. The recommended gaming value is `26` (hex `0x1A`) which provides:

- Short, variable quantums for foreground threads
- Separate foreground/background priority classes

```
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Task\Games]
"Scheduling Category"="High"
"SFIO Priority"="High"
"Background Only"="False"
"Clock Rate"=dword:00002710
"GPU Priority"=dword:00000008
"Priority"=dword:00000006
```

| Value | Description |
|-------|-------------|
| `Clock Rate` | Timer tick rate in 100ns units (0x2710 = 10000 = 1ms) |
| `Scheduling Category` | "High" = MMCSS treats game threads as high-priority |
| `Background Only` | "False" = Profile applies to foreground game processes |

### Timer Resolution API

Applications can request higher timer resolution via the Windows Multimedia API:

```c
#include <windows.h>
#pragma comment(lib, "winmm.lib")

// Request 1ms timer resolution
timeBeginPeriod(1);

// ... game loop ...

// Restore default resolution
timeEndPeriod(1);
```

**Note:** Many modern game engines (Unreal Engine 5, Unity 2022+) already call `timeBeginPeriod(1)` automatically when running. This is an engine-level optimization, not a system-level one.

### Global Timer Resolution Requests

```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\kernel]
"GlobalTimerResolutionRequests"=dword:00000001
```

This registry value forces Windows to maintain high-resolution timers globally when any application requests them, preventing the system from falling back to lower resolution when an application releases its timer request.

```powershell
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\kernel" -Name "GlobalTimerResolutionRequests" -Value 1 -Type DWord
```

### Caveats

- Higher timer resolution increases power consumption and interrupt frequency. Laptops may see reduced battery life.
- The Windows 10 2004+ timer infrastructure improvements have reduced the benefit of manual timer resolution tweaks.
- If no application requests high resolution, the global timer resolution will still fall back to 15.6ms regardless of this registry setting.

---

## 5. Network Optimization for Gaming

**Evidence Score: 4/5** -- Nagle's Algorithm and network throttling index changes are well-documented with measurable latency reductions in online gaming scenarios.

### 5.1 Disable Nagle's Algorithm

**What It Does:** Nagle's Algorithm batches small TCP packets to improve network efficiency. For gaming, this introduces unnecessary latency as small packets (player input, position updates) are held briefly before transmission.

```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{INTERFACE_GUID}]
"TcpAckFrequency"=dword:00000001
"TcpNoDelay"=dword:00000001
"TcpDelAckTicks"=dword:00000000
```

| Value | Description |
|-------|-------------|
| `TcpAckFrequency` | 1 = Send ACK immediately (disable delayed ACK) |
| `TcpNoDelay` | 1 = Disable Nagle's Algorithm |
| `TcpDelAckTicks` | 0 = No delayed ACK timer (immediate acknowledgment) |

**Important:** You must apply these to each network adapter interface. The `{INTERFACE_GUID}` is a per-adapter identifier.

```powershell
# Find your active network adapter GUIDs
Get-NetAdapter | Where-Object {$_.Status -eq "Up"} | Select-Object Name, InterfaceGuid

# Apply to a specific adapter (replace GUID)
$adapterPath = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{YOUR-ADAPTER-GUID}"
Set-ItemProperty -Path $adapterPath -Name "TcpAckFrequency" -Value 1 -Type DWord
Set-ItemProperty -Path $adapterPath -Name "TcpNoDelay" -Value 1 -Type DWord
Set-ItemProperty -Path $adapterPath -Name "TcpDelAckTicks" -Value 0 -Type DWord

# Apply to ALL active adapters
$adapters = Get-NetAdapter | Where-Object {$_.Status -eq "Up"}
foreach ($adapter in $adapters) {
    $path = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$($adapter.InterfaceGuid)"
    Set-ItemProperty -Path $path -Name "TcpAckFrequency" -Value 1 -Type DWord
    Set-ItemProperty -Path $path -Name "TcpNoDelay" -Value 1 -Type DWord
    Set-ItemProperty -Path $path -Name "TcpDelAckTicks" -Value 0 -Type DWord
    Write-Host "Applied TCP optimizations to: $($adapter.Name)"
}
```

### 5.2 Network Throttling Index

**What It Does:** Windows throttles non-multimedia network traffic when multimedia workloads are active. The default throttling index is 10 (decimal). Setting it to maximum disables throttling entirely.

```
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile]
"NetworkThrottlingIndex"=dword:ffffffff
"SystemResponsiveness"=dword:00000000
```

| Value | Description |
|-------|-------------|
| `NetworkThrottlingIndex` | `0xFFFFFFFF` (4294967295) = disable throttling completely |
| `SystemResponsiveness` | 0 = minimize reserved CPU for background/system tasks |

```powershell
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" -Name "NetworkThrottlingIndex" -Value 0xFFFFFFFF -Type DWord
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" -Name "SystemResponsiveness" -Value 0 -Type DWord
```

### 5.3 MSS Throttling

Maximum Segment Size (MSS) throttling limits TCP segment sizes to prevent network congestion. For gaming with low-latency connections, this can be disabled:

```powershell
netsh int tcp set global mss=disable
```

Verify:
```powershell
netsh int tcp show global
```

### 5.4 DNS Optimization

Low-latency DNS servers reduce the initial connection time to game servers and reduce DNS lookup stutter.

**Recommended Low-Latency DNS Servers:**

| Provider | Primary | Secondary | Avg Latency |
|----------|---------|-----------|-------------|
| Cloudflare | 1.1.1.1 | 1.0.0.1 | ~11ms globally |
| Google | 8.8.8.8 | 8.8.4.4 | ~20ms globally |
| Quad9 | 9.9.9.9 | 149.112.112.112 | ~15ms globally |
| Cloudflare Gaming | 1.1.1.2 | 1.0.0.2 | ~11ms (includes malware blocking) |

```powershell
# Set Cloudflare DNS on active adapter
$adapter = Get-NetAdapter | Where-Object {$_.Status -eq "Up"} | Select-Object -First 1
Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses ("1.1.1.1", "1.0.0.1")

# Set Google DNS alternative
# Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses ("8.8.8.8", "8.8.4.4")
```

### 5.5 Additional Network Tuning

```powershell
# Enable TCP window auto-tuning (should remain enabled on modern networks)
netsh int tcp set global autotuninglevel=normal

# Enable Direct Cache Access (reduces CPU overhead for network packets)
netsh int tcp set global dca=enabled

# Disable ICMP redirects (prevents route changes mid-game)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" -Name "EnableICMPRedirect" -Value 0 -Type DWord

# Optimize DNS cache
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters" -Name "MaxCacheEntryTtlLimit" -Value 86400 -Type DWord
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters" -Name "MaxSOACacheEntryTtlLimit" -Value 120 -Type DWord
```

---

## 6. Power Plan for Gaming

**Evidence Score: 5/5** -- Extensively documented by Microsoft, Intel, AMD, and NVIDIA. Power plan settings have the single largest impact on gaming performance of any optimization category.

### 6.1 Power Plan Selection

**Ultimate Performance** is the optimal plan for desktop gaming. It is hidden by default and must be enabled.

```powershell
# Enable Ultimate Performance power plan (hidden by default)
powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61

# Set as active plan
powercfg -setactive e9a42b02-d5df-448d-aa00-03f14749eb61

# Alternative: High Performance plan (if Ultimate Performance unavailable)
powercfg -setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
```

**Power Plan Comparison for Gaming:**

| Plan | EPP Range | Core Parking | Boost Mode | Best For |
|------|-----------|-------------|------------|----------|
| Ultimate Performance | 0 (fixed) | Disabled | Aggressive | Desktop gaming |
| High Performance | 0 (fixed) | Disabled | Aggressive | Desktop gaming |
| Balanced | 0-100 (dynamic) | Enabled | Enabled | General use |
| Power Saver | 50-100 | Aggressive | Disabled | Battery life |

### 6.2 Core Parking

**What It Does:** Core parking allows Windows to put CPU cores into a deep sleep state to save power. During gaming, parked cores cause micro-stutters when the scheduler wakes them to handle sudden workload spikes.

```powershell
# Disable core parking (minimum active state = 100%)
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 100

# Apply changes
powercfg -setactive SCHEME_CURRENT
```

```powershell
# Verify current settings
powercfg -query SCHEME_CURRENT SUB_PROCESSOR CPMINCORES
```

**Note on Modern CPUs:** On Intel 12th gen+ and AMD Ryzen 7000+ with heterogeneous architectures, core parking is managed more intelligently by the OS and driver stack. Manual core parking disabling has diminished returns and may interfere with the Thread Director on hybrid CPUs. See Section 11 (Myths Debunked) for details.

### 6.3 Energy Performance Preference (EPP)

```powershell
# Set EPP to maximum performance (0)
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFENERGYPERF 0

# Apply
powercfg -setactive SCHEME_CURRENT
```

| EPP Value | Behavior |
|-----------|----------|
| 0 | Maximum performance -- no power saving |
| 25 | Aggressive performance |
| 50 | Balanced |
| 75 | Power saving |
| 100 | Maximum power saving |

### 6.4 Processor Boost Mode

```powershell
# Disable turbo boost (not recommended for gaming)
# powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0

# Enable aggressive turbo boost (recommended for gaming)
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 1

# Apply
powercfg -setactive SCHEME_CURRENT
```

| Boost Mode | Value | Description |
|-----------|-------|-------------|
| Disabled | 0 | No turbo boost |
| Aggressive | 1 | Maximum boost frequency (recommended for gaming) |
| Efficient Enabled | 2 | Boost enabled, power-efficient |
| Efficient Aggressive | 4 | Aggressive boost within power budget (recommended for hybrid CPUs) |

**For Hybrid CPUs (Intel 12th gen+, AMD Ryzen with 3D V-Cache):**
Use boost mode 4 (Efficient Aggressive) to let the Thread Director manage P-core/E-core scheduling while still allowing high boost clocks.

### 6.5 Minimum Processor State

```powershell
# Set minimum processor state to 50% (prevents deep C-states while idle in menus)
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 50

# Or 100% for absolute minimum latency (higher power consumption)
# powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 100

# Apply
powercfg -setactive SCHEME_CURRENT
```

### 6.6 Complete Gaming Power Plan Script

```powershell
Write-Host "=== Applying Gaming Power Plan ===" -ForegroundColor Cyan

# Enable Ultimate Performance plan
powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 2>$null
powercfg -setactive e9a42b02-d5df-448d-aa00-03f14749eb61

# Processor settings
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 100        # No core parking
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFENERGYPERF 0      # Max performance
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 1       # Aggressive boost
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 50    # Min state 50%

# Disable USB selective suspend
powercfg -setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0

# Disable PCI Express link state power management
powercfg -setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0

# Apply all changes
powercfg -setactive SCHEME_CURRENT

Write-Host "Gaming power plan applied successfully." -ForegroundColor Green
Write-Host ""
Write-Host "Current active plan:" -ForegroundColor Yellow
powercfg -getactivescheme
```

---

## 7. Visual Effects for Gaming

**Evidence Score: 3/5** -- Reducing desktop compositing overhead provides measurable FPS gains (typically 1-5%), primarily on systems with integrated graphics or older GPUs.

### What It Does

Windows desktop visual effects (transparency, animations, shadows) consume GPU resources through the Desktop Window Manager (DWM). While modern discrete GPUs handle this easily, these effects can:

- Cause GPU contention in fullscreen windowed games.
- Add 1-2ms of compositing latency in borderless fullscreen mode.
- Consume VRAM on systems with limited video memory.

### Visual Effects Configuration

```powershell
# Set visual effects to "Adjust for best performance"
# This disables all visual effects
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" -Name "VisualFXSetting" -Value 2 -Type DWord

# Or selectively disable specific effects (recommended approach)
$disableEffects = @(
    "Animation"                          # Taskbar/menu animations
    "ClientAreaAnimation"                # Window client area animations
    "ComboAnimation"                     # ComboBox dropdown animations
    "CursorShadow"                       # Cursor shadow effect
    "DragFullWindows"                    # Show window contents while dragging (disable for performance)
    "GradientCaptions"                   # Gradient caption bars
    "ListBoxSmoothScrolling"             # Smooth scrolling in listboxes
    "MenuAnimation"                      # Menu fade/slide animations
    "SelectionFade"                      # Selection fade effect
    "TaskbarAnimations"                  # Taskbar button animations
    "TooltipAnimation"                   # Tooltip fade animations
)

foreach ($effect in $disableEffects) {
    Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name $effect -Value "0" -Type String
}

Write-Host "Visual effects optimized for gaming performance." -ForegroundColor Green
```

### Disable Transparency

```powershell
# Disable Windows transparency effects (Acrylic/Mica)
Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize" -Name "EnableTransparency" -Value 0 -Type DWord

# Disable Acrylic blur on Start Menu and Taskbar
New-Item -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize" -Name "EnableTransparency" -Value 0 -Type DWord
```

### Disable Window Animations

```powershell
# Disable minimize/maximize animations
Set-ItemProperty -Path "HKCU:\Control Panel\Desktop\WindowMetrics" -Name "MinAnimate" -Value "0" -Type String

# Disable smooth scrolling
Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "SmoothScroll" -Value 0 -Type DWord
```

### GUI Path

Settings > System > About > Advanced System Settings > Performance Settings > "Adjust for best performance"

For selective control, uncheck specific animations you want to keep (e.g., "Smooth edges of screen fonts" for readability).

---

## 8. Storage Optimization for Gaming

**Evidence Score: 4/5** -- TRIM, free space management, and indexing impacts are well-documented with measurable load time and streaming performance improvements.

### 8.1 TRIM for SSDs

**What It Does:** TRIM tells the SSD which data blocks are no longer in use, allowing the drive to perform garbage collection efficiently. Without TRIM, SSD write performance degrades over time as the drive fills up with stale data.

```powershell
# Check if TRIM is enabled
fsutil behavior query DisableDeleteNotify
# Returns: DisableDeleteNotify = 0 (TRIM enabled) or 1 (TRIM disabled)

# Enable TRIM (should be default on Windows 10/11)
fsutil behavior set DisableDeleteNotify 0

# Verify TRIM is active
Get-Volume | Where-Object {$_.FileSystemType -eq "NTFS"} | Select-Object DriveLetter, FileSystem, HealthStatus
```

### 8.2 Free Space Management

**Recommendation:** Maintain at least 20% free space on game drives.

- **SSDs:** Free space enables wear leveling and garbage collection. Below 20% free, SSD performance degrades significantly.
- **HDDs:** Free space reduces fragmentation and improves sequential read performance for game asset loading.

```powershell
# Check free space on all drives
Get-PSDrive -PSProvider FileSystem | Select-Object Name, 
    @{N='Used (GB)'; E={[math]::Round($_.Used/1GB, 1)}},
    @{N='Free (GB)'; E={[math]::Round($_.Free/1GB, 1)}},
    @{N='Total (GB)'; E={[math]::Round(($_.Used + $_.Free)/1GB, 1)}},
    @{N='Free %'; E={[math]::Round($_.Free/($_.Used + $_.Free) * 100, 1)}}

# Warn about low free space
$drives = Get-PSDrive -PSProvider FileSystem
foreach ($drive in $drives) {
    $totalGB = [math]::Round(($drive.Used + $drive.Free)/1GB, 1)
    $freePercent = [math]::Round($drive.Free/($drive.Used + $drive.Free) * 100, 1)
    if ($freePercent -lt 20 -and $totalGB -gt 10) {
        Write-Warning "Drive $($drive.Name): Only $freePercent% free ($([math]::Round($drive.Free/1GB, 1)) GB) - recommend 20%+ free"
    }
}
```

### 8.3 Disable Search Indexing on Game Folders

**What It Does:** Windows Search indexes file contents for fast file searching. Game directories contain thousands of large, rarely-searched files (textures, models, audio). Indexing these consumes disk I/O and CPU.

```powershell
# Disable search indexing on a specific folder (e.g., game library)
$gamePaths = @(
    "D:\Games",
    "E:\SteamLibrary",
    "F:\GameInstalls"
)

foreach ($path in $gamePaths) {
    if (Test-Path $path) {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.Namespace($path)
        $folderItem = $folder.Self
        # Set folder attributes to exclude from indexing
        $folderItem.InvokeVerb("properties")
        Write-Host "Manually disable indexing for: $path (Right-click > Properties > Advanced > uncheck 'Allow files to have contents indexed')"
    }
}

# Or use Registry to exclude paths
# Windows Search uses the registry to track indexed locations
# The recommended approach is GUI-based: Right-click folder > Properties > Advanced > Uncheck indexing
```

**Alternative via Group Policy:**
```
Computer Configuration > Administrative Templates > Windows Components > Search > "Default excluded paths"
```

### 8.4 Additional Storage Optimizations

```powershell
# Enable write caching on SSDs (improves write performance)
$disk = Get-WmiObject Win32_DiskDrive | Where-Object {$_.MediaType -eq "SSD"}
# Note: Write caching is typically enabled by default on SSDs

# Disable last access time stamp (reduces metadata writes)
fsutil behavior set disablelastaccess 1

# Disable 8.3 short name creation (reduces metadata overhead)
fsutil behavior set disable8dot3 1

# Optimize NTFS memory usage
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "NtfsMemoryUsage" -Value 2 -Type DWord

# Increase NTFS MFT zone (improves MFT allocation for large directories)
fsutil behavior set mftzone 2
```

---

## 9. Background Process Management

**Evidence Score: 3/5** -- Background process impact varies significantly by system configuration. Disabling known resource hogs provides measurable gains; blanket optimization has diminishing returns.

### 9.1 Identify Resource-Heavy Processes

```powershell
# Find top CPU-consuming processes
Get-Process | Sort-Object CPU -Descending | Select-Object -First 20 Name, CPU, WorkingSet64, Id |
    Format-Table -AutoSize

# Find top memory consumers
Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 20 Name,
    @{N='Memory (MB)'; E={[math]::Round($_.WorkingSet64/1MB, 1)}}, Id |
    Format-Table -AutoSize

# Find processes with high I/O
Get-Process | Where-Object {$_.Id -ne 0} | ForEach-Object {
    try {
        $io = $_.IO
        [PSCustomObject]@{
            Name = $_.Name
            Id = $_.Id
            ReadBytes = [math]::Round($_.IO.ReadBytes/1MB, 2)
            WriteBytes = [math]::Round($_.IO.WriteBytes/1MB, 2)
        }
    } catch {}
} | Sort-Object ReadBytes -Descending | Select-Object -First 10
```

### 9.2 Common Background Processes to Disable for Gaming

```powershell
# Services safe to disable for gaming sessions
$optionalServices = @(
    @{Name="SysMain"; Desc="Superfetch/SysMain (preloads apps)"; Note="Disable on SSD systems"},
    @{Name="WSearch"; Desc="Windows Search Indexer"; Note="Disable temporarily for gaming"},
    @{Name="DiagTrack"; Desc="Connected User Experiences and Telemetry"; Note="Microsoft telemetry"},
    @{Name="dmwappushservice"; Desc="WAP Push Message Routing Service"; Note="Not needed for gaming"},
    @{Name="Fax"; Desc="Fax Service"; Note="Unlikely to be needed"},
    @{Name="MapsBroker"; Desc="Downloaded Maps Manager"; Note="Not needed for gaming"},
    @{Name="lfsvc"; Desc="Geolocation Service"; Note="Not needed for gaming"},
    @{Name="SharedAccess"; Desc="Internet Connection Sharing"; Note="Unless sharing connection"},
    @{Name="RemoteRegistry"; Desc="Remote Registry"; Note="Security risk; disable"},
    @{Name="RetailDemo"; Desc="Retail Demo Service"; Note="Not needed"}
)

Write-Host "Optional services to disable for gaming:" -ForegroundColor Cyan
foreach ($svc in $optionalServices) {
    $status = (Get-Service -Name $svc.Name -ErrorAction SilentlyContinue).Status
    if ($status -eq "Running") {
        Write-Host "  RUNNING: $($svc.Name) - $($svc.Desc) [$($svc.Note)]" -ForegroundColor Yellow
    }
}

# To disable a service:
# Stop-Service -Name "ServiceName" -Force
# Set-Service -Name "ServiceName" -StartupType Disabled
```

### 9.3 MMCSS Task Profiles

The Multimedia Class Scheduler Service (MMCSS) allocates CPU and I/O resources to multimedia and game workloads. Verify game profiles are configured correctly:

```
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile]
```

```
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games]
"Affinity"=dword:00000000
"Background Only"="False"
"Clock Rate"=dword:00002710
"GPU Priority"=dword:00000008
"Priority"=dword:00000006
"Scheduler Index"=dword:00000000
"Scheduling Category"="High"
"SFIO Priority"="High"
"BackgroundPriority"=dword:00000000
```

### 9.4 Startup Management

```powershell
# List all startup items
Get-CimInstance Win32_StartupCommand | Select-Object Name, Command, Location, User |
    Format-Table -AutoSize -Wrap

# Disable startup items via Task Manager registry locations
$startupPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$startupItems = Get-ItemProperty -Path $startupPath
$startupItems.PSObject.Properties | Where-Object {$_.Name -notlike "PS*"} | ForEach-Object {
    Write-Host "Startup: $($_.Name) = $($_.Value)" -ForegroundColor Gray
}
```

---

## 10. AMD vs NVIDIA Specific Optimizations

**Evidence Score: 4/5** -- Both vendors document these settings. NVIDIA Low Latency Mode and AMD Anti-Lag have measurable latency reductions in supported titles.

### 10.1 NVIDIA Optimizations

#### Low Latency Mode (NVIDIA Reflex)

NVIDIA Reflex minimizes the render queue to reduce input-to-display latency. Available on Kepler (GTX 600) and newer.

```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000]
"RLagQl"=dword:00000004
```

| Value | Mode | Latency Impact |
|-------|------|---------------|
| 0 | Off | Default render queue depth |
| 1 | On | Reduced render queue |
| 4 | Ultra | Minimum render queue; highest CPU overhead but lowest latency |

**Note:** Ultra mode may reduce FPS by 3-5% but reduces input latency by 5-15ms in supported games.

#### Power Management Mode

```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000]
"PerfLevelSrc"=dword:00008888
"PowerMizerEnable"=dword:00000001
"PowerMizerLevel"=dword:00000001
"PowerMizerLevelAC"=dword:00000001
```

| Setting | Value | Description |
|---------|-------|-------------|
| Power Management Mode | Prefer Maximum Performance | Prevents GPU from downclocking |
| Texture Filtering Quality | High Performance | Trades minor visual quality for performance |
| Threaded Optimization | On | Enables multi-threaded rendering pipeline |

#### NVIDIA Profile Inspector Settings

```powershell
# These settings are applied through NVIDIA Profile Inspector or nvidia-smi

# Set power management mode to maximum performance
nvidia-smi -ac 5001,1500  # Example: memory and GPU clock offset

# Verify current GPU clocks and power state
nvidia-smi -q -d CLOCK,POWER
```

### 10.2 AMD Optimizations

#### Radeon Anti-Lag

AMD Anti-Lag reduces input-to-display latency by synchronizing CPU and GPU frame submission. Available on GCN 1.0 (RX 200 series) and newer.

**Configuration via AMD Software:**
- AMD Software: Adrenalin Edition > Gaming > Graphics > Anti-Lag = Enabled

**Registry equivalent:**
```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000]
"KMD_AntiLagEnable"=dword:00000001
"KMD_AntiLagOverride"=dword:00000001
```

#### AMD Power Settings

```
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000]
"PP_SclkDeepSleepDisable"=dword:00000001
"KMD_PowerControlEnabled"=dword:00000001
"KMD_PowerControlOD_DPM_PerfLevel"=dword:00000002
```

| Setting | Description |
|---------|-------------|
| `PP_SclkDeepSleepDisable` | Prevents GPU clock from entering deep sleep states |
| `KMD_PowerControlEnabled` | Enables driver-level power management control |
| `KMD_PowerControlOD_PerfLevel` | Performance level override (0=power saving, 2=overdrive) |

#### AMD Radeon Chill

Radeon Chill dynamically adjusts frame rates based on input activity to reduce power consumption and heat. NOT recommended for competitive gaming but useful for reducing GPU load in less intense scenes.

**Configuration:** AMD Software > Gaming > Graphics > Radeon Chill = Configure per-game

### 10.3 Vendor Comparison Table

| Feature | NVIDIA | AMD | Impact |
|---------|--------|-----|--------|
| Low Latency | Reflex (Ultra mode) | Anti-Lag | Both reduce 5-15ms input latency |
| Power Management | Prefer Maximum Performance | Overdrive / Anti-Lag | Prevents clock throttling |
| Frame Sync | G-SYNC / G-SYNC Compatible | FreeSync | Eliminates tearing without V-Sync lag |
| Upscaling | DLSS (Tensor cores) | FSR (open source) | Both improve FPS; DLSS is higher quality |
| Frame Generation | DLSS 3 Frame Gen (RTX 40+) | AFMF (RX 7000+) | AI-generated frames between real frames |
| Driver Overhead | NVIDIA has higher DX11 overhead | AMD has lower DX11 overhead | AMD slightly better in DX11 CPU-limited scenarios |

---

## 11. Myths Debunked

**Evidence Score: 5/5** -- These myths have been extensively analyzed and debunked through controlled testing by hardware reviewers, game developers, and Microsoft engineers.

### Myth 1: "Disable CPU Core Parking for Gaming"

**Status: MYTH on modern CPUs**

- Core parking was a significant issue on Windows 7 and early Windows 8. Microsoft completely reworked the parking algorithm in Windows 8.1/10.
- On modern CPUs (Intel 8th gen+, AMD Ryzen), core parking adds negligible latency (sub-microsecond wake time).
- On hybrid architectures (Intel 12th gen+, AMD Ryzen with mixed CCD/IOD), manual core parking disabling can interfere with the OS Thread Director and reduce performance.
- Setting `CPMINCORES=100` may actually hurt performance on hybrid CPUs by preventing efficient core utilization during non-game background tasks.
- **Modern recommendation:** Leave core parking enabled. Use "Ultimate Performance" or "High Performance" power plans which already manage parking intelligently.

### Myth 2: "Set Process Affinity for Games"

**Status: MYTH**

- Setting process affinity manually almost always hurts performance on modern CPUs.
- Windows scheduler is designed to migrate threads across cores for optimal load balancing.
- On Intel hybrid CPUs, the Thread Director knows which cores are P-cores vs E-cores and assigns threads accordingly. Manual affinity can force game threads onto slow E-cores.
- On AMD Ryzen, the scheduler understands CCX/CCD topology and optimizes for cache locality. Manual affinity can break this optimization.
- The only legitimate use case is debugging specific application bugs where a process misbehaves with certain cores.
- **Modern recommendation:** Never manually set process affinity for games.

### Myth 3: "Disable HPET for Gaming"

**Status: DEPRECATED**

- HPET (High Precision Event Timer) was problematic on some early Windows 10 builds and certain AMD platforms (Ryzen 1000/2000 series).
- Microsoft and AMD fixed HPET-related timing issues in Windows 10 1903+ and AGESA updates for Ryzen 3000+.
- On modern hardware, HPET provides accurate timekeeping and does not cause frame pacing issues.
- Disabling HPET can actually cause problems: some games and anti-cheat systems rely on HPET for timing.
- **Modern recommendation:** Leave HPET enabled. If you suspect timing issues, check for BIOS/firmware updates rather than disabling HPET.

```powershell
# Verify HPET status (for reference only; do NOT change without cause)
bcdedit /enum | findstr /i "useplatformclock"
# If "useplatformclock Yes" is set, HPET is being used
# Only disable if you have verified timing issues AND are on older hardware:
# bcdedit /set useplatformclock false
```

### Myth 4: "Disable V-Sync Globally for Gaming"

**Status: SITUATIONAL**

- Disabling V-Sync globally eliminates input latency from the V-Sync buffer (1-2 frames) but introduces screen tearing.
- Tearing is visually distracting and can be worse than the latency trade-off for many players.
- The correct approach depends on your hardware:
    - **G-SYNC / FreeSync monitors:** Disable V-Sync in-game AND in driver, rely on variable refresh rate.
    - **60Hz fixed refresh monitors:** Keep V-Sync on to avoid tearing; consider RTSS scanline sync for lower latency without tearing.
    - **144Hz+ monitors:** Tearing is less visible at high refresh rates; V-Sync off is acceptable.
- **Modern recommendation:** Use adaptive sync (G-SYNC/FreeSync) as the primary approach. Disable V-Sync only if you have a VRR display.

### Additional Debunked Myths

| Myth | Status | Reality |
|------|--------|---------|
| "Disable hyper-threading for gaming" | DEPRECATED | SMT/HT improves gaming performance 5-15% in modern titles |
| "More RAM speed doesn't matter" | FALSE | DDR5-6000 vs DDR4-2100 can mean 10-20% FPS difference in CPU-bound titles |
| "Higher MHz always means faster" | FALSE | Memory latency (CAS latency) matters as much as frequency |
| "Regedit tweaks give 50% FPS boost" | FALSE | Most registry tweaks provide 0-5% improvement at best |
| "Clean Windows install always better" | SITUATIONAL | Fresh installs help when accumulated bloat exists; otherwise minimal impact |

---

## Quick Reference: Gaming Optimization Checklist

### High Impact (Do These First)

| # | Optimization | Impact | Risk |
|---|-------------|--------|------|
| 1 | Enable Ultimate Performance power plan | High | None |
| 2 | Disable Game DVR / Game Bar | Medium-High | Lose Xbox features |
| 3 | Set GPU to maximum performance mode | Medium | Slightly higher power draw |
| 4 | Disable fullscreen optimizations (competitive games) | Medium | Loses borderless Alt-Tab |
| 5 | Enable HAGS (if supported) | Low-Medium | Possible instability in old games |

### Medium Impact (Worth Doing)

| # | Optimization | Impact | Risk |
|---|-------------|--------|------|
| 6 | Set Games MMCSS profile to High priority | Low-Medium | Minimal |
| 7 | Disable Nagle's Algorithm | Low-Medium | None on wired; be cautious on WiFi |
| 8 | Set DNS to Cloudflare 1.1.1.1 | Low | None |
| 9 | Disable network throttling index | Low | None |
| 10 | Disable unnecessary startup items | Low-Medium | May break app functionality |

### Low Impact (Optional Polish)

| # | Optimization | Impact | Risk |
|---|-------------|--------|------|
| 11 | Disable desktop transparency/animations | Low | Visual quality loss |
| 12 | Disable search indexing on game folders | Low | Can't search game files via Windows Search |
| 13 | Ensure TRIM is enabled | Maintenance | None |
| 14 | Set timer resolution to 1ms | Low | Higher power consumption |
| 15 | Disable Xbox services | Low | Breaks Xbox ecosystem |

### Avoid (Myths and Harmful Tweaks)

| # | Don't Bother | Why |
|---|-------------|-----|
| 1 | Manual process affinity | Hrts performance on modern CPUs |
| 2 | Disabling HPET | Fixed on modern hardware; causes more problems |
| 3 | Disabling core parking on hybrid CPUs | Interferes with Thread Director |
| 4 | Applying random registry "speed hacks" | Most are fake or harmful |
| 5 | Disabling SMT/Hyper-Threading | Costs 5-15% FPS in modern titles |

---

*Document version: 2026-08-31*
*Last updated: 2026-08-31*
*Evidence scores are based on available documentation, independent benchmarks, and vendor disclosures as of the document date.*
