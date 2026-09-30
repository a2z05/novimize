# Windows 11 Non-Destructive Cleanup Targets -- Comprehensive Reference

> Each entry documents: exact path, contents, safety, programmatic deletion, and recreability.

---

## 1. Windows Temp

**Path:** `C:\Windows\Temp`

**What it contains:** System-level temporary files created by Windows services, installers, drivers, and system processes. Includes .tmp, .log, .etl, .cab files, and installer remnants. Can accumulate significantly (hundreds of MB to several GB).

**Safe to delete?** YES, with caveats. Safe to delete files that are not currently locked by a running process. Windows locks files in use, so a simple file enumeration + delete will skip them. Do NOT force-delete locked files.

**How to delete:**

```powershell
# PowerShell (run as Administrator)
Get-ChildItem -Path "C:\Windows\Temp" -Recurse -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Alternative: robocopy empty folder trick (fast, skips locked files)
mkdir "$env:TEMP\empty_dir" -Force
robocopy "$env:TEMP\empty_dir" "C:\Windows\Temp" /MIR /XD * /XF * /NJH /NJS /NDL /NP
Remove-Item "$env:TEMP\empty_dir" -Recurse -Force

# Disk Cleanup approach (built-in, safest)
cleanmgr /sagerun:1
```

**Windows recreates it?** YES. Windows recreates this folder and its contents automatically. Some subdirectories are recreated on next service start or application launch.

---

## 2. User Temp

**Path:** `%LOCALAPPDATA%\Temp` (resolves to `C:\Users\<USERNAME>\AppData\Local\Temp`)

**What it contains:** Per-user temporary files from applications, browsers, editors, development tools, etc. Often the single largest user-reclaimable cache. Common contents: npm cache remnants, VS Code cache, browser temp downloads, application crash artifacts, installer staging. Can grow to many GB.

**Safe to delete?** YES, same caveat as Windows Temp -- skip locked files.

**How to delete:**

```powershell
# PowerShell (user-level, no admin needed for most files)
Get-ChildItem -Path "$env:LOCALAPPDATA\Temp" -Recurse -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# robocopy trick (fastest, skips locked files gracefully)
mkdir "$env:TEMP\empty_dir_temp" -Force
robocopy "$env:TEMP\empty_dir_temp" "$env:LOCALAPPDATA\Temp" /MIR /XD * /XF * /NJH /NJS /NDL /NP
Remove-Item "$env:TEMP\empty_dir_temp" -Recurse -Force

# Use environment variable to confirm actual path
Write-Host $env:LOCALAPPDATA\Temp
```

**Windows recreates it?** YES. Windows and applications recreate needed temp files on demand.

---

## 3. Windows Update Cache

**Path:** `C:\Windows\SoftwareDistribution\Download`

**What it contains:** Downloaded Windows Update packages (.cab, .esd, .wim files). After updates are installed, the downloaded files remain and are no longer needed. Can accumulate to 5-10+ GB after several months of updates.

**Safe to delete?** YES, but you must stop the Windows Update service first to avoid file locks and database corruption.

**How to delete:**

```powershell
# PowerShell (run as Administrator) -- must stop services first
Stop-Service -Name wuauserv -Force
Stop-Service -Name bits -Force

Remove-Item -Path "C:\Windows\SoftwareDistribution\Download\*" -Recurse -Force -ErrorAction SilentlyContinue

Start-Service -Name wuauserv
Start-Service -Name bits

# Verify service is running
Get-Service wuauserv, bits | Format-Table Name, Status

# Alternative: use DISM to clean up component store
DISM.exe /online /Cleanup-Image /StartComponentCleanup

# More aggressive (removes ability to uninstall updates):
DISM.exe /online /Cleanup-Image /StartComponentCleanup /ResetBase
```

**Windows recreates it?** YES. Windows Update will re-download updates as needed. The SoftwareDistribution folder structure is recreated automatically when the wuauserv service restarts.

---

## 4. Delivery Optimization Cache

**Paths:**
- `C:\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache`
- `C:\Windows\SoftwareDistribution\DeliveryOptimization`

**What it contains:** Delivery Optimization (DO) caches update chunks that have been downloaded and can be shared with other PCs on the local network or from Microsoft CDN. This is the peer-to-peer update sharing cache. Contents are .dat and . ModelRenderer files. Can be hundreds of MB to several GB.

**Safe to delete?** YES. Deleting the cache forces DO to re-download content if needed.

**How to delete:**

```powershell
# PowerShell (run as Administrator) -- must stop DO service
Stop-Service -Name DoSvc -Force

Remove-Item -Path "C:\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache\*" `
    -Recurse -Force -ErrorAction SilentlyContinue

Remove-Item -Path "C:\Windows\SoftwareDistribution\DeliveryOptimization\*" `
    -Recurse -Force -ErrorAction SilentlyContinue

Start-Service -Name DoSvc

# Alternative: clear via Delivery Optimization reset
Get-DeliveryOptimizationStatus | Format-Table

# Disable DO caching entirely (if you don't use peer-to-peer):
Set-DeliveryOptimization -DownloadMode 1  # Bypass mode (direct download only)

# Or via Group Policy / Registry:
# HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization
#   DODownloadMode = 1 (Bypass)
```

**Windows recreates it?** YES. Delivery Optimization recreates cache directories and re-downloads content as needed.

---

## 5. Recycle Bin

**Path:** `C:\$Recycle.Bin` (per-user SID subdirectories within)

**What it contains:** All files the user has deleted via Explorer or the shell. Each user has a SID-named subfolder. The Recycle Bin can hold files from all drives (separate $Recycle.Bin on each drive root).

**Safe to delete?** YES -- but this is permanent deletion. Files cannot be recovered after emptying.

**How to delete:**

```powershell
# PowerShell -- empty Recycle Bin for current user
Clear-RecycleBin -DriveLetter C -Force -ErrorAction SilentlyContinue

# Empty ALL drives' Recycle Bins
Clear-RecycleBin -Force -ErrorAction SilentlyContinue

# Check size before clearing
$shell = New-Object -ComObject Shell.Application
$rb = $shell.NameSpace(0xa)  # Recycle Bin
Write-Host "Items in Recycle Bin: $($rb.Items().Count)"

# Alternative: cmd approach
rd /s /q C:\$Recycle.Bin   # Requires admin, clears ALL users' recycle bins

# Nuclear: disable Recycle Bin entirely (files go straight to permanent delete)
# Not recommended for most users

# Check Recycle Bin size (PowerShell)
$rbPath = "C:\$Recycle.Bin"
$size = (Get-ChildItem $rbPath -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Host "Recycle Bin size: $([math]::Round($size / 1MB, 2)) MB"
```

**Windows recreates it?** The $Recycle.Bin folder itself is always present (system-created). It is always recreatable. Individual subdirectories per user SID are created when a user first deletes a file.

---

## 6. Crash Dumps

**Paths:**
- `C:\Windows\Minidump` -- Small memory dump files (.dmp) from BSODs
- `%LOCALAPPDATA%\CrashDumps` -- Application crash dumps (full user-mode dumps)
- `C:\Windows\MEMORY.DMP` -- Full kernel memory dump (can be very large, 1-16+ GB)

**What it contains:**
- **Minidump:** Small (64-256 KB each) crash dump files from blue screens. Named like `MMDDYY-HHMMSS-NN.dmp`. Useful for post-mortem BSOD analysis with WinDbg.
- **CrashDumps:** Full user-mode process crash dumps from Windows Error Reporting (WER). Can be large (hundreds of MB per dump).
- **MEMORY.DMP:** Complete kernel memory dump from last BSOD. Size equals roughly your RAM amount.

**Safe to delete?** YES, if you don't need to analyze past crashes. These are historical diagnostic files.

**How to delete:**

```powershell
# PowerShell (Administrator recommended for Minidump and MEMORY.DMP)
# Minidump files
Remove-Item -Path "C:\Windows\Minidump\*.dmp" -Force -ErrorAction SilentlyContinue

# User-mode crash dumps
Remove-Item -Path "$env:LOCALAPPDATA\CrashDumps\*.dmp" -Force -ErrorAction SilentlyContinue

# Full kernel dump (can reclaim many GB)
Remove-Item -Path "C:\Windows\MEMORY.DMP" -Force -ErrorAction SilentlyContinue

# Check sizes first
Get-ChildItem "C:\Windows\Minidump" -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum | Select-Object Count, @{N='SizeMB';E={[math]::Round($_.Sum/1MB,2)}}

Get-ChildItem "$env:LOCALAPPDATA\CrashDumps" -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum | Select-Object Count, @{N='SizeMB';E={[math]::Round($_.Sum/1MB,2)}}

# Change dump settings to reduce future accumulation:
# Small memory dump (recommended for most users -- 256 KB max):
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl" `
    -Name "CrashDumpEnabled" -Value 3  # 3 = Small memory dump

# Disable kernel dump entirely:
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl" `
    -Name "CrashDumpEnabled" -Value 0
```

**Windows recreates it?** NO. These are one-time diagnostic files. Once deleted, they are gone until the next crash. The directories themselves are recreated by Windows as needed.

---

## 7. Windows Error Reports

**Path:** `C:\ProgramData\Microsoft\Windows\WER`

**Subdirectories:**
- `C:\ProgramData\Microsoft\Windows\WER\ReportArchive` -- Archived error reports
- `C:\ProgramData\Microsoft\Windows\WER\ReportQueue` -- Queued reports waiting to be sent
- `C:\ProgramData\Microsoft\Windows\WER\Temp` -- Temporary working files

**What it contains:** Windows Error Reporting (WER) data files -- application crash reports, hang reports, compatibility problem reports. Contains .wer files, .xml metadata, and sometimes .tmp files. Typically hundreds of MB.

**Safe to delete?** YES. These are diagnostic/reports that have either been queued for sending or already archived.

**How to delete:**

```powershell
# PowerShell (Administrator recommended)
Remove-Item -Path "C:\ProgramData\Microsoft\Windows\WER\ReportArchive\*" `
    -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "C:\ProgramData\Microsoft\Windows\WER\ReportQueue\*" `
    -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "C:\ProgramData\Microsoft\Windows\WER\Temp\*" `
    -Recurse -Force -ErrorAction SilentlyContinue

# Alternative: Use the cleanup task
# Disk Cleanup > Clean up system files > check "Windows Error Reporting Files"

# Disable WER entirely (optional, not recommended for diagnosing issues):
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting" `
    -Name "Disabled" -Value 1
New-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting" `
    -Name "Disabled" -Value 1 -PropertyType DWORD -Force
```

**Windows recreates it?** YES. WER recreates directories as needed. New reports will be generated on future application crashes.

---

## 8. Thumbnail Cache

**Path:** `%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db`

**Files:**
- `thumbcache_16.db` -- 16x16 thumbnails
- `thumbcache_32.db` -- 32x32 thumbnails
- `thumbcache_48.db` -- 48x48 thumbnails
- `thumbcache_96.db` -- 96x96 thumbnails
- `thumbcache_256.db` -- 256x256 thumbnails
- `thumbcache_1024.db` -- 1024x1024 thumbnails
- `thumbcache_1280.db` -- 1280x1280 thumbnails
- `thumbcache_1920.db` -- 1920x1920 thumbnails
- `thumbcache_2560.db` -- 2560x2560 thumbnails
- `iconcache_*.db` -- Icon cache (see #17)
- `thumbcache_idx.db` -- Index file for the above

**What it contains:** Windows Explorer's cached thumbnail images for files (images, videos, documents, PDFs, folders). Avoids regenerating thumbnails every time you open a folder in Explorer. Can be tens to hundreds of MB on systems with large media collections.

**Safe to delete?** YES. Thumbnails are regenerated on-demand when you open a folder in Explorer. You may notice brief lag in Explorer the first time after deletion.

**How to delete:**

```powershell
# PowerShell -- requires stopping Explorer first (or accept partial deletion)
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\Windows\Explorer\thumbcache_*.db" `
    -Force -ErrorAction SilentlyContinue

Start-Process explorer

# Alternative: without killing Explorer (some files may be locked)
Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\Windows\Explorer\thumbcache_*.db" `
    -Force -ErrorAction SilentlyContinue

# Disable thumbnail generation entirely (not recommended):
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" `
    -Name "DisableThumbnailCache" -Value 1 -Type DWORD -Force
```

**Windows recreates it?** YES. Explorer regenerates thumbnails automatically when you browse folders.

---

## 9. Shader Cache (GPU)

### NVIDIA Shader Cache
**Path:** `%LOCALAPPDATA%\NVIDIA\DXCache`

**What it contains:** DirectX shader cache for NVIDIA GPUs. Pre-compiled HLSL shaders stored as `.bin` and `.cache` files. Improves game and application loading times by avoiding shader recompilation. Can be several GB for gamers.

### AMD Shader Cache
**Path:** `%LOCALAPPDATA%\AMD\DxCache`

**What it contains:** Equivalent DirectX shader cache for AMD GPUs. Same purpose as NVIDIA's cache.

### Intel Shader Cache
**Path:** `%LOCALAPPDATA%\Intel\ShaderCache`

**What it contains:** Shader cache for Intel integrated/Arc GPUs. Same concept as above.

**Safe to delete?** YES. All GPU shader caches are safe to delete. Games and applications will experience slightly longer initial load times as shaders are recompiled, but will rebuild the cache automatically.

**How to delete:**

```powershell
# PowerShell -- Kill GPU-dependent processes first for clean deletion

# NVIDIA
Remove-Item -Path "$env:LOCALAPPDATA\NVIDIA\DXCache\*" `
    -Recurse -Force -ErrorAction SilentlyContinue
# Also check for GLCache
Remove-Item -Path "$env:LOCALAPPDATA\NVIDIA\GLCache\*" `
    -Recurse -Force -ErrorAction SilentlyContinue

# AMD
Remove-Item -Path "$env:LOCALAPPDATA\AMD\DxCache\*" `
    -Recurse -Force -ErrorAction SilentlyContinue

# Intel
Remove-Item -Path "$env:LOCALAPPDATA%\Intel\ShaderCache\*" `
    -Recurse -Force -ErrorAction SilentlyContinue

# Nuclear option: clear ALL GPU caches at once
$shaderCachePaths = @(
    "$env:LOCALAPPDATA\NVIDIA\DXCache",
    "$env:LOCALAPPDATA\NVIDIA\GLCache",
    "$env:LOCALAPPDATA\AMD\DxCache",
    "$env:LOCALAPPDATA\Intel\ShaderCache"
)
foreach ($path in $shaderCachePaths) {
    if (Test-Path $path) {
        Remove-Item -Path "$path\*" -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Cleared: $path"
    }
}
```

**Windows recreates it?** YES. Shader caches are rebuilt by GPU drivers automatically during game/application use.

---

## 10. Browser Caches

### Google Chrome

Chrome stores cache under multiple profile directories. The default profile is "Default"; additional profiles are "Profile 1", "Profile 2", etc.

**Path:** `%LOCALAPPDATA%\Google\Chrome\User Data\`

**Cache subpaths (per profile):**

| Subpath | Description |
|---------|-------------|
| `<Profile>\Cache` | Main HTTP cache (disk cache) |
| `<Profile>\Cache\index` | Cache index file |
| `<Profile>\Code Cache\js` | JavaScript compiled code cache |
| `<Profile>\Code Cache\wasm` | WebAssembly compiled code cache |
| `<Profile>\GPUCache` | GPU shader cache (browser-specific) |
| `<Profile>\Service Worker\CacheStorage` | Service Worker cached responses |
| `<Profile>\Service Worker\ScriptCache` | Service Worker script cache |
| `<Profile>\Session Storage` | Session data (usually small) |
| `<Profile>\IndexedDB` | IndexedDB databases per-origin |
| `<Profile>\Local Storage` | LocalStorage per-origin |
| `<Profile>\DawnGraphiteCache` | Dawn/WebGPU cache |
| `<Profile>\DawnWebGPUCache` | Additional WebGPU cache |
| `ShaderCache\` | Browser-level GPU shader cache |
| `ShaderCache\GPUCache\` | GPU-specific shader cache |
| `GrShaderCache\` | Skia Graphite shader cache |

Where `<Profile>` = `Default`, `Profile 1`, `Profile 2`, etc.

**Full path pattern:** `%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache\*`

**Safe to delete?** YES. All cache subdirectories are safe. Chrome recreates them. Logged-in sessions and bookmarks are NOT affected (those live in different files). Cookies in `<Profile>\Cookies` are NOT cache and should be preserved if you want to stay logged in.

**How to delete:**

```powershell
# PowerShell -- must close Chrome first
Stop-Process -Name chrome -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

$chromeProfiles = @("Default", "Profile 1", "Profile 2", "Profile 3", "Profile 4")
$cacheSubDirs = @("Cache", "Code Cache", "GPUCache", "Service Worker\CacheStorage",
    "Service Worker\ScriptCache", "Session Storage", "DawnGraphiteCache",
    "DawnWebGPUCache")

$chromeBase = "$env:LOCALAPPDATA\Google\Chrome\User Data"

foreach ($profile in $chromeProfiles) {
    foreach ($subDir in $cacheSubDirs) {
        $path = Join-Path $chromeBase $profile $subDir
        if (Test-Path $path) {
            Remove-Item -Path "$path\*" -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# Top-level caches
Remove-Item -Path "$chromeBase\ShaderCache\*" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$chromeBase\GrShaderCache\*" -Recurse -Force -ErrorAction SilentlyContinue

# Measure Chrome cache size before deletion
$chromeCacheSize = (Get-ChildItem "$chromeBase\*\Cache" -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Host "Chrome cache size: $([math]::Round($chromeCacheSize / 1MB, 2)) MB"
```

### Mozilla Firefox

**Path:** `%LOCALAPPDATA%\Mozilla\Firefox\Profiles\`

Firefox profiles are named `<random>.<name>` (e.g., `a1b2c3d4.default`).

**Cache subpaths (per profile):**

| Subpath | Description |
|---------|-------------|
| `cache2\` | Main HTTP/disk cache (entries, index) |
| `cache2\entries\` | Cached response bodies |
| `cache2\index` | Cache index |
| `startupCache\` | Startup/script cache |
| `shader-cache\` | GPU shader cache |
| `cache2\doomed\` | Files marked for deletion |

**Full path pattern:** `%LOCALAPPDATA%\Mozilla\Firefox\Profiles\<random>.default\cache2\*`

**Safe to delete?** YES. Same as Chrome -- cache only, not profiles/bookmarks/passwords (which are in `logins.json`, `places.sqlite`, `key4.db`).

**How to delete:**

```powershell
# PowerShell -- must close Firefox first
Stop-Process -Name firefox -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

$firefoxProfiles = Get-ChildItem "$env:LOCALAPPDATA\Mozilla\Firefox\Profiles" -Directory

foreach ($profile in $firefoxProfiles) {
    $cacheDirs = @("cache2", "startupCache", "shader-cache")
    foreach ($dir in $cacheDirs) {
        $path = Join-Path $profile.FullName $dir
        if (Test-Path $path) {
            Remove-Item -Path "$path\*" -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
```

### Microsoft Edge

**Path:** `%LOCALAPPDATA%\Microsoft\Edge\User Data\`

Edge uses the same Chromium engine as Chrome, so the cache structure is nearly identical.

**Cache subpaths (per profile):**

| Subpath | Description |
|---------|-------------|
| `<Profile>\Cache` | Main HTTP disk cache |
| `<Profile>\Code Cache\js` | JavaScript compiled code cache |
| `<Profile>\Code Cache\wasm` | WebAssembly compiled code cache |
| `<Profile>\GPUCache` | GPU shader cache |
| `<Profile>\Service Worker\CacheStorage` | Service Worker cached responses |
| `<Profile>\Service Worker\ScriptCache` | Service Worker scripts |
| `<Profile>\Session Storage` | Session data |
| `<Profile>\DawnGraphiteCache` | Dawn/WebGPU cache |
| `<Profile>\DawnWebGPUCache` | WebGPU cache |
| `ShaderCache\` | Browser-level shader cache |
| `GrShaderCache\` | Skia Graphite cache |

Where `<Profile>` = `Default`, `Profile 1`, `Profile 2`, etc.

**Safe to delete?** YES. Same rationale as Chrome.

**How to delete:**

```powershell
# PowerShell -- must close Edge first
Stop-Process -Name msedge -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

$edgeProfiles = @("Default", "Profile 1", "Profile 2", "Profile 3")
$cacheSubDirs = @("Cache", "Code Cache", "GPUCache", "Service Worker\CacheStorage",
    "Service Worker\ScriptCache", "Session Storage", "DawnGraphiteCache",
    "DawnWebGPUCache")

$edgeBase = "$env:LOCALAPPDATA\Microsoft\Edge\User Data"

foreach ($profile in $edgeProfiles) {
    foreach ($subDir in $cacheSubDirs) {
        $path = Join-Path $edgeBase $profile $subDir
        if (Test-Path $path) {
            Remove-Item -Path "$path\*" -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
Remove-Item -Path "$edgeBase\ShaderCache\*" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$edgeBase\GrShaderCache\*" -Recurse -Force -ErrorAction SilentlyContinue
```

**Windows recreates all browser caches?** YES. All three browsers rebuild their caches automatically during browsing.

---

## 11. Windows Logs

**Path:** `C:\Windows\Logs`

**Subdirectories and files:**

| Path | Description | Typical Size |
|------|-------------|-------------|
| `C:\Windows\Logs\CBS\` | Component-Based Servicing logs (update install logs) | 50-500 MB |
| `C:\Windows\Logs\CBS\CBS.log` | Active CBS log | Rotates at ~10 MB |
| `C:\Windows\Logs\CBS\CBS.persist.log` | Previous CBS log | Variable |
| `C:\Windows\Logs\DISM\` | DISM operation logs | 10-100 MB |
| `C:\Windows\Logs\DISM\dism.log` | Active DISM log | Variable |
| `C:\Windows\Logs\MoSetup\` | Modern Setup logs (upgrade installs) | 50-200 MB |
| `C:\Windows\Logs\setupact.log` | Setup action log (upgrade/install) | 10-100 MB |
| `C:\Windows\Logs\setuperr.log` | Setup error log | Small |
| `C:\Windows\Logs\NetworkSetup.log` | Network setup log | Small |
| `C:\Windows\Logs\WaasMedic\` | Windows Update Medic logs | 10-50 MB |
| `C:\Windows\Panther\` | Setup/upgrade diagnostic logs | 50-500 MB |
| `C:\Windows\Panther\setupact.log` | Upgrade action log | Large after upgrade |
| `C:\Windows\Panther\setuperr.log` | Upgrade error log | Small |

**Safe to delete?** YES. Logs are historical diagnostics. Deleting them does not affect system operation. Windows will create new log files as needed.

**How to delete:**

```powershell
# PowerShell (Administrator)
$logPaths = @(
    "C:\Windows\Logs\CBS\*",
    "C:\Windows\Logs\DISM\*",
    "C:\Windows\Logs\MoSetup\*",
    "C:\Windows\Logs\WaasMedic\*",
    "C:\Windows\Logs\setupact.log",
    "C:\Windows\Logs\setuperr.log",
    "C:\Windows\Logs\NetworkSetup.log",
    "C:\Windows\Panther\*.log",
    "C:\Windows\Panther\*.etl"
)

foreach ($logPath in $logPaths) {
    Remove-Item -Path $logPath -Force -ErrorAction SilentlyContinue
}

# Measure total size before cleanup
$logsSize = (Get-ChildItem "C:\Windows\Logs" -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
$pantherSize = (Get-ChildItem "C:\Windows\Panther" -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Host "Logs: $([math]::Round($logsSize / 1MB, 2)) MB"
Write-Host "Panther: $([math]::Round($pantherSize / 1MB, 2)) MB"
```

**Windows recreates it?** YES. Windows creates new log files as needed. Active logging resumes immediately after deletion.

---

## 12. Font Cache

**Paths:**
- `C:\Windows\ServiceProfiles\LocalService\AppData\Local\FontCache\` -- System-wide font cache
- `C:\Windows\ServiceProfiles\LocalService\AppData\Local\~FontCache\` -- Temp font cache
- `%LOCALAPPDATA%\Microsoft\Windows\Fonts\` -- User-installed fonts (NOT cache, do not delete)
- `C:\Windows\Fonts\` -- System fonts (do not delete)

**Font cache files:**
- `FontCache\*.dat` -- Font cache data files (FontCache-*.dat, FontCache-S-1-5-*.dat)
- `FontCache\WindowsSystem.dat` -- System font cache
- `C:\Windows\System32\FNTCACHE.DAT` -- Legacy font cache

**What it contains:** Pre-built font metrics and glyph data to speed up font enumeration and rendering. Without the cache, Windows must scan all font files on every application start, which slows down font-heavy applications.

**Safe to delete?** YES, but with a brief performance penalty. Windows rebuilds font cache automatically on next restart (or immediately when the Windows Font Cache Service starts).

**How to delete:**

```powershell
# PowerShell (Administrator)

# Stop the Font Cache Service
Stop-Service -Name FontCache -Force
Stop-Service -Name "FontCache3.0.0.0" -Force -ErrorAction SilentlyContinue

# Clear font cache files
$fontCachePaths = @(
    "$env:SystemRoot\ServiceProfiles\LocalService\AppData\Local\FontCache\*.dat",
    "$env:SystemRoot\ServiceProfiles\LocalService\AppData\Local\~FontCache\*",
    "$env:SystemRoot\System32\FNTCACHE.DAT"
)

foreach ($path in $fontCachePaths) {
    Remove-Item -Path $path -Force -ErrorAction SilentlyContinue
}

# Restart the service (triggers cache rebuild)
Start-Service -Name FontCache
Start-Service -Name "FontCache3.0.0.0" -Force -ErrorAction SilentlyContinue
```

**Windows recreates it?** YES. The Font Cache Service rebuilds the cache automatically on restart. You may see a brief delay the first time applications enumerate fonts.

---

## 13. DNS Cache

**What it contains:** In-memory cache of recent DNS lookups. Does not consume disk space -- lives entirely in RAM. Cleared on reboot anyway. Useful to flush when DNS changes have been made or DNS resolution is stale.

**Safe to delete?** YES. Completely safe. DNS will be re-resolved on next request.

**How to delete:**

```cmd
:: Command Prompt (Administrator recommended)
ipconfig /flushdns

:: PowerShell equivalent
Clear-DnsClientCache

:: Verify it was cleared
ipconfig /displaydns | Measure-Object -Line
:: Should show 0 lines or "Could not display the DNS Resolver Cache"
```

```powershell
# PowerShell -- check current cache size
$dnsStats = Get-DnsClientCache | Measure-Object
Write-Host "DNS cache entries: $($dnsStats.Count)"
Clear-DnsClientCache
Write-Host "DNS cache flushed."
```

**Windows recreates it?** YES. DNS cache is rebuilt immediately as DNS lookups occur during normal network activity.

---

## 14. Windows.old

**Path:** `C:\Windows.old`

**What it contains:** A complete backup of the previous Windows installation after a major feature update or clean install performed while keeping old files. Contains the entire previous `C:\Windows`, `C:\Program Files`, `C:\Users`, etc. This can be enormous (20-50+ GB).

**Safe to delete?** YES, BUT you lose the ability to roll back to the previous Windows version. Microsoft automatically removes it after 10 days (configurable via Storage Sense or `Settings > System > Storage > Temporary Files > Previous Windows Installation`). After 10 days, Windows will not let you roll back anyway.

**How to delete:**

```powershell
# PowerShell (Administrator)

# Method 1: Disk Cleanup (recommended by Microsoft)
cleanmgr /sagerun:1
# Then check "Previous Windows Installation(s)" and "Temporary Windows Installation files"

# Method 2: Storage Sense
# Settings > System > Storage > Temporary Files > check "Previous Windows Installation"
# Click "Remove files"

# Method 3: Direct deletion (requires admin + take ownership)
# First take ownership
$path = "C:\Windows.old"
$acl = Get-Acl $path
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.AddAccessRule($rule)
Set-Acl $path $acl

Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue

# Method 4: DISM cleanup (removes without manual deletion)
DISM.exe /online /Cleanup-Image /StartComponentCleanup

# Change rollback period from default 10 days to fewer:
DISM.exe /online /Cleanup-Image /StartComponentCleanup /ResetBase
# Note: /ResetBase makes rollback impossible immediately
```

**Windows recreates it?** NO. Windows.old is a one-time artifact from an upgrade. It is not recreated unless you perform another major Windows upgrade.

---

## 15. Package Cache ($PatchCache$)

**Path:** `C:\Windows\Installer\$PatchCache$`

**What it contains:** Cached patch/hotfix information used by Windows Installer (MSI) for patching and uninstalling updates to installed applications. Contains `.patch` files and metadata. Allows MSI to roll back patches.

**Safe to delete?** CAUTION -- partially safe. The `$PatchCache$` subdirectory specifically is generally safe to delete (it is a cache of merged patch data). However, the parent `C:\Windows\Installer` folder contains critical MSI installer cache files (.msp, .msi) that should NOT be deleted. Losing those breaks application repair/uninstall.

**Better approach: Use PatchCleaner or similar tool that identifies orphaned files.**

**How to delete (only the PatchCache subdirectory):**

```powershell
# PowerShell (Administrator) -- ONLY delete the $PatchCache$ subfolder, NOT the entire Installer folder

$patchCacheSize = (Get-ChildItem "C:\Windows\Installer\$PatchCache$" -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Host "PatchCache size: $([math]::Round($patchCacheSize / 1MB, 2)) MB"

Remove-Item -Path "C:\Windows\Installer\$PatchCache$\*" -Recurse -Force -ErrorAction SilentlyContinue

# DO NOT DO THIS -- it will break installed applications:
# Remove-Item -Path "C:\Windows\Installer\*" -Recurse -Force   # NEVER RUN THIS

# For a safer full cleanup, use PatchCleaner (free tool):
# https://www.homedev.com.au/Free/PatchCleaner
# It identifies orphaned .msp/.msi files that are no longer referenced.
```

**Windows recreates it?** YES. Windows Installer rebuilds the $PatchCache$ from installed MSI databases as needed, though a full rebuild may require running applications that trigger MSI operations.

---

## 16. Windows Prefetch

**Path:** `C:\Windows\Prefetch`

**What it contains:** `.pf` files that Windows uses to speed up application startup. Contains information about which files and DLLs each application loads, allowing Windows to pre-load them before the application requests them. Files are named `<APPNAME>-<HASH>.pf`. Each application typically has 1-4 prefetch files. Can accumulate to hundreds of MB after extensive use.

**Safe to delete?** YES, but with a trade-off. Deleting prefetch files means the first launch of each application after deletion will be slower while Windows re-learns the startup pattern. Subsequent launches return to normal speed. Some power users delete old prefetch files periodically to remove entries for uninstalled applications.

**How to delete:**

```powershell
# PowerShell (Administrator recommended but not strictly required)

# Delete all prefetch files
Remove-Item -Path "C:\Windows\Prefetch\*.pf" -Force -ErrorAction SilentlyContinue

# Delete only files older than 30 days (keeps recent, removes stale)
Get-ChildItem "C:\Windows\Prefetch\*.pf" -Force |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
    Remove-Item -Force -ErrorAction SilentlyContinue

# Measure size
$pfSize = (Get-ChildItem "C:\Windows\Prefetch\*.pf" -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Host "Prefetch size: $([math]::Round($pfSize / 1MB, 2)) MB"

# Disable prefetch entirely (NOT recommended -- slows app launches):
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters" `
    -Name "EnablePrefetcher" -Value 0 -Type DWORD -Force
```

**Windows recreates it?** YES. Windows recreates prefetch files on the next launch of each application. The `ReadyBoot` subdirectory within Prefetch is also recreated by the system.

---

## 17. Icon Cache

**Path:** `%LOCALAPPDATA%\IconCache.db` (legacy location)
**Modern location:** `%LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache_*.db`

**Files:**
- `IconCache.db` -- Legacy single-file icon cache (older Windows)
- `iconcache_16.db` -- 16x16 icons
- `iconcache_32.db` -- 32x32 icons
- `iconcache_48.db` -- 48x48 icons
- `iconcache_96.db` -- 96x96 icons
- `iconcache_256.db` -- 256x256 icons
- `iconcache_1280.db` -- 1280x1280 icons
- `iconcache_1920.db` -- 1920x1920 icons
- `iconcache_2560.db` -- 2560x2560 icons
- `iconcache_idx.db` -- Index file

**What it contains:** Pre-rendered icon images from executables, DLLs, and file type associations. Allows Explorer to display icons quickly without re-extracting them from binaries each time.

**Safe to delete?** YES. Icons are regenerated on-demand. You may see brief "blank icon" flashes in Explorer immediately after deletion.

**How to delete:**

```powershell
# PowerShell -- must stop Explorer for reliable deletion
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Delete legacy and modern icon cache files
Remove-Item -Path "$env:LOCALAPPDATA\IconCache.db" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\Windows\Explorer\iconcache_*.db" `
    -Force -ErrorAction SilentlyContinue

Start-Process explorer

# Alternative: without killing Explorer (may fail on locked files)
Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\Windows\Explorer\iconcache_*.db" `
    -Force -ErrorAction SilentlyContinue

# Rebuild icon cache by clearing the thumbnail cache too (they share the Explorer process)
```

**Windows recreates it?** YES. Explorer regenerates icon cache automatically when browsing folders.

---

## 18. Windows Update Logs

**Path:** `C:\Windows\Logs\WindowsUpdate`

**Files:**
- `WindowsUpdate.log` -- Main Windows Update log
- `WindowsUpdate.altlog` -- Alternative/update log
- Various `.etl` (Event Trace Log) files from Windows Update operations

**What it contains:** Detailed Windows Update diagnostic logs including update search, download, install, and error information. In Windows 10/11, much of the update logging has moved to Event Tracing for Windows (ETW) with `.etl` files, which are then compiled into the readable `WindowsUpdate.log`. The ETL files can accumulate significantly.

**Safe to delete?** YES. Historical diagnostic data. Useful only for troubleshooting past update issues.

**How to delete:**

```powershell
# PowerShell (Administrator recommended)
Remove-Item -Path "C:\Windows\Logs\WindowsUpdate\*" -Recurse -Force -ErrorAction SilentlyContinue

# Measure size first
$wuLogSize = (Get-ChildItem "C:\Windows\Logs\WindowsUpdate" -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Host "Windows Update logs: $([math]::Round($wuLogSize / 1MB, 2)) MB"

# Regenerate readable WindowsUpdate.log from ETW traces (if needed for debugging):
# Get-WindowsUpdateLog -LogPath "$env:USERPROFILE\Desktop\WindowsUpdate.log"
```

**Windows recreates it?** YES. New log files are created on the next Windows Update operation.

---

## 19. Software Reporter Tool (Chrome)

**Path:** `%LOCALAPPDATA%\Google\Chrome\User Data\SwReporter\<VERSION>\`

Where `<VERSION>` is a version number directory like `130.0.6723.58` or similar.

**Files:**
- `software_reporter_tool.exe` -- The scanner executable (~2-3 MB)
- `software_reporter_tool.exe.sig` -- Signature file
- `crashpad_handler.exe` -- Crash reporter

**What it contains:** Google's Software Reporter Tool, which periodically scans for software that may interfere with Chrome (malware, unwanted extensions, etc.). It runs as a scheduled task and can use significant CPU (20-50%+) for extended periods (10-30+ minutes). It generates reports in `%LOCALAPPDATA%\Google\Chrome\User Data\SwReporter\` and writes results to Chrome settings.

**Safe to delete?** YES. Chrome will recreate it on the next Chrome update. Disabling it permanently is done by removing registry permissions or the scheduled task.

**How to delete:**

```powershell
# PowerShell -- delete the tool (it will return on next Chrome update)
$swReporterPath = "$env:LOCALAPPDATA\Google\Chrome\User Data\SwReporter"
if (Test-Path $swReporterPath) {
    $versions = Get-ChildItem $swReporterPath -Directory
    foreach ($ver in $versions) {
        Remove-Item -Path "$($ver.FullName)\*" -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Cleared SwReporter version: $($ver.Name)"
    }
}

# Better long-term solution: disable via registry
# Prevent Chrome from loading the Software Reporter Tool
$regPath = "HKCU:\SOFTWARE\Google\Chrome\Plugins"
New-ItemProperty -Path $regPath -Name "SoftwareReporter" -Value 0 -PropertyType DWORD -Force

# Or remove the scheduled task that runs it
Unregister-ScheduledTask -TaskName "Software Reporter Tool" -Confirm:$false -ErrorAction SilentlyContinue

# Alternative registry approach (set to 0x0 to disable):
Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" `
    -Name "DisallowSoftwareReporterTool" -Value 1 -Type DWORD -Force
```

**Windows recreates it?** YES. Chrome restores it on each update. The permanent fix is the registry approach above.

---

## 20. Large System Files (hiberfil.sys, pagefile.sys, swapfile.sys) -- WARNING

These files are in `C:\` but are HIDDEN, SYSTEM, PROTECTED files. They are **NOT safe to simply delete**.

### hiberfil.sys

**Path:** `C:\hiberfil.sys`

**What it contains:** A compressed copy of the contents of RAM used for Windows Hibernate (H4) and Fast Startup (H2). Size is typically 40-75% of installed RAM (e.g., 16 GB RAM = 6-12 GB hiberfil.sys). In Windows 11 24H2+, may use H2 (hybrid) or H4 compression.

**NOT safe to simply delete.** Deleting hiberfil.sys by force can cause:
- Fast Startup to fail (blue screen or slow boot)
- Hibernate to be unavailable
- Potential system instability if hibernation was in progress

**Proper management:**

```powershell
# Check current hibernation state
powercfg /a

# Check hiberfil.sys size
Get-Item "C:\hiberfil.sys" -Force | Select-Object @{N='SizeGB';E={[math]::Round($_.Length/1GB,2)}}

# Reduce size (reduces from 75% to 50% of RAM):
powercfg /h /type reduced

# Disable hibernation entirely (removes hiberfil.sys properly):
powercfg /h off
# This safely removes hiberfil.sys and frees the disk space

# Re-enable later:
powercfg /h on

# Disable Fast Startup only (reduces but does not eliminate hiberfil.sys):
powercfg /h /type reduced
# Or via Control Panel: Power Options > Choose what the power buttons do >
#   Change settings that are currently unavailable > Uncheck "Turn on fast startup"
```

### pagefile.sys

**Path:** `C:\pagefile.sys`

**What it contains:** Virtual memory page file -- overflow storage for RAM when physical memory is exhausted. Size is managed automatically by Windows (typically 1-1.5x RAM size, or dynamically sized).

**NOT safe to simply delete.** Consequences:
- Applications will crash with "out of memory" errors when physical RAM is exhausted
- System may become unstable or unresponsive
- Windows will recreate it on next boot if set to "System managed"

**Proper management:**

```powershell
# Check pagefile size
Get-Item "C:\pagefile.sys" -Force | Select-Object @{N='SizeGB';E={[math]::Round($_.Length/1GB,2)}}

# Move pagefile to another drive (if you have one with space):
wmic computersystem where name="%computername%" set AutomaticManagedPagefile=False
wmic pagefileset where name="C:\\pagefile.sys" delete
wmic pagefileset create name="D:\\pagefile.sys"
wmic pagefileset where name="D:\\pagefile.sys" set InitialSize=4096,MaximumSize=8192

# Set custom size (min/max in MB):
# System Properties > Advanced > Performance Settings > Advanced > Virtual Memory > Change

# Check current configuration
Get-WmiObject Win32_PageFileSetting -ErrorAction SilentlyContinue | Select-Object Name, InitialSize, MaximumSize
```

### swapfile.sys

**Path:** `C:\swapfile.sys`

**What it contains:** Separate swap file for UWP (Universal Windows Platform) apps. Always 256 MB (fixed size). Required for modern apps to function.

**NOT safe to delete.** Windows recreates it automatically, but deleting it while running will crash UWP apps. It is trivially small (256 MB) so there is no benefit to removing it.

**Proper management:**

```powershell
# Swap file is automatically managed and only exists when pagefile exists
# It is created/removed along with the pagefile
# No separate management needed

# Check it exists
Get-Item "C:\swapfile.sys" -Force -ErrorAction SilentlyContinue |
    Select-Object FullName, @{N='SizeMB';E={[math]::Round($_.Length/1MB,2)}}
```

**Windows recreates these files?**
- **hiberfil.sys:** Only if hibernation is re-enabled. Does NOT recreate itself if you force-delete it while hibernation is enabled (causes errors instead).
- **pagefile.sys:** YES, Windows recreates it on boot if pagefile is configured (which it always is by default).
- **swapfile.sys:** YES, recreated automatically alongside pagefile.sys.

---

## BONUS: Additional Safe Cleanup Targets

### Windows Thumbnail Cache (beyond thumbcache_*.db)

```powershell
# Additional Explorer cache
Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\Windows\Explorer\*.db" -Force -ErrorAction SilentlyContinue
```

### Delivery Optimization Logs

```powershell
Remove-Item -Path "C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Logs\*" `
    -Recurse -Force -ErrorAction SilentlyContinue
```

### Windows Error Reporting (User-level)

```powershell
Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\Windows\WER\ReportArchive\*" `
    -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\Windows\WER\ReportQueue\*" `
    -Recurse -Force -ErrorAction SilentlyContinue
```

### Windows Installer Patch Cache (cautious)

```powershell
# Check size before cleanup
$installerSize = (Get-ChildItem "C:\Windows\Installer" -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Host "Windows Installer cache: $([math]::Round($installerSize / 1MB, 2)) MB"
# Only delete $PatchCache$ subfolder, NOT the main Installer folder contents
```

### Event Logs (system diagnostic logs)

```powershell
# Clear all event logs (comprehensive cleanup, but removes diagnostic history)
wevtutil el | ForEach-Object { wevtutil cl $_ } 2>$null

# Or clear specific logs:
wevtutil cl Application
wevtutil cl System
wevtutil cl Security
wevtutil cl Setup
```

### Store Cache

```powershell
# Microsoft Store download cache
Remove-Item -Path "$env:LOCALAPPDATA\Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache\*" `
    -Recurse -Force -ErrorAction SilentlyContinue

# Software Distribution (alternative approach via service)
```

### Notification Area Icon Cache

```powershell
# System tray icon cache
Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\Windows\Explorer\iconcache_*.db" `
    -Force -ErrorAction SilentlyContinue
```

---

## Master Cleanup Script

The following PowerShell script combines all safe cleanup targets into a single run:

```powershell
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Comprehensive Windows 11 non-destructive cleanup script.
.DESCRIPTION
    Safely removes temporary files, caches, and logs while preserving system stability.
    Always skip files that are locked/in-use rather than force-deleting them.
#>

$ErrorActionPreference = "SilentlyContinue"
$totalFreed = 0

function Get-FolderSize {
    param([string]$Path)
    if (Test-Path $Path) {
        return (Get-ChildItem $Path -Recurse -Force -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
    }
    return 0
}

function Remove-CacheFiles {
    param([string]$Path, [string]$Description)
    $before = Get-FolderSize $Path
    Remove-Item -Path "$Path\*" -Recurse -Force -ErrorAction SilentlyContinue
    $after = Get-FolderSize $Path
    $freed = $before - $after
    $script:totalFreed += $freed
    Write-Host ("  {0}: Freed {1:N2} MB" -f $Description, ($freed / 1MB))
}

Write-Host "`n=== Windows 11 Cleanup Script ===" -ForegroundColor Cyan
Write-Host "Running as: $env:USERNAME`n"

# --- Stop services that lock files ---
Write-Host "Stopping services..." -ForegroundColor Yellow
$servicesToStop = @("wuauserv", "bits", "DoSvc", "FontCache", "FontCache3.0.0.0")
foreach ($svc in $servicesToStop) {
    Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
}

# --- Kill browsers ---
Write-Host "Closing browsers..." -ForegroundColor Yellow
$browserProcs = @("chrome", "firefox", "msedge", "opera", "brave")
foreach ($proc in $browserProcs) {
    Stop-Process -Name $proc -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 3

# --- 1. Windows Temp ---
Write-Host "`n[1/16] Windows Temp" -ForegroundColor Green
Remove-CacheFiles "C:\Windows\Temp" "Windows Temp"

# --- 2. User Temp ---
Write-Host "[2/16] User Temp" -ForegroundColor Green
Remove-CacheFiles "$env:LOCALAPPDATA\Temp" "User Temp"

# --- 3. Windows Update Cache ---
Write-Host "[3/16] Windows Update Cache" -ForegroundColor Green
Remove-CacheFiles "C:\Windows\SoftwareDistribution\Download" "WU Download Cache"

# --- 4. Delivery Optimization ---
Write-Host "[4/16] Delivery Optimization" -ForegroundColor Green
Remove-CacheFiles "C:\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache" "DO Cache"
Remove-CacheFiles "C:\Windows\SoftwareDistribution\DeliveryOptimization" "DO Alt Cache"

# --- 5. Recycle Bin ---
Write-Host "[5/16] Recycle Bin" -ForegroundColor Green
$rbSize = (Get-ChildItem "C:\$Recycle.Bin" -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Clear-RecycleBin -Force -ErrorAction SilentlyContinue
$script:totalFreed += $rbSize
Write-Host ("  Recycle Bin: Freed {0:N2} MB" -f ($rbSize / 1MB))

# --- 6. Crash Dumps ---
Write-Host "[6/16] Crash Dumps" -ForegroundColor Green
Remove-CacheFiles "C:\Windows\Minidump" "Minidumps"
Remove-CacheFiles "$env:LOCALAPPDATA\CrashDumps" "User CrashDumps"
Remove-Item -Path "C:\Windows\MEMORY.DMP" -Force -ErrorAction SilentlyContinue

# --- 7. WER ---
Write-Host "[7/16] Windows Error Reports" -ForegroundColor Green
Remove-CacheFiles "C:\ProgramData\Microsoft\Windows\WER" "WER (System)"
Remove-CacheFiles "$env:LOCALAPPDATA\Microsoft\Windows\WER" "WER (User)"

# --- 8. Thumbnail Cache ---
Write-Host "[8/16] Thumbnail Cache" -ForegroundColor Green
Remove-CacheFiles "$env:LOCALAPPDATA\Microsoft\Windows\Explorer" "Thumbnails"

# --- 9. GPU Shader Caches ---
Write-Host "[9/16] GPU Shader Caches" -ForegroundColor Green
$gpuPaths = @(
    "$env:LOCALAPPDATA\NVIDIA\DXCache",
    "$env:LOCALAPPDATA\NVIDIA\GLCache",
    "$env:LOCALAPPDATA\AMD\DxCache",
    "$env:LOCALAPPDATA\Intel\ShaderCache"
)
foreach ($p in $gpuPaths) { Remove-CacheFiles $p (Split-Path $p -Leaf) }

# --- 10. Browser Caches ---
Write-Host "[10/16] Browser Caches" -ForegroundColor Green
# Chrome
$chromeBase = "$env:LOCALAPPDATA\Google\Chrome\User Data"
$chromeProfiles = Get-ChildItem $chromeBase -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match "^(Default|Profile \d+)$" }
foreach ($prof in $chromeProfiles) {
    foreach ($sub in @("Cache", "Code Cache", "GPUCache", "Service Worker\CacheStorage",
        "Service Worker\ScriptCache", "Session Storage")) {
        Remove-CacheFiles (Join-Path $prof.FullName $sub) "Chrome $($prof.Name)/$sub"
    }
}
# Firefox
$ffProfiles = Get-ChildItem "$env:LOCALAPPDATA\Mozilla\Firefox\Profiles" -Directory -ErrorAction SilentlyContinue
foreach ($prof in $ffProfiles) {
    foreach ($sub in @("cache2", "startupCache", "shader-cache")) {
        Remove-CacheFiles (Join-Path $prof.FullName $sub) "Firefox/$sub"
    }
}
# Edge
$edgeBase = "$env:LOCALAPPDATA\Microsoft\Edge\User Data"
$edgeProfiles = Get-ChildItem $edgeBase -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match "^(Default|Profile \d+)$" }
foreach ($prof in $edgeProfiles) {
    foreach ($sub in @("Cache", "Code Cache", "GPUCache", "Service Worker\CacheStorage",
        "Service Worker\ScriptCache", "Session Storage")) {
        Remove-CacheFiles (Join-Path $prof.FullName $sub) "Edge $($prof.Name)/$sub"
    }
}

# --- 11. Windows Logs ---
Write-Host "[11/16] Windows Logs" -ForegroundColor Green
$logPaths = @(
    "C:\Windows\Logs\CBS",
    "C:\Windows\Logs\DISM",
    "C:\Windows\Logs\MoSetup",
    "C:\Windows\Logs\WaasMedic",
    "C:\Windows\Logs\WindowsUpdate"
)
foreach ($lp in $logPaths) { Remove-CacheFiles $lp (Split-Path $lp -Leaf) }

# --- 12. Font Cache ---
Write-Host "[12/16] Font Cache" -ForegroundColor Green
Remove-CacheFiles "C:\Windows\ServiceProfiles\LocalService\AppData\Local\FontCache" "Font Cache"

# --- 13. DNS Cache ---
Write-Host "[13/16] DNS Cache" -ForegroundColor Green
Clear-DnsClientCache
Write-Host "  DNS cache flushed."

# --- 14. Windows.old ---
Write-Host "[14/16] Windows.old" -ForegroundColor Green
if (Test-Path "C:\Windows.old") {
    $oldSize = Get-FolderSize "C:\Windows.old"
    Write-Host ("  Windows.old detected: {0:N2} MB -- run Disk Cleanup to remove" -f ($oldSize / 1MB))
    Write-Host "  (cleanmgr /sagerun:1 or Settings > Storage > Temporary Files)"
} else {
    Write-Host "  Windows.old not present."
}

# --- 15. Patch Cache ---
Write-Host "[15/16] Package Cache" -ForegroundColor Green
Remove-CacheFiles "C:\Windows\Installer\$PatchCache$" "PatchCache$"

# --- 16. Prefetch ---
Write-Host "[16/16] Prefetch" -ForegroundColor Green
# Only delete prefetch files older than 30 days
$oldPrefetch = Get-ChildItem "C:\Windows\Prefetch\*.pf" -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) }
$pfFreed = ($oldPrefetch | Measure-Object -Property Length -Sum).Sum
$oldPrefetch | Remove-Item -Force -ErrorAction SilentlyContinue
$script:totalFreed += $pfFreed
Write-Host ("  Prefetch (>30 days): Freed {0:N2} MB" -f ($pfFreed / 1MB))

# --- Restart services ---
Write-Host "`nRestarting services..." -ForegroundColor Yellow
foreach ($svc in $servicesToStop) {
    Start-Service -Name $svc -ErrorAction SilentlyContinue
}

# --- Summary ---
Write-Host "`n=== Cleanup Complete ===" -ForegroundColor Cyan
Write-Host ("Total space freed: {0:N2} MB ({1:N2} GB)" -f ($totalFreed / 1MB), ($totalFreed / 1GB))
Write-Host ""

# --- Additional recommendations ---
Write-Host "=== Additional Manual Steps ===" -ForegroundColor Yellow
Write-Host "1. Run 'cleanmgr /sagerun:1' for Disk Cleanup (handles Windows.old, etc.)"
Write-Host "2. Run 'Dism.exe /online /Cleanup-Image /StartComponentCleanup' (admin)"
Write-Host "3. Check Settings > System > Storage > Temporary Files for GUI cleanup"
Write-Host "4. Consider running PatchCleaner for orphaned MSI installer files"
```

---

## Summary Table

| # | Target | Typical Size | Safe? | Recreated? | Impact After Delete |
|---|--------|-------------|-------|------------|---------------------|
| 1 | Windows Temp | 100 MB - 2 GB | YES | YES | None |
| 2 | User Temp | 500 MB - 10+ GB | YES | YES | None |
| 3 | WU Download Cache | 1-10 GB | YES | YES | Re-downloads updates |
| 4 | Delivery Optimization | 100 MB - 3 GB | YES | YES | Re-downloads DO content |
| 5 | Recycle Bin | Varies | YES | YES | Permanent deletion |
| 6 | Crash Dumps | 10 MB - 16 GB | YES | NO* | Lose crash diagnostics |
| 7 | WER Reports | 50-500 MB | YES | YES | None |
| 8 | Thumbnail Cache | 10-500 MB | YES | YES | Brief Explorer lag |
| 9 | Shader Caches | 100 MB - 5+ GB | YES | YES | Slower game first-launch |
| 10 | Browser Caches | 500 MB - 10+ GB | YES | YES | Slower page loads initially |
| 11 | Windows Logs | 100 MB - 1 GB | YES | YES | Lose diagnostic history |
| 12 | Font Cache | 10-50 MB | YES | YES | Brief font enumeration lag |
| 13 | DNS Cache | 0 (RAM only) | YES | YES | None (re-resolves) |
| 14 | Windows.old | 20-50+ GB | YES | NO | Lose rollback ability |
| 15 | $PatchCache$ | 50-200 MB | CAUTION | YES | Possible MSI patch issues |
| 16 | Prefetch | 50-500 MB | YES | YES | Slower app first-launch |
| 17 | Icon Cache | 10-100 MB | YES | YES | Brief blank icons |
| 18 | WU Logs | 10-200 MB | YES | YES | Lose WU diagnostics |
| 19 | SwReporter | 2-10 MB | YES | YES (on Chrome update) | None |
| 20 | hiberfil/sys/swap | 10-50+ GB | **NO** | Varies | System instability |

*Crash dump .dmp files are NOT recreated unless a new crash occurs. The directories are recreated.

---

*Last updated: 2026-08-31. Paths verified against Windows 11 24H2.*
