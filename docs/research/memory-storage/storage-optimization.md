# Storage Optimization - Windows Performance Tuning

> **Category:** Memory & Storage
> **Risk Level:** LOW to MEDIUM (varies by section)
> **Last Updated:** 2026-08-31
> **Applies To:** Windows 10 1709+, Windows 11

---

## Table of Contents

1. [TRIM (Delete Notifications)](#1-trim-delete-notifications)
2. [Write Caching Policies](#2-write-caching-policies)
3. [NTFS Settings](#3-ntfs-settings)
4. [Storage Sense](#4-storage-sense)
5. [Defragmentation / Optimize Drives](#5-defragmentation--optimize-drives)
6. [NVMe Power States](#6-nvme-power-states)
7. [Disk Health Monitoring](#7-disk-health-monitoring)
8. [Storage Controllers](#8-storage-controllers)
9. [HDD-Specific Settings](#9-hdd-specific-settings)

---

## 1. TRIM (Delete Notifications)

TRIM allows the operating system to inform an SSD which data blocks are no longer in use and can be wiped internally. This maintains SSD write performance over time and reduces write amplification.

**Risk:** LOW | **Evidence:** 5/5 | **Restart Required:** No

### 1.1 Query TRIM Status

```cmd
fsutil behavior query DisableDeleteNotify
```

**Output interpretation:**
```
DisableDeleteNotify = 0  (TRIM enabled - NTFS default)
DisableDeleteNotify = 1  (TRIM disabled)
```

| Value | Meaning |
|-------|---------|
| `0` | TRIM **enabled** (OS sends delete notifications to SSD) |
| `1` | TRIM **disabled** (OS does not send delete notifications) |

### 1.2 Enable/Disable TRIM

```cmd
:: Enable TRIM (recommended for all SSDs)
fsutil behavior set DisableDeleteNotify 0

:: Disable TRIM (not recommended)
fsutil behavior set DisableDeleteNotify 1
```

### 1.3 Filesystem Defaults

| Filesystem | Default Value | TRIM Status |
|------------|---------------|-------------|
| NTFS | `0` | Enabled |
| ReFS v2 | `1` | Disabled |
| FAT32/exFAT | N/A | Not supported |

### 1.4 Verify TRIM Execution

```powershell
# Check that TRIM is actually running (requires admin)
Get-StorageReliabilityCounter -PhysicalDisk (Get-PhysicalDisk -FriendlyName "NVMe*") | Select-Object Trimmètre

# Alternative: monitor TRIM via Performance Monitor
# Counter: \FileSystem\Delete Notifications/sec
```

### 1.5 Manual TRIM (Force)

```cmd
:: Force TRIM on entire volume (offline optimization)
defrag C: /L

:: PowerShell equivalent
Optimize-Volume -DriveLetter C -ReTrim
```

### 1.6 Recommendations

| Scenario | Setting | Notes |
|----------|---------|-------|
| SSD (any) | `0` (enabled) | Always enable on SSDs |
| HDD | `1` (disabled) | Harmless but pointless on HDD |
| Mixed system | `0` (enabled) | Windows only sends TRIM to SSDs anyway |
| ReFS volume on SSD | Verify `0` | ReFS default differs from NTFS |

---

## 2. Write Caching Policies

Write caching allows the OS to buffer write operations in volatile RAM before committing them to disk. This improves write throughput but introduces a small window for data loss during unexpected power loss.

**Risk:** MEDIUM | **Evidence:** 4/5 | **Restart Required:** Yes (for Device Manager change)

### 2.1 Device Manager Method

1. Open **Device Manager** (`devmgmt.msc`)
2. Expand **Disk drives**
3. Right-click target disk, select **Properties**
4. Go to **Policies** tab
5. Select removal policy:

| Policy | Behavior | Risk |
|--------|----------|------|
| **Quick removal** (default) | Disables write caching; safe to unplug anytime | Low |
| **Better performance** | Enables write caching; requires safe removal | Medium |

When "Better performance" is selected, an additional option appears:
- **Enable write caching on the device** -- default ON
- **Turn off Windows write-cache buffer flushing on the device** -- default OFF

### 2.2 Registry Method

**Registry path:**
```
HKLM\SYSTEM\CurrentControlSet\Enum\<DeviceID>\Device Parameters\Disk
```

**Finding the Device ID:**
```powershell
# List all disk device instance IDs
Get-PnpDevice -Class DiskDrive | Select-Object InstanceId, FriendlyName
```

**Registry values:**

| Value Name | Type | Data | Meaning |
|------------|------|------|---------|
| `UserWriteCacheSetting` | REG_DWORD | `0` or `1` | `0` = Quick removal, `1` = Better performance |
| `CacheIsPowerProtected` | REG_DWORD | `0` or `1` | `0` = Flush on idle, `1` = Cache survives power loss (enterprise SSDs) |
| `WriteCacheEnable` | REG_DWORD | `0` or `1` | `0` = Cache disabled, `1` = Cache enabled |

**Example command to enable write cache:**
```powershell
# Set "Better performance" for a specific device
$devicePath = "HKLM:\SYSTEM\CurrentControlSet\Enum\PCI\VEN_1028&DEV_0002\5&12345678&0&000000\Device Parameters\Disk"
Set-ItemProperty -Path $devicePath -Name "UserWriteCacheSetting" -Value 1 -Type DWord

# If the SSD firmware supports power-loss protection:
Set-ItemProperty -Path $devicePath -Name "CacheIsPowerProtected" -Value 1 -Type DWord
```

### 2.3 Recommendations Per Scenario

| Scenario | Quick Removal | Write Cache | Power Flush Off | CacheIsPowerProtected |
|----------|:------------:|:-----------:|:---------------:|:---------------------:|
| **Desktop with UPS** | No | ON | ON | 1 |
| **Desktop without UPS** | No | ON | OFF (default) | 0 |
| **Laptop (battery)** | Yes (default) | OFF | OFF | 0 |
| **Server (enterprise SSD)** | No | ON | ON | 1 |
| **Server (consumer SSD)** | No | ON | OFF | 0 |
| **External USB drive** | Yes | OFF | OFF | 0 |
| **Database server** | No | ON | ON | 1 |

### 2.4 Verify Current Write Cache Status

```powershell
# Quick check for all disks
Get-Disk | Select-Object Number, FriendlyName, IsWriteCacheEnabled, IsPowerProtected

# Detailed view via WMI
Get-WmiObject Win32_DiskDrive | Select-Object DeviceID, Model, 
    @{N='WriteCache';E={$_.WriteCachePolicy}},
    @{N='PowerProtected';E={$_.PowerManagementSupported}}
```

### 2.5 ForceUnitAccess (FUA)

The `ForceUnitAccess` registry value forces writes to bypass the drive cache and go directly to persistent storage. This is used by databases and other applications requiring strict durability.

```
ForceUnitAccess = 1  (every write is FUA - significant performance penalty)
ForceUnitAccess = 0  (default - application can choose FUA per write)
```

**Do not set globally.** Only set for specific applications that need it (e.g., SQL Server log files configured with `DISK = NONE`).

---

## 3. NTFS Settings

NTFS has several tunable parameters that affect performance, disk space usage, and I/O behavior. The defaults are well-chosen for general use, but specific workloads benefit from adjustments.

**Risk:** LOW | **Evidence:** 5/5 | **Restart Required:** No (most), Yes (some registry changes)

### 3.1 8.3 Filename Creation

Windows NTFS supports both long filenames (LFN) and legacy 8.3 short filenames (e.g., `PROGRA~1`). Short names consume an MFT record entry on every directory and can slow directory enumeration on volumes with many files.

**Query current setting:**
```cmd
fsutil 8dot3name query C:
```

**Disable 8.3 name creation (recommended for new files):**
```cmd
:: Set for C: drive (0=enable, 1=disable)
fsutil 8dot3name set C: 1
```

**Registry method:**
```
HKLM\SYSTEM\CurrentControlSet\Control\FileSystem
NtfsDisable8dot3NameCreation (REG_DWORD)
```

| Value | Behavior |
|-------|----------|
| `0` | 8.3 names created for all volumes (legacy default) |
| `1` | 8.3 names not created for new files (recommended) |
| `2` | Per-volume setting via `fsutil 8dot3name set` |

**Note:** Disabling 8.3 names does NOT remove existing short names. To remove them:
```cmd
:: First enumerate existing short names
fsutil 8dot3name scan C:

:: Remove all existing short names (use /s for subdirectories)
for /f "tokens=*" %i in ('fsutil 8dot3name scan C: ^| findstr /i "8.3"') do @fsutil file setfilenameinfo %i
```

**Impact assessment:**

| Workload | 8.3 Names | Recommendation |
|----------|-----------|----------------|
| General desktop | Disable (1) | No modern app needs 8.3 names |
| Legacy applications | Enable (0) | Some old installers reference short names |
| Server with millions of files | Disable (1) | Measurable MFT and directory scan improvement |
| Database server | Disable (1) | All entries in MFT improve cluster allocation |

### 3.2 Last Access Time Updates

By default, NTFS updates the last-access timestamp on every file read. This generates unnecessary write I/O. Windows 8+ defaults to disabling this at the registry level.

**Query current setting:**
```cmd
fsutil behavior query disablelastaccess
```

**Disable last access time updates:**
```cmd
fsutil behavior set disablelastaccess 1
```

**Registry method:**
```
HKLM\SYSTEM\CurrentControlSet\Control\FileSystem
NtfsDisableLastAccessUpdate (REG_DWORD)
```

| Value | Behavior |
|-------|----------|
| `0x00000000` | Last access time updated on every read (legacy behavior) |
| `0x00000001` | Last access time disabled for directories |
| `0x00000002` | Last access time disabled for files and directories |
| `0x80000000` | Default (Windows 8+). Bit 31 set = system default; current OS default is `0x80000001` (dirs only) |

**Recommended:**
```cmd
:: Disable for both files and directories (best performance)
fsutil behavior set disablelastaccess 2
```

### 3.3 NTFS Memory Usage

Controls the amount of system memory NTFS uses for its paged pool cache. This affects file system caching performance.

**Query:**
```cmd
fsutil behavior query memoryusage
```

**Set:**
```cmd
:: Default value (1) - 128MB paged pool for NTFS
fsutil behavior set memoryusage 1

:: Increased value (2) - 256MB paged pool for NTFS
fsutil behavior set memoryusage 2
```

| Value | Paged Pool Allocation | Recommended For |
|-------|-----------------------|-----------------|
| `1` | ~128 MB (default) | Systems with <16 GB RAM |
| `2` | ~256 MB (doubled) | Systems with 16+ GB RAM, heavy file I/O |

**Warning:** Setting to `2` on systems with low RAM (<8 GB) may cause pool exhaustion and system instability.

### 3.4 MFT Zone

The MFT (Master File Table) zone is reserved space on the volume that NTFS reserves for MFT growth. When the MFT zone fills, NTFS fragments the MFT, which can degrade performance.

**Query:**
```cmd
fsutil behavior query mftzone
```

**Set:**
```cmd
:: 1 = ~200 MB reserved (default)
fsutil behavior set mftzone 1

:: 2 = ~400 MB reserved
fsutil behavior set mftzone 2

:: 3 = ~600 MB reserved
fsutil behavior set mftzone 3

:: 4 = ~800 MB reserved (maximum)
fsutil behavior set mftzone 4
```

| Value | Reserved Space | Recommended For |
|-------|---------------|-----------------|
| `1` | ~200 MB | General desktop use |
| `2` | ~400 MB | Volumes with 500K+ files |
| `3` | ~600 MB | Servers with millions of files |
| `4` | ~800 MB | Large file servers, 1M+ files |

**Note:** Takes effect on next format or new volume creation. Does not retroactively resize the zone on existing volumes.

### 3.5 Comprehensive NTFS Settings Script

```powershell
# NTFS Optimization Script (Run as Administrator)
# Applies recommended NTFS settings for a desktop/server with adequate RAM

$volume = "C:"

Write-Host "=== NTFS Optimization for $volume ===" -ForegroundColor Cyan

# Disable 8.3 filename creation
Write-Host "`n[1/4] Setting 8.3 filename creation..." -ForegroundColor Yellow
fsutil 8dot3name set $volume.Replace(":", "") 1

# Disable last access time updates
Write-Host "[2/4] Disabling last access time updates..." -ForegroundColor Yellow
fsutil behavior set disablelastaccess 2

# Increase NTFS memory usage (only if RAM >= 16 GB)
$ramGB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
if ($ramGB -ge 16) {
    Write-Host "[3/4] Setting NTFS memory usage to 2 (increased, $ramGB GB RAM detected)..." -ForegroundColor Yellow
    fsutil behavior set memoryusage 2
} else {
    Write-Host "[3/4] Skipping memory usage increase ($ramGB GB RAM < 16 GB threshold)..." -ForegroundColor Yellow
}

# Set MFT zone
$volumeSize = (Get-Volume -DriveLetter $volume.Replace(":", "")).Size / 1GB
if ($volumeSize -gt 500) {
    Write-Host "[4/4] Setting MFT zone to 2 (large volume: $([math]::Round($volumeSize)) GB)..." -ForegroundColor Yellow
    fsutil behavior set mftzone 2
} else {
    Write-Host "[4/4] Keeping MFT zone at 1 (volume: $([math]::Round($volumeSize)) GB)..." -ForegroundColor Yellow
}

Write-Host "`n=== Complete. Some changes take effect on next format. ===" -ForegroundColor Green
```

---

## 4. Storage Sense

Storage Sense is an automated disk cleanup feature introduced in Windows 10 1709. It can free up space by deleting temporary files, emptying the recycle bin, and removing files from the Downloads folder after a configurable period.

**Risk:** LOW | **Evidence:** 4/5 | **Restart Required:** No

### 4.1 Enable via Registry

```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy
```

**Registry values (bitmask):**

| Value Name | Bit(s) | Meaning |
|------------|--------|---------|
| `01` | Bit 0 | Storage Sense **enabled** (`1`) or disabled (`0`) |
| `02` | Bit 1 | Run frequency: `0` = every month, `1` = every 3 months, `2` = every 6 months, `3` = during low free space |
| `04` | Bit 2 | Delete temporary files that apps aren't using (`1` = enabled) |
| `08` | Bit 3 | Delete files in Downloads after N days (`1` = enabled) |
| `256` | Bits 8+ | Number of days to keep Downloads files (default: 30) |
| `512` | Bit 9 | Delete files in Recycle Bin after N days (`1` = enabled) |
| `0C00` | Bits 10-11 | Recycle Bin retention: `0` = 1 day, `1` = 14 days, `2` = 30 days, `3` = 60 days |

**Example: Enable Storage Sense with monthly runs, temp cleanup, 30-day Downloads cleanup, 30-day Recycle Bin cleanup:**
```powershell
$regPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy"

# Ensure registry path exists
if (!(Test-Path $regPath)) { New-Item -Path $regPath -Force }

# Compose value: SS enabled (01) + monthly (00) + temp cleanup (04) + downloads 30d (08 + 0100) + recyclebin 30d (200)
Set-ItemProperty -Path $regPath -Name "StoragePolicy" -Value 0x30D -Type DWord
```

**Common configurations:**

| Scenario | StoragePolicy Value | Description |
|----------|-------------------|-------------|
| Minimal | `1` | Storage Sense on, defaults only |
| Balanced | `0x30D` | Monthly, temp + downloads (30d) + recycle bin (30d) |
| Aggressive | `0xF0D` | Monthly, all cleanup options enabled |
| Disabled | `0` | All Storage Sense off |

### 4.2 Enable via Group Policy

```
Computer Configuration > Administrative Templates > System > Storage Sense
```

- **Allow Storage Sense** = Enabled
- **Configure Storage Sense cadence** = Monthly / Quarterly / During low free space

### 4.3 PowerShell: Query Storage Sense Status

```powershell
$regPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy"
$policy = Get-ItemProperty -Path $regPath -Name "StoragePolicy" -ErrorAction SilentlyContinue

if ($null -eq $policy) {
    Write-Host "Storage Sense: Not configured (default OFF)"
} else {
    $val = $policy.StoragePolicy
    $enabled = if ($val -band 1) { "Enabled" } else { "Disabled" }
    $frequency = switch (($val -shr 1) -band 3) {
        0 { "Every month" }
        1 { "Every 3 months" }
        2 { "Every 6 months" }
        3 { "During low free space" }
    }
    $tempClean = if ($val -band 4) { "Yes" } else { "No" }
    $downloadsClean = if ($val -band 8) { "Yes" } else { "No" }
    $recycleBinClean = if ($val -band 0x200) { "Yes" } else { "No" }
    
    Write-Host "Storage Sense: $enabled"
    Write-Host "  Frequency: $frequency"
    Write-Host "  Temp files: $tempClean"
    Write-Host "  Downloads cleanup: $downloadsClean"
    Write-Host "  Recycle Bin cleanup: $recycleBinClean"
}
```

---

## 5. Defragmentation / Optimize Drives

Windows includes built-in tools for defragmenting HDDs and issuing TRIM commands to SSDs. The "Optimize Drives" service (formerly "Disk Defragmenter") correctly identifies drive type and applies appropriate optimization.

**Risk:** LOW | **Evidence:** 5/5 | **Restart Required:** No

### 5.1 Command-Line: defrag.exe

```cmd
:: Analyze drive fragmentation (read-only, no changes)
defrag C: /A

:: Optimize drive (defrag HDD or TRIM SSD)
defrag C: /O

:: Optimize with progress output
defrag C: /O /U

:: Full verbose output with layout map
defrag C: /O /U /V

:: Retrim only (force TRIM on SSD)
defrag C: /L

:: Offline optimization (runs at next idle, reboot may be needed)
defrag C: /O /L
```

**Command flags reference:**

| Flag | Action | Use Case |
|------|--------|----------|
| `/A` | Analyze only | Check fragmentation level |
| `/O` | Optimize | Defrag HDD or TRIM SSD (recommended) |
| `/U` | Show progress | Verbose progress during operation |
| `/V` | Verbose output | Detailed cluster-level output |
| `/L` | Retrim | Force TRIM on SSD |
| `/X` | Free space consolidation | Merge free space on HDD |
| `/B` | Boot optimization | Optimize boot files (offline) |
| `/M` | Multi-threaded | Parallel optimization (multiple volumes) |

### 5.2 PowerShell: Optimize-Volume Cmdlet

```powershell
# Analyze volume
Optimize-Volume -DriveLetter C -Analyze

# Defragment HDD or TRIM SSD
Optimize-Volume -DriveLetter C -Defrag

# Force TRIM
Optimize-Volume -DriveLetter C -ReTrim

# Optimize with full verbose output
Optimize-Volume -DriveLetter C -Defrag -Verbose

# Optimize all volumes
Get-Volume | Where-Object {$_.DriveType -eq 'Fixed' -and $_.DriveLetter} | 
    ForEach-Object { Optimize-Volume -DriveLetter $_.DriveLetter -Defrag }
```

### 5.3 Windows Drive Optimization Behavior

| Drive Type | Technology | Action During Optimization |
|------------|-----------|---------------------------|
| HDD (NTFS) | Mechanical | Defragmentation + free space consolidation |
| SSD (NTFS) | NAND Flash | TRIM (ReTTrim) only |
| SSD (ReFS) | NAND Flash | TRIM only |
| NVMe (any) | NAND Flash | TRIM only |
| Virtual disk (VHDX) | Pass-through | Depends on underlying physical disk |
| USB/Removable | Any | Analyzed but not optimized by default |

### 5.4 Scheduled Optimization Task

**Task Scheduler path:**
```
\Microsoft\Windows\Defrag\ScheduledDefrag
```

**Query current schedule:**
```powershell
Get-ScheduledTask -TaskPath "\Microsoft\Windows\Defrag\" | 
    Select-Object TaskName, State, 
    @{N='Triggers';E={($_.Triggers | ForEach-Object { $_.CimClass.CimClassName }) -join ', '}}
```

**Default schedule:** Weekly, triggered by the Task Scheduler on idle.

**Modify schedule via GUI:**
1. Open **Defragment and Optimize Drives** (`dfrgui.exe`)
2. Click **Change settings**
3. Set frequency: Daily / Weekly / Monthly

**Modify schedule via PowerShell:**
```powershell
# Disable scheduled defrag
Disable-ScheduledTask -TaskPath "\Microsoft\Windows\Defrag\" -TaskName "ScheduledDefrag"

# Enable with custom trigger (e.g., daily at 2 AM)
$action = New-ScheduledTaskAction -Execute "defrag.exe" -Argument "C: /O"
$trigger = New-ScheduledTaskTrigger -Daily -At 2:00AM
Register-ScheduledTask -TaskPath "\Microsoft\Windows\Defrag\" -TaskName "ScheduledDefrag" `
    -Action $action -Trigger $trigger -Settings (New-ScheduledTaskSettingsSet -StartWhenAvailable)
```

### 5.5 Verify Optimization Status

```powershell
# Get last run time and fragmentation level
$vol = Get-Volume -DriveLetter C
$vol | Select-Object DriveLetter, FileSystemLabel, 
    @{N='Fragmentation';E={ 
        (Optimize-Volume -DriveLetter $_.DriveLetter -Analyze -ErrorAction SilentlyContinue)
    }}

# Check via WMI
Get-WmiObject Win32_DefragAnalysis -Filter "DeviceID='C:'" | 
    Select-Object ConsolidationRecommendation, PercentFragmentation
```

---

## 6. NVMe Power States

NVMe defines six power states (PS0 through PS5) that control the trade-off between power consumption and access latency. Understanding these states is critical for balancing performance and power efficiency, especially on laptops.

**Risk:** MEDIUM | **Evidence:** 4/5 | **Restart Required:** Yes (for registry changes)

### 6.1 NVMe Power States Overview

| Power State | Name | Typical Latency | Power (typical) | Use Case |
|-------------|------|-----------------|------------------|----------|
| **PS0** | Maximum Performance | ~2-10 us | 5-10W (active) | Full-speed I/O |
| **PS1** | Non-Operational | ~30-100 us | 2-4W | Light idle |
| **PS2** | Non-Operational | ~100-500 us | 1-2W | Medium idle |
| **PS3** | Non-Operational | ~1-5 ms | 20-80 mW | Deep idle |
| **PS4** | Non-Operational | ~5-30 ms | 5-20 mW | Deepest idle (some drives) |
| **PS5** | Non-Operational | ~30-100 ms | <5 mW | Lowest power (rarely used) |

**Note:** Actual latencies and power values vary significantly by NVMe controller and firmware.

### 6.2 Query Current NVMe Power State

```powershell
# Check NVMe power state via storage reliability counters
Get-PhysicalDisk | Where-Object {$_.BusType -eq 'NVMe'} | 
    ForEach-Object {
        $counters = $_ | Get-StorageReliabilityCounter
        [PSCustomObject]@{
            FriendlyName = $_.FriendlyName
            MediaType = $_.MediaType
            HealthStatus = $_.HealthStatus
            Temperature = $counters.Temperature
            PowerOnHours = $counters.PowerOnHours
        }
    }

# Using smartctl (from smartmontools, if installed)
smartctl -a /dev/nvme0
```

### 6.3 AHCI Link Power Management (HIPM/DIPM)

For AHCI (SATA) drives, Host-Initiated Power Management (HIPM) and Device-Initiated Power Management (DIPM) control link power states.

**Registry settings:**
```
HKLM\SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device
```

or for AHCI:
```
HKLM\SYSTEM\CurrentControlSet\Services\storahci\Parameters\Device
```

**Power plan GUID for AHCI Link Power Management:**

```
HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\0012ee47-9041-4b5d-9b77-535fba8b1442\0b2d69d7-a2a1-449c-9680-f91c70521c6d
```

**Attributes:**
```
Attributes = 2   (show in power plan advanced settings)
```

**Values under the above key:**

| Setting | AHCI HIPM | Description |
|---------|-----------|-------------|
| `0` | Active | HIPM disabled, link stays active |
| `1` | Balanced | HIPM enabled, moderate power saving |
| `2` | Medium power | HIPM enabled, more aggressive |
| `3` | Maximum power savings | HIPM maximum aggression |
| `255` | (DIPM only) | Device-initiated only |

### 6.4 NVMe-Specific Power Settings

**Registry path:**
```
HKLM\SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device
```

| Value Name | Type | Default | Recommended | Description |
|------------|------|---------|-------------|-------------|
| `HpState` | REG_DWORD | 1 | 1 | Host Power State (1=enabled) |
| `nvme_lp` | REG_DWORD | 0 | 0 or 1 | NVMe Low Power (0=disabled, 1=enabled) |
| `nvme_power_state_transition_latency` | REG_DWORD | 0 | 0 | Max transition latency (microseconds, 0=no limit) |

### 6.5 Power Plan Configuration

```powershell
# List all power plans
powercfg /list

# Get current active plan
powercfg /getactivescheme

# Set AHCI Link Power Management to Maximum (value 2)
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\0012ee47-9041-4b5d-9b77-535fba8b1442\0b2d69d7-a2a1-449c-9680-f91c70521c6d"
Set-ItemProperty -Path $regPath -Name "Attributes" -Value 2 -Type DWord
```

### 6.6 Recommendations Per Scenario

| Scenario | HIPM/DIPM | NVMe Low Power | Min Sleep State | Notes |
|----------|:---------:|:--------------:|:---------------:|-------|
| **Desktop** | Disabled | Off | S3 | Latency-sensitive workloads benefit from staying in PS0 |
| **Laptop (battery)** | Maximum | On | S3/S0ix | Maximize battery life; accept slightly higher latency |
| **Laptop (plugged)** | Balanced | On | S3 | Best balance of performance and power |
| **Gaming** | Disabled | Off | S0 | Ensure fastest storage response times |
| **Workstation** | Balanced | Off | S3 | Fast access, moderate power saving at idle |
| **Server** | Disabled | Off | None | Always-on, performance first |

### 6.7 Optimize NVMe for Performance

```powershell
# Ensure NVMe controller is in highest performance state
# Set power plan to High Performance or Ultimate Performance
powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c   # High Performance
# or
powercfg /setactive e9a42b02-d5df-448d-aa00-03f14749eb61   # Ultimate Performance

# Disable PCI Express Link State Power Management (prevents ASPM)
powercfg /setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0
powercfg /setactive SCHEME_CURRENT

# Verify settings
powercfg /query SCHEME_CURRENT SUB_PCIEXPRESS ASPM
```

---

## 7. Disk Health Monitoring

Proactive disk health monitoring is essential for preventing data loss. Windows provides multiple cmdlets and WMI classes for querying disk health, SMART data, temperature, wear levels, and error counts.

**Risk:** LOW (read-only) | **Evidence:** 5/5 | **Restart Required:** No

### 7.1 Get-PhysicalDisk

```powershell
# Basic disk information
Get-PhysicalDisk | Format-Table DeviceId, FriendlyName, MediaType, BusType, 
    HealthStatus, OperationalStatus, Size -AutoSize

# Detailed view
Get-PhysicalDisk | Select-Object * | Format-List

# Filter to healthy disks only
Get-PhysicalDisk | Where-Object HealthStatus -ne 'Healthy'

# Filter by bus type
Get-PhysicalDisk | Where-Object BusType -eq 'NVMe'
```

### 7.2 Get-StorageReliabilityCounter

This cmdlet provides SMART-like health data including temperature, wear, power-on hours, and error counters.

```powershell
# All disks with reliability data
Get-PhysicalDisk | ForEach-Object {
    $disk = $_
    try {
        $reliability = $disk | Get-StorageReliabilityCounter -ErrorAction Stop
        [PSCustomObject]@{
            Disk           = $disk.FriendlyName
            BusType        = $disk.BusType
            Temperature    = "$($reliability.Temperature)C"
            TemperatureDelta = "$($reliability.TemperatureDelta)C"
            Wear           = "$($reliability.Wear)%"
            PowerOnHours   = $reliability.PowerOnHours
            ReadErrors     = $reliability.ReadErrorsTotal
            WriteErrors    = $reliability.WriteErrorsTotal
            UnloadErrors   = $reliability.UnloadErrorsTotal
            HashErrors     = $reliability.HashErrorsTotal
            HealthStatus   = $disk.HealthStatus
        }
    } catch {
        [PSCustomObject]@{
            Disk           = $disk.FriendlyName
            BusType        = $disk.BusType
            HealthStatus   = "N/A - $($_.Exception.Message)"
        }
    }
} | Format-Table -AutoSize
```

**Reliability counter properties:**

| Property | Description | Warning Threshold |
|----------|-------------|-------------------|
| `Temperature` | Current temperature in Celsius | >70C sustained |
| `TemperatureDelta` | Temperature change since last query | >10C rapid rise |
| `Wear` | Percentage of lifetime used (SSD/NVMe) | >80% approaching end of life |
| `PowerOnHours` | Total hours powered on | Varies by manufacturer |
| `ReadErrorsTotal` | Cumulative read errors | >0 warrants investigation |
| `WriteErrorsTotal` | Cumulative write errors | >0 warrants investigation |
| `UnloadErrorsTotal` | Head unload errors (HDD) | >0 on SSD is unusual |
| `HashErrorsTotal` | Data integrity errors | >0 indicates hardware failure |

### 7.3 Get-Disk

```powershell
# List all disks with partition style and operational status
Get-Disk | Select-Object Number, FriendlyName, PartitionStyle, 
    OperationalStatus, IsSystem, IsBoot, Size, 
    @{N='SizeGB';E={[math]::Round($_.Size/1GB,1)}} | Format-Table -AutoSize

# Detailed partition info for a specific disk
Get-Disk -Number 0 | Get-Partition | Format-Table PartitionNumber, DriveLetter, 
    Offset, Size, Type, IsActive -AutoSize
```

### 7.4 Win32_DiskDrive WMI

```powershell
# Comprehensive WMI disk query
Get-CimInstance Win32_DiskDrive | Select-Object DeviceID, Model, InterfaceType, 
    MediaType, Partitions, Size, SectorsPerTrack, TotalCylinders, 
    TotalHeads, TotalSectors, TotalTracks, FirmwareRevision, 
    SerialNumber, Status | Format-List

# Quick health check
Get-CimInstance Win32_DiskDrive | Select-Object Model, Status, 
    @{N='SizeGB';E={[math]::Round($_.Size/1GB,1)}} | Format-Table -AutoSize
```

### 7.5 Comprehensive Disk Health Monitoring Script

```powershell
<#
.SYNOPSIS
    Comprehensive disk health monitoring report.
.DESCRIPTION
    Gathers disk health data from multiple sources and generates a summary report.
    Run as Administrator for full data access.
#>

function Get-DiskHealthReport {
    $report = @()

    $disks = Get-PhysicalDisk
    foreach ($disk in $disks) {
        $entry = [ordered]@{
            Disk            = $disk.DeviceId
            Model           = $disk.FriendlyName
            BusType         = $disk.BusType
            MediaType       = $disk.MediaType
            SizeGB          = [math]::Round($disk.Size / 1GB, 1)
            HealthStatus    = $disk.HealthStatus
            OperationalStatus = ($disk.OperationalStatus -join ', ')
            SpindleSpeed    = $disk.SpindleSpeed
        }

        # Add reliability counters if available
        try {
            $reliability = $disk | Get-StorageReliabilityCounter -ErrorAction Stop
            $entry.Temperature = $reliability.Temperature
            $entry.TemperatureDelta = $reliability.TemperatureDelta
            $entry.Wear = $reliability.Wear
            $entry.PowerOnHours = $reliability.PowerOnHours
            $entry.ReadErrors = $reliability.ReadErrorsTotal
            $entry.WriteErrors = $reliability.WriteErrorsTotal
            $entry.FirmwareVersion = $reliability.FirmwareVersion
        } catch {
            $entry.Temperature = "N/A"
            $entry.Wear = "N/A"
            $entry.PowerOnHours = "N/A"
        }

        # Add partition info
        try {
            $partitions = Get-Partition -DiskNumber $disk.DeviceId -ErrorAction Stop
            $entry.PartitionCount = $partitions.Count
            $entry.DriveLetters = ($partitions | Where-Object DriveLetter | 
                Select-Object -ExpandProperty DriveLetter) -join ', '
        } catch {
            $entry.PartitionCount = "N/A"
            $entry.DriveLetters = "N/A"
        }

        $report += [PSCustomObject]$entry
    }

    # WMI supplemental data
    $wmiDisks = Get-CimInstance Win32_DiskDrive
    foreach ($wmi in $wmiDisks) {
        $match = $report | Where-Object { $_.Model -like "*$($wmi.Model.Substring(0, [Math]::Min(20, $wmi.Model.Length)))*" }
        if ($match) {
            $match | Add-Member -NotePropertyName 'SerialNumber' -NotePropertyValue $wmi.SerialNumber -Force
            $match | Add-Member -NotePropertyName 'FirmwareRevision' -NotePropertyValue $wmi.FirmwareRevision -Force
        }
    }

    # Output
    Write-Host "`n=== DISK HEALTH REPORT ===" -ForegroundColor Cyan
    Write-Host "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
    Write-Host "Total Disks: $($report.Count)`n" -ForegroundColor Gray

    foreach ($entry in $report) {
        $color = switch ($entry.HealthStatus) {
            'Healthy'     { 'Green' }
            'Caution'     { 'Yellow' }
            'Unhealthy'   { 'Red' }
            default       { 'White' }
        }

        Write-Host "--- Disk $($entry.Disk): $($entry.Model) ---" -ForegroundColor $color
        Write-Host "  Bus Type:         $($entry.BusType)"
        Write-Host "  Media Type:       $($entry.MediaType)"
        Write-Host "  Size:             $($entry.SizeGB) GB"
        Write-Host "  Health:           $($entry.HealthStatus)" -ForegroundColor $color
        Write-Host "  Status:           $($entry.OperationalStatus)"

        if ($entry.Temperature -ne "N/A") {
            $tempColor = if ($entry.Temperature -gt 70) { 'Red' } elseif ($entry.Temperature -gt 55) { 'Yellow' } else { 'Green' }
            Write-Host "  Temperature:      $($entry.Temperature)C (Delta: $($entry.TemperatureDelta)C)" -ForegroundColor $tempColor
        }
        if ($entry.Wear -ne "N/A") {
            $wearColor = if ($entry.Wear -gt 80) { 'Red' } elseif ($entry.Wear -gt 60) { 'Yellow' } else { 'Green' }
            Write-Host "  Wear Level:       $($entry.Wear)%" -ForegroundColor $wearColor
        }
        if ($entry.PowerOnHours -ne "N/A") {
            Write-Host "  Power-On Hours:   $($entry.PowerOnHours) ($([math]::Round($entry.PowerOnHours / 8760, 1)) years)"
        }
        if ($entry.ReadErrors -gt 0) {
            Write-Host "  READ ERRORS:      $($entry.ReadErrors)" -ForegroundColor Red
        }
        if ($entry.WriteErrors -gt 0) {
            Write-Host "  WRITE ERRORS:     $($entry.WriteErrors)" -ForegroundColor Red
        }

        Write-Host "  Partitions:       $($entry.PartitionCount) ($($entry.DriveLetters))"
        Write-Host ""
    }

    return $report
}

# Run the report
Get-DiskHealthReport
```

### 7.6 SMART Data via smartctl (Third-Party)

For more detailed SMART data, install [smartmontools](https://www.smartmontools.org/) and use:

```cmd
:: List all drives
smartctl --scan

:: Full SMART data for drive 0
smartctl -a /dev/sda

:: NVMe SMART data
smartctl -a /dev/nvme0

:: Quick health check
smartctl -H /dev/sda
```

---

## 8. Storage Controllers

Windows uses different storage drivers depending on the hardware interface. Understanding the storage stack helps diagnose performance issues and ensure the optimal driver is in use.

**Risk:** LOW (informational) to MEDIUM (driver changes) | **Evidence:** 4/5 | **Restart Required:** Varies

### 8.1 Storage Driver Overview

| Driver | Interface | Use Case | Notes |
|--------|-----------|----------|-------|
| **storahci** | AHCI (SATA) | SATA SSDs and HDDs | Microsoft inbox driver, reliable |
| **stornvme** | NVMe | NVMe SSDs | Microsoft inbox driver, good performance |
| **iaStorAC** | AHCI/NVMe | Intel SATA/NVMe | Intel RST driver, newer |
| **iaStorAVC** | VMD | Intel Volume Management Device | Intel VMD (PCIe NVMe behind VMD) |
| **rtsstor** | NVMe | Realtek NVMe | Realtek vendor driver |
| **amdgfx** / vendor | RAID | Hardware RAID controllers | Vendor-specific |
| **Spaceport** | Storage Spaces | Software-defined storage | Windows built-in |

### 8.2 NVMe vs AHCI Comparison

| Feature | AHCI (SATA) | NVMe |
|---------|:-----------:|:----:|
| **Max queue depth** | 32 | 65,535 |
| **Queue count** | 1 | 65,535 |
| **Max bandwidth** | 600 MB/s (SATA III) | 7,000+ MB/s (PCIe Gen4 x4) |
| **Latency** | ~100 us | ~10 us |
| **Protocol overhead** | High (AHCI) | Low (NVMe native) |
| **CPU overhead** | Higher | Lower |
| **Power efficiency** | Moderate | Better (at idle and active) |
| **Driver** | storahci | stornvme |
| **Interface** | SATA | PCIe |
| **Hot-plug** | Yes | Controller-dependent |
| **TRIM support** | Yes | Yes |
| **Price/GB (2026)** | ~$0.05 | ~$0.07 |

### 8.3 Query Current Storage Stack

```powershell
# Show storage controllers and their drivers
Get-CimInstance Win32_PnPSignedDriver | 
    Where-Object {$_.DeviceClass -eq 'SCSIAdapter' -or $_.DeviceClass -eq 'HDC'} |
    Select-Object DeviceName, DeviceClass, DriverVersion, InfName, 
    Manufacturer, FriendlyName | Format-Table -AutoSize

# Show which driver each physical disk is using
Get-PhysicalDisk | ForEach-Object {
    $disk = $_
    $pnp = Get-PnpDevice -InstanceId (Get-PnpDeviceProperty -InstanceId $_.DeviceId -KeyName 'DEVPKEY_Device_Service' -ErrorAction SilentlyContinue).Data
    [PSCustomObject]@{
        Disk   = $disk.FriendlyName
        Bus    = $disk.BusType
        Driver = $pnp.Service
    }
} | Format-Table -AutoSize

# Alternative: via registry
Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services" | 
    Where-Object { $_.Name -match '(stornvme|storahci|iaStor)' } |
    Select-Object PSChildName, @{N='ImagePath';E={(Get-ItemProperty $_.PSPath).ImagePath}}
```

### 8.4 MSI/MSI-X Interrupt Settings

Message Signaled Interrupts (MSI/MSI-X) allow devices to signal the CPU via memory-mapped writes rather than traditional pin-based interrupts. This reduces interrupt latency and improves multi-queue performance.

**Check current interrupt mode:**
```powershell
# Check if MSI-X is enabled for NVMe
Get-CimInstance Win32_PnPSignedDriver | 
    Where-Object DeviceName -like '*NVMe*' |
    Select-Object DeviceName, DriverVersion

# Via Device Manager > Storage controller > Properties > Details tab
# Property: "Interrupt Affinity Policy" (DEVPKEY_Device_AcceleratedListsSupported)
```

**Registry for interrupt policy:**
```
HKLM\SYSTEM\CurrentControlSet\Enum\<DeviceID>\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties
```

| Value | Type | Description |
|-------|------|-------------|
| `MSISupported` | REG_DWORD | `1` = MSI enabled, `0` = legacy INTx |
| `MSIXSupported` | REG_DWORD | `1` = MSI-X enabled |

**MSI-X is strongly recommended for NVMe** as it supports per-queue interrupts, enabling true parallelism.

### 8.5 Storage Stack Overview

```
Application
    |
    v
File System (NTFS / ReFS / FAT32)
    |
    v
Volume Manager (volsnap, partmgr)
    |
    v
Storage Port Driver (storahci / stornvme / vendor)
    |
    v
Miniport Driver (vendor-specific, if applicable)
    |
    v
Hardware (SATA / NVMe / RAID controller)
    |
    v
Physical Disk (SSD / HDD)
```

### 8.6 Driver Recommendations

| Hardware | Recommended Driver | Notes |
|----------|-------------------|-------|
| SATA SSD/HDD (generic) | storahci | Inbox driver is reliable and well-tested |
| NVMe SSD (generic) | stornvme | Microsoft inbox NVMe driver |
| Intel NVMe (with RST) | iaStorAC | Intel's driver; use if Intel RST is configured |
| Intel VMD (server/workstation) | iaStorAVC | Required for VMD-bypassed NVMe |
| Samsung NVMe | stornvme | Samsung's driver adds little over Microsoft's |
| WD/SanDisk NVMe | stornvme | Similar to Samsung recommendation |
| RAID controller | Vendor driver | Always use vendor driver for hardware RAID |

---

## 9. HDD-Specific Settings

Hard disk drives have unique characteristics (mechanical heads, spinning platters) that benefit from specific tuning for acoustic management, power saving, and longevity.

**Risk:** LOW | **Evidence:** 4/5 | **Restart Required:** No (most)

### 9.1 Automatic Acoustic Management (AAM)

AAM controls the aggressiveness of head seek operations, trading access speed for reduced noise.

```cmd
:: Query AAM status (requires smartctl from smartmontools)
smartctl -A /dev/sda | findstr "Automatic Acoustic"

:: Set AAM level (128=disabled/fast, 254=quietest/slowest, 128-254=range)
:: Via hdparm (Linux) or vendor tool on Windows
```

**AAM levels:**

| Value | Acoustic Level | Performance Level | Noise (typical) |
|-------|---------------|-------------------|-----------------|
| `128` | Disabled (fastest) | Maximum | Loudest |
| `180` | Quiet | Moderate | Moderate |
| `192` | Quiet | Balanced | Quiet |
| `254` | Quietest | Minimum | Quietest |

**Windows approach:** Use manufacturer's tools (e.g., SeaTools for Seagnostics, WDC Dashboard for WD) since Windows does not expose AAM natively.

### 9.2 Automatic Power Management (APM)

APM controls how aggressively the drive spins down or reduces performance to save power.

```cmd
:: Query APM via smartctl
smartctl -A /dev/sda | findstr "Power"
```

**APM behavior levels:**

| Level | Behavior | Use Case |
|-------|----------|----------|
| `128` (0x80) | Spindown enabled, moderate | Desktop with power savings |
| `254` (0xFE) | Performance | Workstation, database |
| `255` (0xFF) | Disabled (always on) | Server, NAS |
| `1`-`127` | Aggressive spindown | Laptops, archival drives |

### 9.3 Head Parking

HDD heads retract to a landing zone when the drive is idle, reducing the risk of head crashes during power loss but increasing wear from frequent parking cycles.

```powershell
# Check head parking count via SMART (smartctl required)
smartctl -A /dev/sda | Select-String "Load_Cycle_Count"

# Check via PowerShell SMART (if available)
Get-PhysicalDisk | Get-StorageReliabilityCounter | Select-Object -Property *
```

**Load Cycle Count thresholds:**

| Count | Assessment |
|-------|------------|
| 0-100,000 | Normal for desktop use |
| 100,000-300,000 | Elevated; check APM settings |
| 300,000-600,000 | High; consider disabling aggressive head parking |
| 600,000+ | Critical; drive may fail soon |

### 9.4 Disk Idle Timeout via powercfg

Windows manages disk spin-down through power plan settings. The disk timeout determines how long the disk stays active after the last I/O operation.

```cmd
:: Query current disk timeout (in seconds, 0=never)
powercfg /query SCHEME_CURRENT SUB_DISK DISKIDLE

:: Set disk idle timeout (in seconds)
:: Value in hex: 300 = 5 minutes, 600 = 10 minutes, 900 = 15 minutes
powercfg /setacvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE 300
powercfg /setdcvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE 900

:: Apply changes
powercfg /setactive SCHEME_CURRENT
```

**Recommended disk idle timeouts:**

| Scenario | AC Power | Battery | Notes |
|----------|----------|---------|-------|
| Desktop | 0 (never) or 600 (10 min) | N/A | HDDs spin down unnecessarily with 0 on desktops with lots of background I/O |
| Laptop (general) | 300 (5 min) | 180 (3 min) | Balance between power savings and spin-up delay |
| Laptop (presentation) | 0 (never) | 0 (never) | Prevent mid-presentation spin-up delays |
| Server | 0 (never) | N/A | Always-on required |

### 9.5 Write Cache Settings for HDD

Write cache on HDDs improves performance but risks data loss during power failure. See [Section 2](#2-write-caching-policies) for the registry and Device Manager methods.

**HDD-specific considerations:**

| Setting | Recommendation | Rationale |
|---------|---------------|-----------|
| Write cache (desktop, UPS) | ON | Battery backup protects against power loss |
| Write cache (desktop, no UPS) | ON (default) | Moderate risk; frequent saves mitigate |
| Write cache (laptop) | OFF (default) | Battery removal risk |
| Power-flush protection | OFF (default) | HDD cache is volatile; flushing on idle is prudent |

### 9.6 HDD Health Monitoring via powercfg

```cmd
:: Request a disk power capabilities report
powercfg /devicequery wake_armed

:: Generate a detailed sleep study (useful for diagnosing HDD spin-up issues)
powercfg /sleepstudy /output sleepstudy.html

:: Check which devices can wake the system
powercfg /devicequery wake_from_any
```

### 9.7 Comprehensive HDD Optimization Script

```powershell
<#
.SYNOPSIS
    Applies HDD-specific optimizations for desktop/laptop systems.
.DESCRIPTION
    Configures power management, idle timeouts, and monitoring for HDDs.
    Run as Administrator. Reverts with -RestoreDefaults switch.
#>

param(
    [switch]$RestoreDefaults,
    [string]$Profile = "Desktop"  # Desktop, Laptop, Server
)

function Set-HDDOptimizations {
    param([string]$Profile)

    Write-Host "`n=== HDD Optimization: $Profile Profile ===" -ForegroundColor Cyan

    # Power plan settings
    switch ($Profile) {
        "Desktop" {
            # Disk idle: 10 minutes on AC
            powercfg /setacvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE 600
            Write-Host "[OK] Disk idle timeout: 600s (10 min)" -ForegroundColor Green
        }
        "Laptop" {
            # Disk idle: 5 min AC, 3 min battery
            powercfg /setacvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE 300
            powercfg /setdcvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE 180
            Write-Host "[OK] Disk idle timeout: 300s (AC), 180s (Battery)" -ForegroundColor Green
        }
        "Server" {
            # Disk idle: never
            powercfg /setacvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE 0
            Write-Host "[OK] Disk idle timeout: 0 (never spin down)" -ForegroundColor Green
        }
    }

    # Apply changes
    powercfg /setactive SCHEME_CURRENT

    # Report HDD health
    Write-Host "`n--- HDD Health Status ---" -ForegroundColor Yellow
    Get-PhysicalDisk | Where-Object MediaType -eq 'HDD' | ForEach-Object {
        Write-Host "  Disk $($_.DeviceId): $($_.FriendlyName)" -ForegroundColor White
        Write-Host "    Health: $($_.HealthStatus)"
        Write-Host "    Size:   $([math]::Round($_.Size/1GB,1)) GB"
        
        try {
            $reliability = $_ | Get-StorageReliabilityCounter -ErrorAction Stop
            if ($reliability.PowerOnHours) {
                $years = [math]::Round($reliability.PowerOnHours / 8760, 1)
                Write-Host "    Power-On: $($reliability.PowerOnHours) hours ($years years)"
            }
        } catch {
            Write-Host "    SMART data: Not available" -ForegroundColor DarkGray
        }
    }
}

function Reset-HDDDefaults {
    Write-Host "`n=== Resetting HDD Settings to Defaults ===" -ForegroundColor Yellow
    powercfg /setacvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE 0
    powercfg /setdcvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE 0
    powercfg /setactive SCHEME_CURRENT
    Write-Host "[OK] Disk idle timeout reset to 0 (never)" -ForegroundColor Green
}

if ($RestoreDefaults) {
    Reset-HDDDefaults
} else {
    Set-HDDOptimizations -Profile $Profile
}
```

---

## Summary: Risk and Impact Matrix

| Section | Risk | Evidence | Restart | Performance Impact |
|---------|------|----------|---------|-------------------|
| 1. TRIM | LOW | 5/5 | No | Maintains SSD performance |
| 2. Write Caching | MEDIUM | 4/5 | Yes | Improved write throughput |
| 3. NTFS Settings | LOW | 5/5 | Partial | Reduced metadata overhead |
| 4. Storage Sense | LOW | 4/5 | No | Automated disk cleanup |
| 5. Defragmentation | LOW | 5/5 | No | Optimized file layout |
| 6. NVMe Power States | MEDIUM | 4/5 | Yes | Power vs latency trade-off |
| 7. Disk Monitoring | LOW | 5/5 | No | Early failure detection |
| 8. Storage Controllers | LOW-MED | 4/5 | Varies | Driver-level optimization |
| 9. HDD Settings | LOW | 4/5 | No | Acoustic/power optimization |

## Quick-Start Commands

```powershell
# === Run all LOW-risk optimizations in one shot ===

# 1. Enable TRIM
fsutil behavior set DisableDeleteNotify 0

# 2. NTFS optimizations
fsutil 8dot3name set C 1
fsutil behavior set disablelastaccess 2

# 3. Enable Storage Sense (balanced profile)
$ssPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy"
if (!(Test-Path $ssPath)) { New-Item -Path $ssPath -Force }
Set-ItemProperty -Path $ssPath -Name "StoragePolicy" -Value 0x30D -Type DWord

# 4. Schedule weekly optimization
# (Already enabled by default - verify with:)
Get-ScheduledTask -TaskPath "\Microsoft\Windows\Defrag\" | Select-Object TaskName, State

# 5. View current disk health
Get-PhysicalDisk | Select-Object DeviceId, FriendlyName, HealthStatus, BusType, MediaType
```

## References

- [Microsoft Docs: fsutil behavior](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior)
- [Microsoft Docs: Optimize-Volume](https://learn.microsoft.com/en-us/powershell/module/storage/optimize-volume)
- [Microsoft Docs: Get-PhysicalDisk](https://learn.microsoft.com/en-us/powershell/module/storage/get-physicaldisk)
- [Microsoft Docs: Get-StorageReliabilityCounter](https://learn.microsoft.com/en-us/powershell/module/storage/get-storagereliabilitycounter)
- [Microsoft Docs: Storage Sense](https://learn.microsoft.com/en-us/windows/storage/storage-sense)
- [NVMe Specification](https://nvmexpress.org/specifications/)
- [smartmontools](https://www.smartmontools.org/)
