# Windows GPU Optimization Settings — Complete Technical Reference

---

## 1. HAGS (Hardware-Accelerated GPU Scheduling)

### Overview
HAGS offloads GPU video memory scheduling from the Windows CPU-based scheduler to the GPU's own scheduling processor. Introduced in Windows 10 v2004 (May 2020 Update).

### Registry Path
```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers
```
| Value Name | Type | Data | Meaning |
|---|---|---|---|
| `HwSchMode` | DWORD | `1` | Disabled |
| `HwSchMode` | DWORD | `2` | Enabled |

### GUI Path
Settings > System > Display > Graphics > Change default graphics settings > "Hardware-accelerated GPU scheduling"

### Command (PowerShell)
```powershell
# Enable
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" -Name "HwSchMode" -Value 2 -Type DWord

# Disable
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" -Name "HwSchMode" -Value 1 -Type DWord
```

### Vendor Support

| Vendor | Minimum GPU | Minimum Driver | Notes |
|---|---|---|---|
| **NVIDIA** | GTX 10-series (Pascal) unofficial; RTX 20-series (Turing) official | 450.80+; recommended 546.xx+ (2024) / 550-560 series (2025) | Best results on RTX 30/40/50 series. RTX 50-series (Blackwell) strongly recommends HAGS for G-SYNC and Reflex |
| **AMD** | RX 400-series (Polaris) and newer | AMD Adrenalin 2020+ | Mixed results; RDNA 2/3 show slight improvements |
| **Intel** | Intel Arc / 12th-gen+ iGPU | Intel Arc driver packages | Basic support; limited benchmark data |

### Performance Evidence
- Average FPS improvement: **1-3%** (marginal in most titles)
- Frame pacing improvement: Measurable reduction in 1% low frame drops in some titles
- DLSS 3 Frame Generation titles (NVIDIA RTX 40/50) may particularly benefit
- Works synergistically with NVIDIA Reflex for latency reduction
- Some users report occasional stuttering in specific games (inconsistent)

### Default Value
- `2` (Enabled) on fresh Windows 11 installs
- `1` (Disabled) on systems upgraded from Windows 10 pre-2004

### Recommended Value
- **Enable (2)** on RTX 20-series or newer NVIDIA / RDNA 2+ AMD / Intel Arc
- **Disable (1)** on older GPUs or if experiencing unexplained stuttering

### Risk Classification
- **LOW** — Fully reversible on reboot; no driver reinstall needed

### Windows Version Compatibility
- **Windows 10** version 2004 (20H1) and later
- **Windows 11** all versions (enabled by default on fresh installs)

### Evidence Score: 7/10
Consistent across multiple independent benchmarks (Hardware Unboxed, TechPowerUp, community testing). Gains are small but measurable. Variability across game titles.

---

## 2. Fullscreen Optimizations

### Overview
Windows fullscreen optimizations (FSE) hybridize fullscreen exclusive and borderless windowed modes. The OS composites the game window but grants it priority access to the display. Some users/games experience issues with this behavior.

### Primary Registry Keys

#### System-Wide FSE Behavior
```
HKEY_CURRENT_USER\System\GameConfigStore
```
| Value Name | Type | Values | Meaning |
|---|---|---|---|
| `GameDVR_FSEBehaviorMode` | DWORD | `0` = Default, `1` = Force FSE on, `2` = Disable fullscreen optimizations | Master FSE behavior mode |
| `GameDVR_FSEBehavior` | DWORD | `0` = Default, `1` = On, `2` = Off | FSE behavior override |
| `GameDVR_HonorUserFSEBehaviorMode` | DWORD | `0` = Ignore, `1` = Honor user's FSE mode selection | Whether to respect user FSE preference |
| `GameDVR_DXGIHonorFSEWindowsCompatible` | DWORD | `0` = No, `1` = Yes | Honor FSE Windows compatibility via DXGI |
| `GameDVR_EFSEFeatureFlags` | DWORD | `0`/`1` | Enhanced FSE feature flags |
| `GameDVR_BEFHBehaviorMode` | DWORD | `0`/`1`/`2` | BEFH behavior mode |

### Recommended Values for Disabling Fullscreen Optimizations
```reg
[HKEY_CURRENT_USER\System\GameConfigStore]
"GameDVR_FSEBehaviorMode"=dword:00000002
"GameDVR_FSEBehavior"=dword:00000002
"GameDVR_HonorUserFSEBehaviorMode"=dword:00000001
```

### Per-Application Disable (Compatibility Tab Equivalent)
```
HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers
```
Add a **String Value** where:
- **Value name**: Full path to executable (e.g., `C:\Games\game.exe`)
- **Value data**: `~ DISABLEWIN8DPISCALING HIGHDPIAWARE` (also sets high DPI behavior)

To specifically disable fullscreen optimizations per-app, the compatibility tab checkbox corresponds to setting the `~ DISABLEfullscreenoptimizations` flag in the Layers value data string, or more precisely the EXE's compatibility flags.

### PowerShell Commands
```powershell
# Disable fullscreen optimizations system-wide
Set-ItemProperty -Path "HKCU:\System\GameConfigStore" -Name "GameDVR_FSEBehaviorMode" -Value 2 -Type DWord
Set-ItemProperty -Path "HKCU:\System\GameConfigStore" -Name "GameDVR_FSEBehavior" -Value 2 -Type DWord
Set-ItemProperty -Path "HKCU:\System\GameConfigStore" -Name "GameDVR_HonorUserFSEBehaviorMode" -Value 1 -Type DWord
```

### Default Value
- `GameDVR_FSEBehaviorMode` = `0` (Windows default: hybrid mode)
- `GameDVR_FSEBehavior` = `0`

### Recommended Value
- **2** for both `GameDVR_FSEBehaviorMode` and `GameDVR_FSEBehavior` if experiencing stuttering/input lag in fullscreen games
- **0** (default) if no issues are present — the hybrid mode works well for most titles

### Risk Classification
- **LOW** — Fully reversible; may affect Alt+Tab behavior and multi-monitor compositing

### Windows Version Compatibility
- **Windows 10** all versions
- **Windows 11** all versions (behavior modified in some 22H2+ updates)

### Evidence Score: 8/10
Widely documented; the compatibility tab approach is officially supported by Microsoft. Disabling FSE optimizations is a well-established troubleshooting step.

---

## 3. Game Mode

### Overview
Windows Game Mode prioritizes system resources for games, reduces background task interference, and can prevent Windows Update from installing drivers or restarting during gameplay.

### GUI Path
Settings > Gaming > Game Mode

### All Game Bar Registry Keys

#### GameBar Settings
```
HKEY_CURRENT_USER\Software\Microsoft\GameBar
```
| Value Name | Type | Values | Meaning |
|---|---|---|---|
| `AllowAutoGameMode` | DWORD | `0`/`1` | Allow automatic Game Mode activation |
| `AutoGameModeEnabled` | DWORD | `0`/`1` | Game Mode auto-enable state |
| `ShowStartupPanel` | DWORD | `0`/`1` | Show Game Bar panel at game startup |
| `UseNexusForGameBarEnabled` | DWORD | `0`/`1` | Use new Game Bar hub (Nexus UI) |

#### GameConfigStore (GameDVR & Fullscreen)
```
HKEY_CURRENT_USER\System\GameConfigStore
```
| Value Name | Type | Values | Meaning |
|---|---|---|---|
| `GameDVR_Enabled` | DWORD | `0`/`1` | Master Game DVR toggle |
| `GameDVR_HardwareMode` | DWORD | `0`/`1` | Hardware (GPU) encoding for capture |
| `GameDVR_FSEBehavior` | DWORD | `0`/`1`/`2` | Full-screen exclusive behavior |
| `GameDVR_FSEBehaviorMode` | DWORD | `0`/`1`/`2` | FSE behavior mode |
| `GameDVR_HonorUserFSEBehaviorMode` | DWORD | `0`/`1` | Honor user FSE mode |
| `GameDVR_DXGIHonorFSEWindowsCompatible` | DWORD | `0`/`1` | DXGI FSE Windows compatibility |
| `GameDVR_EFSEFeatureFlags` | DWORD | `0`/`1` | Enhanced FSE feature flags |
| `GameDVR_AllowShadowCapture` | DWORD | `0`/`1` | Allow background shadow capture |
| `GameDVR_AllowGameDVR` | DWORD | `0`/`1` | Allow Game DVR globally |
| `GameDVR_BEFHBehaviorMode` | DWORD | `0`/`1`/`2` | BEFH behavior mode |
| `GameDVR_DSHistoryOnDevice` | DWORD | `0`/`1` | Disable save history on device |
| `GameDVR_CaptureEvenWhenNotFocused` | DWORD | `0`/`1` | Capture when app not focused |

#### GameDVR Advanced Capture Settings
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR
```
| Value Name | Type | Meaning |
|---|---|---|
| `AppCaptureEnabled` | DWORD | Enable/Disable app capture |
| `HistoricalCaptureEnabled` | DWORD | Enable/Disable historical capture |
| `HistoricalCaptureOnBatteryAllowed` | DWORD | Allow recording on battery |
| `HistoricalCaptureOnWirelessDisplayAllowed` | DWORD | Allow on wireless display |
| `AudioCaptureEnabled` | DWORD | Capture system audio |
| `MicrophoneCaptureEnabled` | DWORD | Capture microphone |
| `CursorCaptureEnabled` | DWORD | Capture mouse cursor |
| `CustomVideoBitrate` | DWORD | Custom video bitrate (kbps) |
| `CustomVideoFrameRate` | DWORD | Frame rate (30/60) |
| `VideoEncodingBitrateMode` | DWORD | 1=standard, 2=high |
| `VideoEncodingResolutionMode` | DWORD | Resolution mode |

#### Group Policy Override
```
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR
```
| Value Name | Type | Values | Meaning |
|---|---|---|---|
| `AllowGameDVR` | DWORD | `0`/`1` | Group Policy override to allow/block GameDVR |

### GameBarPresenceWriter
- **Path**: `C:\Windows\System32\GameBarPresenceWriter.exe`
- Runs in background even when Game Bar is disabled
- Disable via Task Scheduler: `Task Scheduler Library > Microsoft > Windows > GameBarPresenceWriter`
- Also check: `Microsoft > GameInput` for related tasks
- Disabling reduces background CPU/RAM usage and prevents conflicts with streaming software

### Batch Disable Commands
```bat
:: Disable Game Bar
reg add "HKCU\Software\Microsoft\GameBar" /v "AllowAutoGameMode" /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\GameBar" /v "AutoGameModeEnabled" /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\GameBar" /v "ShowStartupPanel" /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\GameBar" /v "UseNexusForGameBarEnabled" /t REG_DWORD /d 0 /f

:: Disable Game DVR
reg add "HKCU\System\GameConfigStore" /v "GameDVR_Enabled" /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR" /v "AppCaptureEnabled" /t REG_DWORD /d 0 /f

:: Policy-level block
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR" /v "AllowGameDVR" /t REG_DWORD /d 0 /f
```

### Default Values
- Game Mode: **Enabled** on Windows 10/11 fresh installs
- Game DVR: **Enabled** by default

### Recommended Values
- **Game Mode**: Enable for most users; marginal benefit in CPU-limited scenarios
- **Game DVR**: Disable if not using recording features (saves resources)
- **GameBarPresenceWriter**: Disable via Task Scheduler if not using Game Bar

### Risk Classification
- **LOW** — Fully reversible; disabling Game DVR may lose instant replay functionality

### Windows Version Compatibility
- **Windows 10** 1809+ (Game Mode), 1709+ (Game DVR)
- **Windows 11** all versions

### Evidence Score: 6/10
Game Mode performance impact is minimal in most benchmarks (often <1 FPS difference). Game DVR disabling is well-established for resource savings.

---

## 4. Variable Refresh Rate (VRR)

### Overview
VRR synchronizes the display's refresh rate to the GPU's frame output, eliminating screen tearing without V-Sync input lag. Includes G-Sync (NVIDIA), FreeSync (AMD), and HDMI 2.1 VRR.

### Settings Path
Settings > System > Display > Graphics > Default Graphics Settings > "Variable Refresh Rate"

### Related Registry Keys

#### System-Level VRR
```
HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences
```
| Value Name | Type | Meaning |
|---|---|---|
| `DirectXUserGlobalSettings` | REG_SZ | Contains VRR flags as serialized string |
| `VRROptimizeEnable=1` | String flag | Enables VRR optimization |

#### Per-Display VRR
```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration
```
VRR state is tracked per-display configuration entry.

### GPU Requirements

| Vendor | Minimum GPU | Protocol |
|---|---|---|
| **NVIDIA** | GTX 10-series (Pascal) for G-Sync Compatible; RTX 20+ for full G-Sync | G-Sync, G-Sync Compatible, G-Sync Ultimate |
| **AMD** | RX 400-series (Polaris) and newer | FreeSync, FreeSync Premium, FreeSync Premium Pro |
| **Intel** | Arc A-series / 12th-gen+ iGPU | Adaptive Sync (VRR) |
| **Display** | Must be VRR-capable and connected via DisplayPort or HDMI 2.1 | VESA Adaptive-Sync, HDMI 2.1 VRR |

### When to Enable
- Frame rates fluctuating below monitor's maximum refresh rate
- Open-world games with unpredictable performance
- Most single-player / visually intensive games
- Gaming at variable frame rates between 40-144+ FPS

### When to Disable
- Competitive/esports gaming at frame rates consistently matching or exceeding monitor refresh rate
- When experiencing VRR-related flickering (gamma shifts in dark scenes)
- Low frame rates below VRR range (typically below 40-48 FPS for FreeSync)
- When combined with V-Sync ON causing input lag at top of refresh range

### Best Practice Configuration
1. Enable VRR in Windows Settings and GPU driver control panel
2. Enable V-Sync in NVIDIA/AMD driver (NOT in-game)
3. Set in-game frame rate cap **3 FPS below** monitor max refresh rate (e.g., 141 for 144Hz)
4. This eliminates tearing, minimizes input lag, and stays within VRR range

### Default Value
- **Disabled** by default in Windows (must be manually enabled)

### Recommended Value
- **Enable** for most gaming scenarios
- Disable only for competitive gaming at consistently high FPS or when experiencing VRR-specific issues

### Risk Classification
- **LOW** — Requires VRR-capable display and compatible GPU; no harm in enabling

### Windows Version Compatibility
- **Windows 10** 1903+ (basic VRR), 2004+ (improved VRR)
- **Windows 11** all versions (improved VRR management)

### Evidence Score: 8/10
VRR technology is well-documented and universally endorsed by display industry. Benefits are clear and measurable through input lag testing and tear-free operation.

---

## 5. Per-App GPU Preferences

### Overview
Windows allows per-application selection of which GPU to use (power-saving integrated vs. high-performance discrete), useful for multi-GPU systems (laptops with iGPU + dGPU).

### Settings Path
Settings > System > Display > Graphics > Browse for app > Options

### Registry Path
```
HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences
```

### Global Default
| Value Name | Type | Value | Meaning |
|---|---|---|---|
| `SystemSettings_HardwarePreferenceGPUAllApps` | DWORD | `1` | Power saving (integrated GPU) for all apps |
| `SystemSettings_HardwarePreferenceGPUAllApps` | DWORD | `2` | High performance (discrete GPU) for all apps |
| `SystemSettings_HardwarePreferenceGPUAllApps` | DWORD | `3` | System default / Let Windows decide |

### Per-App Entry
Under the same `UserGpuPreferences` key:
| Value Name | Type | Meaning |
|---|---|---|
| `DirectXUserGlobalSettings` | REG_SZ | Global DirectX GPU preference flags |
| `<full_executable_path>` | REG_SZ | Per-app preference (e.g., `C:\Games\game.exe`) |

#### Per-App Value Data Format
```
GpuPreference=2;
```
| GpuPreference Value | Meaning |
|---|---|
| `GpuPreference=1` | Power saving (integrated GPU) |
| `GpuPreference=2` | High performance (discrete GPU) |
| `GpuPreference=3` | Let Windows decide |
| `GpuPreference=2076` | System default (no override) |

### PowerShell Example
```powershell
# Set high performance for a specific app
$appPath = "C:\Program Files\MyApp\myapp.exe"
Set-ItemProperty -Path "HKCU:\Software\Microsoft\DirectX\UserGpuPreferences" -Name $appPath -Value "GpuPreference=2;" -Type String

# Set global default to high performance
Set-ItemProperty -Path "HKCU:\Software\Microsoft\DirectX\UserGpuPreferences" -Name "SystemSettings_HardwarePreferenceGPUAllApps" -Value 2 -Type DWord
```

### Default Value
- `SystemSettings_HardwarePreferenceGPUAllApps` = `3` (Let Windows decide)
- No per-app entries by default

### Recommended Value
- Set individual demanding apps/games to `GpuPreference=2` (high performance)
- Leave global default at `3` (Let Windows decide)

### Risk Classification
- **LOW** — Fully reversible; incorrect values just fall back to system default

### Windows Version Compatibility
- **Windows 10** 1803+ (basic), 1903+ (improved per-app)
- **Windows 11** all versions (improved UI and management)

### Evidence Score: 7/10
Well-documented Microsoft feature; official Settings UI equivalent. Behavior is predictable and reliable.

---

## 6. Shader Cache

### Overview
Shader caches store pre-compiled GPU shader bytecode to avoid recompilation on every game launch. Managed at both the driver level and application level (Steam, Unreal Engine).

### Cache Locations

#### NVIDIA
| Path | Content |
|---|---|
| `%LOCALAPPDATA%\NVIDIA\DXCache` | DirectX shader cache (primary) |
| `%LOCALAPPDATA%\NVIDIA\GLCache` | OpenGL shader cache |
| Full paths: `C:\Users\<username>\AppData\Local\NVIDIA\DXCache` and `GLCache` | |

#### AMD
| Path | Content |
|---|---|
| `%LOCALAPPDATA%\AMD\DxCache` | DirectX shader cache (primary) |
| `%LOCALAPPDATA%\AMD\CN` | Older driver cache data |
| Full path: `C:\Users\<username>\AppData\Local\AMD\DxCache` | |

#### Intel
| Path | Content |
|---|---|
| `%LOCALAPPDATA%\Intel\ShaderCache` | Shader cache for Arc / integrated |
| `C:\Windows\Temp` | Some shader-related temporary files |

#### Application-Level Caches
| Platform | Location |
|---|---|
| **Steam** | `<steamapps>\shadercache\<app_id>\` (separate precaching system) |
| **Unreal Engine** | Game-specific `ShaderCache` folders |

### How to Clear

#### Method 1: Windows Settings
Settings > System > Storage > Temporary Files > Check "DirectX Shader Cache" > Remove files

#### Method 2: Disk Cleanup Utility
```cmd
cleanmgr /d C:
```
Select "DirectX Shader Cache" checkbox

#### Method 3: Manual Deletion
```powershell
# NVIDIA
Remove-Item "$env:LOCALAPPDATA\NVIDIA\DXCache\*" -Recurse -Force
Remove-Item "$env:LOCALAPPDATA\NVIDIA\GLCache\*" -Recurse -Force

# AMD
Remove-Item "$env:LOCALAPPDATA\AMD\DxCache\*" -Recurse -Force

# Intel
Remove-Item "$env:LOCALAPPDATA\Intel\ShaderCache\*" -Recurse -Force
```

#### Method 4: Vendor Software
- **AMD Adrenalin**: Settings > Graphics > "Reset Shader Cache"
- **Intel Graphics Command Center**: System > Troubleshooting > "Delete Shader Cache"
- **NVIDIA**: No dedicated UI button; use manual deletion or Disk Cleanup

### Performance Impact

| Scenario | Effect |
|---|---|
| **After clearing cache** | **Negative short-term** — First encounter with new shaders causes compilation stutter (brief freezes, frame drops) |
| **After recompilation** | **Neutral to positive** — Returns to normal; improves if cache was corrupted |
| **Cache corruption** | Visual artifacts, crashes, rendering bugs |
| **Cache size** | Can grow to several GB; clearing reclaims disk space |

### When to Clear
- After GPU driver update (recommended)
- Corrupted textures, visual glitches, or rendering crashes
- Cache size excessively large (>5GB)
- NOT during active gaming sessions

### Default Value
- Cache auto-populated and managed by drivers

### Recommended Value
- **Leave alone** unless troubleshooting issues
- Clear only after driver updates or when encountering shader-related problems

### Risk Classification
- **MEDIUM** — Clearing causes temporary performance degradation (stuttering) during recompilation. Recovery is automatic but may take several gaming sessions.

### Windows Version Compatibility
- **Windows 10** all versions
- **Windows 11** all versions
- Driver-level feature independent of OS version

### Evidence Score: 7/10
Well-understood mechanism; shader compilation stutter is documented in DX12/Vulkan game development literature.

---

## 7. MPO (Multi-Plane Overlay) Issues

### Overview
MPO allows the GPU hardware to composite multiple display layers (desktop, video, game windows) independently, reducing CPU/GPU overhead. However, buggy implementations cause stuttering, flickering, and frame pacing issues.

### Primary Registry Fixes

#### Disable MPO via OverlayTestMode
```
HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\Dwm
```
| Value Name | Type | Value | Meaning |
|---|---|---|---|
| `OverlayTestMode` | DWORD | `0` | Default (MPO enabled) |
| `OverlayTestMode` | DWORD | `5` | Bypass hardware overlay planes (disables MPO) |

#### Disable Hardware Planes
```
HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\Dwm
```
| Value Name | Type | Value | Meaning |
|---|---|---|---|
| `EnableHWPlanes` | DWORD | `0` | Disable hardware overlay planes |
| `EnableHWPlanes` | DWORD | `1` | Enable (default) |

#### Disable Overlay Planes via Graphics Drivers
```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers
```
| Value Name | Type | Value | Meaning |
|---|---|---|---|
| `DisableOverlayPlanes` | DWORD | `0` | Default (overlays enabled) |
| `DisableOverlayPlanes` | DWORD | `1` | Force single-plane compositing |

### PowerShell Commands
```powershell
# Disable MPO
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\Dwm" -Name "OverlayTestMode" -Value 5 -Type DWord
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\Dwm" -Name "EnableHWPlanes" -Value 0 -Type DWord

# Additional: Disable overlay planes at driver level
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" -Name "DisableOverlayPlanes" -Value 1 -Type DWord
```

### To Re-enable MPO
```powershell
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\Dwm" -Name "OverlayTestMode" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\Dwm" -Name "EnableHWPlanes" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" -Name "DisableOverlayPlanes" -ErrorAction SilentlyContinue
```

### Known MPO Issues
- **Frame pacing / stuttering** in games and browsers (Chrome, Edge)
- **Micro-stuttering** during windowed/borderless gaming
- **Gamma flickering** in dark scenes with VRR enabled
- **Black screens** or flickering during Alt+Tab in some configurations
- Most commonly reported with **NVIDIA GPUs** but also affects AMD
- Windows 11 22H2/23H2/24H2 have incremental fixes but issues persist for some users

### Default Value
- MPO **enabled** by default on all Windows 10/11 installations

### Recommended Value
- **Enable (default)** unless experiencing stuttering/flickering
- **Disable** (`OverlayTestMode=5`) if experiencing unexplained micro-stutters, especially with NVIDIA GPUs

### Risk Classification
- **LOW** — Fully reversible on reboot; disables a hardware optimization but generally no visual regression noticeable to most users

### Windows Version Compatibility
- **Windows 10** 1809+ (MPO support introduced)
- **Windows 11** all versions (ongoing refinements)

### Evidence Score: 6/10
Extensively discussed in community forums (Reddit r/nvidia, r/Windows11, Microsoft Community). Less formally documented by Microsoft. Workaround is widely validated but root cause varies by hardware.

---

## 8. GPU Power Management

### Overview
GPU power management controls how aggressively the GPU clocks and voltages are managed. Maximum performance modes prevent downclocking at the cost of higher power consumption and heat.

### NVIDIA Power Management

#### NVIDIA Control Panel
- 3D Settings > Power management mode
- Options: "Optimal power" (default), "Adaptive", "Prefer maximum performance"

#### Registry — NVIDIA PowerMizer
```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000
```
| Value Name | Type | Value | Meaning |
|---|---|---|---|
| `PerfLevelSrc` | DWORD | `0x2222` | Force maximum performance state |
| `PowerMizerEnable` | DWORD | `1` | Enable PowerMizer |
| `PowerMizerLevel` | DWORD | `1` = Optimal power, `2` = Maximum performance | Performance preference |
| `PowerMizerLevelAC` | DWORD | `1`/`2` | Performance preference when on AC power |
| `PowerMizerLevelDC` | DWORD | `1`/`2` | Performance preference when on DC (battery) |

Note: `0000` is the first GPU; multi-GPU systems have `0001`, `0002`, etc.

### AMD Power Management

#### Registry — AMD PowerTune / DPM
```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000
```
| Value Name | Type | Value | Meaning |
|---|---|---|---|
| `PP_PowerPerformanceMode` | DWORD | `0` | Balanced (default) |
| `PP_PowerPerformanceMode` | DWORD | `1` | Performance mode |
| `PP_SCLKDeepSleepDisable` | DWORD | `1` | Disable GPU shader clock deep sleep |
| `PP_AllGraphicLevelDisable` | DWORD | `1` | Disable dynamic clock switching |

#### AMD UMD Settings
```
HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000\UMD
```
| Value Name | Type | Value | Meaning |
|---|---|---|---|
| `PP_PowerPerformanceMode` | DWORD | `0`/`1` | Power/performance mode |

#### AMD Adrenalin Software
- Performance > Tuning > Power Tuning > Power Limit slider (up to +50%)
- GPU Clock Override for manual frequency control

### Windows Power Plan Interaction

#### Ultimate Performance Power Plan
```cmd
:: Enable Ultimate Performance plan
powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61
```
Registry location:
```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\e9a42b02-d5df-448d-aa00-03f14749eb61
```

#### List Power Plans
```cmd
powercfg -list
```

#### Active Plan GPU Interaction
- **High Performance** / **Ultimate Performance**: GPU stays at higher clocks; faster response
- **Balanced**: GPU may downclock aggressively during idle/light loads; slight wake-up latency
- **Power Saver**: Significant GPU underclocking; not recommended for gaming

#### PCI Express Link State Power Management
Under Power Plan > PCI Express > Link State Power Management:
- **Off** (recommended for gaming): No power saving on PCIe links
- **Moderate power savings**: May introduce slight GPU communication latency

### Default Values
- Windows Power Plan: **Balanced**
- NVIDIA: **Optimal power**
- AMD: **Balanced** (PP_PowerPerformanceMode = 0)

### Recommended Values
- **NVIDIA**: "Prefer maximum performance" for competitive gaming; "Optimal power" for general use
- **AMD**: Use Adrenalin tuning for balanced performance/power
- **Windows**: "High Performance" or "Ultimate Performance" for gaming
- **PCI Express**: Link State Power Management = **Off**

### Risk Classification
- **LOW** (power plan changes) — Fully reversible
- **MEDIUM** (GPU registry PowerMizer/PowerTune) — Incorrect values could cause display issues; GPU driver reinstall recovers

### Windows Version Compatibility
- **Windows 10** all versions
- **Windows 11** all versions
- Ultimate Performance plan: Windows 10 1803+, Windows 11 all versions

### Evidence Score: 7/10
NVIDIA/AMD official documentation covers these settings. Power plan interaction is well-documented by Microsoft. GPU registry tweaks are community-validated.

---

## 9. Visual Effects Settings

### Overview
Windows visual effects (animations, shadows, transparency, font smoothing) consume CPU/GPU resources. Disabling them can free resources for gaming but degrades UI aesthetics.

### GUI Path
System Properties > Advanced > Performance Settings
- Or: `SystemPropertiesPerformance.exe`
- Or: Run `sysdm.cpl` > Advanced tab > Performance Settings

### Master Registry Key
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects
```
| Value Name | Type | Values | Meaning |
|---|---|---|---|
| `VisualFXSetting` | DWORD | `0` | Let Windows choose what's best |
| `VisualFXSetting` | DWORD | `1` | Adjust for best appearance (all ON) |
| `VisualFXSetting` | DWORD | `2` | Adjust for best performance (all OFF) |
| `VisualFXSetting` | DWORD | `3` | Custom (user-selected individual effects) |

### UserPreferencesMask (Bitmask Control)
```
HKEY_CURRENT_USER\Control Panel\Desktop
```
| Value Name | Type | Size | Meaning |
|---|---|---|---|
| `UserPreferencesMask` | BINARY | 8 bytes (64-bit) | Bitmask encoding all visual effect states |

#### Byte 0 Bit Mapping (primary effects)
| Bit | Effect | 1=On, 0=Off |
|---|---|---|
| Bit 1 | Smooth edges of screen fonts (ClearType) | Critical to keep ON for readability |
| Bit 2 | Show shadows under menus | Cosmetic |
| Bit 3 | Show shadows under mouse pointer | Cosmetic |
| Bit 4 | Show shadows under windows | Cosmetic |
| Bit 5 | Animate windows when minimizing/maximizing | Animation |
| Bit 6 | Windows animations (general) | Animation |
| Bit 7 | Fade or slide menus into view | Animation |

#### Byte 1 and Beyond
| Bit | Effect |
|---|---|
| Byte 0, Bit 8 | Fade or slide tooltips into view |
| Byte 0, Bit 9 | Fade out menu items after clicking |
| Byte 0, Bit 10 | Show window contents while dragging |
| Byte 0, Bit 11 | Show translucent selection rectangle |
| Byte 0, Bit 14 | Slide open Combo Boxes |
| Byte 0, Bit 15 | Fade or slide menus into view (extended) |

#### Preset Values (8-byte hex)
| Preset | UserPreferencesMask (Hex) |
|---|---|
| Let Windows choose | `9E 12 01 80 12 00 00 00` |
| Adjust for best performance | `00 00 00 00 00 00 00 00` |
| Adjust for best appearance | `9E 12 01 80 12 01 00 00` |
| Common custom (keep ClearType) | `90 12 01 80 21 00 00 00` |

### Individual Visual Effects Registry Keys
Each effect has its own subkey under:
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\
```
| Subkey | Description | DefaultValue (DWORD) |
|---|---|---|
| `AnimateMinMax` | Animate minimize/maximize | `1` = ON |
| `Animations` | General window animations | `1` = ON |
| `ComboBoxAnimation` | ComboBox fade/slide | `1` = ON |
| `ControlPanelAnimation` | Control Panel slide animation | `1` = ON |
| `CursorShadow` | Cursor shadow effect | `1` = ON |
| `DragFullWindows` | Show contents while dragging | `1` = ON |
| `DropDownAnimation` | Dropdown fade/slide | `1` = ON |
| `FontSmoothing` | ClearType font smoothing | `1` = ON |
| `ListviewAlpha` | Alpha-blended icons in lists | `1` = ON |
| `ListviewShadow` | Icon shadow in lists | `1` = ON |
| `MenuAnimation` | Menu fade/slide animation | `1` = ON |
| `MenuFade` | Menu fade effect | `1` = ON |
| `SelectionFade` | Selection fade animation | `1` = ON |
| `ShowShadow` | Desktop icon shadows | `1` = ON |
| `ShowSpotlight` | Spotlight effects | `1` = ON |
| `ShowThumbnails` | Thumbnail previews | `1` = ON |
| `SmoothEdges` | Smooth screen font edges | `1` = ON |
| `TooltipAnimation` | Tooltip fade/slide animation | `1` = ON |

### VisualFXPolicy (Logon State Tracking)
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\VisualFXPolicy
```
| Value Name | Type | Meaning |
|---|---|---|
| `SetLogonDeregistrationSuccess` | DWORD | Whether "best appearance" was applied at logon |
| `DefaultApplied` | DWORD | Whether defaults have been applied |

### PowerShell Commands
```powershell
# Set "Adjust for best performance" (disable all effects)
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" -Name "VisualFXSetting" -Value 2 -Type DWord

# Set "Adjust for best appearance" (enable all effects)
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" -Name "VisualFXSetting" -Value 1 -Type DWord

# Set "Let Windows choose"
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" -Name "VisualFXSetting" -Value 0 -Type DWord

# Disable all effects via UserPreferencesMask (nuclear option)
$bytes = [byte[]]@(0,0,0,0,0,0,0,0)
Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "UserPreferencesMask" -Value $bytes -Type Binary

# Keep ClearType but disable everything else (recommended for gaming)
$bytes = [byte[]]@(0x90,0x12,0x01,0x80,0x21,0x00,0x00,0x00)
Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "UserPreferencesMask" -Value $bytes -Type Binary
```

### Recommended Gaming Configuration
- Set `VisualFXSetting` = `2` (best performance) OR `3` (custom)
- If custom: Keep `FontSmoothing` and `DragFullWindows` ON; disable all animations
- Use `UserPreferencesMask` = `90 12 01 80 21 00 00 00` as a balanced starting point

### Performance Impact
- **Negligible** on modern high-end systems (GPU handles these easily)
- **Measurable** on older hardware, integrated graphics, or heavily CPU-limited systems
- Disabling transparency alone (Mica/Acrylic) can save noticeable GPU resources on older iGPU systems

### Default Value
- `VisualFXSetting` = `0` (Let Windows choose)

### Risk Classification
- **NONE** — Purely cosmetic; fully reversible with zero functional impact

### Windows Version Compatibility
- **Windows 10** all versions
- **Windows 11** all versions (additional effects like Mica/Acrylic in 11)
- Note: Bit assignments in UserPreferencesMask may vary slightly between major Windows versions

### Evidence Score: 9/10
Officially documented Microsoft feature with GUI controls. Well-understood and universally testable. The underlying API (SystemParametersInfo with SPI_SETUIEFFECTS) is documented in Windows SDK.

---

## Summary Matrix

| Setting | Default | Recommended (Gaming) | Risk | Impact | Evidence |
|---|---|---|---|---|---|
| HAGS | Enabled (Win11) / Disabled (Win10 upgrade) | Enable on RTX 20+ / RDNA 2+ | LOW | Low-Medium | 7/10 |
| Fullscreen Optimizations | Enabled (hybrid) | Disable if stuttering | LOW | Medium | 8/10 |
| Game Mode | Enabled | Enable | LOW | Low | 6/10 |
| Game DVR | Enabled | Disable if not recording | LOW | Medium (resource savings) | 6/10 |
| Variable Refresh Rate | Disabled | Enable with V-Sync + frame cap | LOW | High | 8/10 |
| Per-App GPU | System default | High perf for games | LOW | High (laptops) | 7/10 |
| Shader Cache | Auto-managed | Clear only after driver updates | MEDIUM | Low (after recompilation) | 7/10 |
| MPO | Enabled | Disable if stuttering | LOW | Low-Medium | 6/10 |
| GPU Power Management | Balanced | Max performance for gaming | LOW-MED | Medium | 7/10 |
| Visual Effects | Let Windows choose | Best performance or custom | NONE | Low (older HW) | 9/10 |

---

## Key Commands Quick Reference

```powershell
# === HAGS ===
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" "HwSchMode" 2

# === Fullscreen Optimizations (disable) ===
Set-ItemProperty "HKCU:\System\GameConfigStore" "GameDVR_FSEBehaviorMode" 2
Set-ItemProperty "HKCU:\System\GameConfigStore" "GameDVR_FSEBehavior" 2
Set-ItemProperty "HKCU:\System\GameConfigStore" "GameDVR_HonorUserFSEBehaviorMode" 1

# === Game DVR (disable) ===
Set-ItemProperty "HKCU:\System\GameConfigStore" "GameDVR_Enabled" 0
Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR" "AppCaptureEnabled" 0

# === GameDVR Policy Block ===
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR" /v "AllowGameDVR" /t REG_DWORD /d 0 /f

# === Ultimate Performance Power Plan ===
powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61

# === NVIDIA Max Performance ===
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000" "PerfLevelSrc" 0x2222
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000" "PowerMizerLevel" 2

# === MPO Disable ===
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\Dwm" "OverlayTestMode" 5
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\Dwm" "EnableHWPlanes" 0

# === Visual Effects (Best Performance) ===
Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" "VisualFXSetting" 2

# === Clear NVIDIA Shader Cache ===
Remove-Item "$env:LOCALAPPDATA\NVIDIA\DXCache\*" -Recurse -Force
```
