# Windows Features and AppX Package Management

> Research reference for DISM feature management, AppX package cleanup, and
> preventing bloatware reinstalls on Windows 10/11.

---

## Table of Contents

1. [DISM Feature Management](#1-dism-feature-management)
2. [Features Safe to Remove](#2-features-safe-to-remove)
3. [Features NEVER to Remove](#3-features-never-to-remove)
4. [Edition Differences](#4-edition-differences)
5. [AppX Package Management](#5-appx-package-management)
6. [Default Windows 11 AppX Packages](#6-default-windows-11-appx-packages)
7. [Preventing Reinstall](#7-preventing-reinstall)

---

## 1. DISM Feature Management

DISM (Deployment Image Servicing and Management) is the primary tool for
managing optional Windows features. It operates on both online (live OS) and
offline (WIM/VHD) images.

### 1.1 Listing Features

```powershell
# List all optional features with their state
DISM /Online /Get-Features

# Filter for a specific feature
DISM /Online /Get-Features | findstr /i "Telnet"

# Count enabled vs disabled features
DISM /Online /Get-Features | findstr /c:"Enabled" | find /c /v ""
DISM /Online /Get-Features | findstr /c:"Disabled" | find /c /v ""
```

Output format:

```
Feature Name : FeatureName
State : Enabled / Disabled
```

### 1.2 Enabling Features

```powershell
# Enable a feature (includes all parent/child dependencies)
DISM /Online /Enable-Feature /FeatureName:TelnetClient /All /NoRestart

# Enable with verbose output
DISM /Online /Enable-Feature /FeatureName:TelnetClient /All /NoRestart /LogPath:D:\dism.log
```

Flags:
- `/All` -- enables all parent features in the dependency chain
- `/NoRestart` -- suppresses automatic reboot (recommended for scripting)
- `/Source` -- specify an alternate source (SxS folder or WIM) when
  component store is corrupted or limited (Home edition)

### 1.3 Disabling Features

```powershell
# Disable a feature (does NOT remove payload files)
DISM /Online /Disable-Feature /FeatureName:TelnetClient /NoRestart

# Disable AND remove payload files from the component store
DISM /Online /Disable-Feature /FeatureName:TelnetClient /Remove /NoRestart
```

Key distinction:
- Without `/Remove` -- feature is disabled but binaries remain on disk
  (can be re-enabled instantly, no source needed)
- With `/Remove` -- binaries are deleted from WinSxS (saves space but
  re-enabling requires a source or Windows Update download)

### 1.4 PowerShell Equivalents

PowerShell provides cmdlet-based wrappers around DISM that return structured
objects instead of text.

```powershell
# List all optional features
Get-WindowsOptionalFeature -Online | Format-Table FeatureName, State -AutoSize

# Filter for disabled features only
Get-WindowsOptionalFeature -Online | Where-Object State -eq "Disabled" |
    Select-Object FeatureName | Format-Table -AutoSize

# Enable a feature
Enable-WindowsOptionalFeature -Online -FeatureName TelnetClient -All -NoRestart

# Disable a feature (no removal of payload)
Disable-WindowsOptionalFeature -Online -FeatureName TelnetClient -NoRestart

# Disable and remove payload files
Disable-WindowsOptionalFeature -Online -FeatureName TelnetClient -Remove -NoRestart

# Enable with logging
Enable-WindowsOptionalFeature -Online -FeatureName TelnetClient -All -NoRestart -LogPath D:\dism.log -LogLevel WarningsErrorsInfo
```

PowerShell advantages over raw DISM:
- Returns `DISM.Feature` objects you can pipe, filter, and sort
- `-Online` is the default; works with `-Path` for offline images
- Better error handling with `try/catch` and `$ErrorActionPreference`

### 1.5 Practical Patterns

```powershell
# Disable multiple features in one session
$featuresToDisable = @(
    "TelnetClient"
    "TFTPClient"
    "Internet-Explorer-Optional-amd64"
    "MediaPlayback"
    "XPS-Viewer"
    "Simple-TCPIP"
    "FaxServicesClientPackage"
    "SMB1Protocol"
    "MicrosoftWindowsPowerShellV2Root"
)

foreach ($feature in $featuresToDisable) {
    $state = (Get-WindowsOptionalFeature -Online -FeatureName $feature -ErrorAction SilentlyContinue).State
    if ($state -eq "Enabled") {
        Write-Host "Disabling: $feature" -ForegroundColor Yellow
        Disable-WindowsOptionalFeature -Online -FeatureName $feature -Remove -NoRestart -ErrorAction SilentlyContinue
    } else {
        Write-Host "Already disabled: $feature" -ForegroundColor Gray
    }
}

Write-Host "Done. A reboot is recommended." -ForegroundColor Green
```

---

## 2. Features Safe to Remove

The following table lists commonly found optional Windows features and their
safety classification for removal.

**Safety levels:**
- **SAFE** -- No dependency on mainstream workflows; removing is low risk
- **ADVANCED** -- Removal may break specific subsystems; verify your use case
- **DO NOT REMOVE** -- Core OS dependency (see Section 3)

| DISM Feature Name | Friendly Name | Safety | Notes |
|---|---|---|---|
| `Internet-Explorer-Optional-amd64` | Internet Explorer 11 | SAFE | IE is EOL. Edge handles IE-mode for legacy sites. Removing eliminates an attack surface. |
| `MediaPlayback` | Windows Media Player | SAFE | Legacy media player. Modern apps and codecs handle all media types. No dependency from other features. |
| `TelnetClient` | Telnet Client | SAFE | Insecure protocol (plaintext). Use SSH instead. Only needed if actively testing against legacy network equipment. |
| `TFTPClient` | TFTP Client | SAFE | Trivial FTP is rarely used outside PXE/network boot scenarios. Remove unless doing firmware flashing or network installs. |
| `XPS-Viewer` | XPS Viewer | SAFE | XPS is a dead format. PDF is universal. Viewer has had past vulnerabilities. Safe to remove. |
| `Printing-PrintToPDFServices` | Print to PDF | SAFE | Built-in PDF printer. Safe to remove if you have a third-party PDF printer (Adobe, Foxit). |
| `SMB1Protocol` | SMBv1 File Sharing | SAFE | **Security risk.** SMBv1 was the vector for WannaCry/NotPetya. Only needed for very old NAS devices (pre-2015). Remove unless absolutely required. |
| `Simple-TCPIP` | Simple TCP/IP Services | SAFE | Echo, discard, quote of the day, daytime, chargen services. Legacy diagnostic/debugging tools with no modern use. |
| `FaxServicesClientPackage` | Fax Services | SAFE | Fax functionality. Almost nobody faxes from a PC anymore. Remove unless connected to a physical fax modem. |
| `IIS-WebServer` | IIS Web Server | SAFE (if not dev) | Internet Information Services. Remove if you are not developing/hosting web applications locally. Safe for non-developer machines. |
| `IIS-WebServerManagementTools` | IIS Management Tools | SAFE (if not dev) | GUI and script management for IIS. Paired with IIS removal above. |
| `MicrosoftWindowsPowerShellV2Root` | PowerShell 2.0 Engine | SAFE | **Security risk.** PS 2.0 lacks modern security features (constrained language mode, AMSI, script block logging). Some legacy scripts depend on it but this is rare. Remove unless you have known PS 2.0 dependencies. |
| `MicrosoftWindowsPowerShellV2` | PowerShell 2.0 (Full) | SAFE | The full PS 2.0 package. Removing the root above removes this too. |
| `Client-ProjFS` | Windows Projected File System | SAFE | Used by Dev Drive in some configurations. Safe to remove unless you use projected file system features. |
| `Hyper-V` | Hyper-V Hypervisor | ADVANCED | **Breaks WSL2 and Docker Desktop** if they rely on Hyper-V backend. Also required for Credential Guard and Device Guard in some configs. Safe to remove if you exclusively use WSL2 with WSL backend or do not use virtualization. |
| `Microsoft-Windows-Subsystem-Linux` | Windows Subsystem for Linux | ADVANCED | Removing WSL breaks all Linux distributions installed via WSL. Note: on Windows 11, the newer "Windows Subsystem for Linux" package is an AppX package, not a DISM feature. This DISM feature is the legacy version. |
| `VirtualMachinePlatform` | Virtual Machine Platform | ADVANCED | Required for WSL2 on Windows 11 (along with the WSL optional feature). Also used by some Android emulation tools. Remove only if you are certain no virtualization subsystem depends on it. |
| `Containers` | Windows Containers | ADVANCED | Needed for Windows container support (Docker Windows containers). Remove if you only use Linux containers. |
| `Microsoft-Hyper-V-All` | Hyper-V (All Components) | ADVANCED | Meta-feature that enables Hyper-V Management Tools, Platform, and HyperVisor Platform. Same caveats as Hyper-V above. |

### Batch Disable Script (Safe Tier Only)

```powershell
# Safe-to-remove features only
$safeFeatures = @(
    "Internet-Explorer-Optional-amd64"
    "MediaPlayback"
    "TelnetClient"
    "TFTPClient"
    "XPS-Viewer"
    "Printing-PrintToPDFServices"
    "SMB1Protocol"
    "Simple-TCPIP"
    "FaxServicesClientPackage"
    "MicrosoftWindowsPowerShellV2Root"
)

Write-Host "=== Safe Feature Removal ===" -ForegroundColor Cyan

foreach ($feature in $safeFeatures) {
    try {
        $info = Get-WindowsOptionalFeature -Online -FeatureName $feature -ErrorAction Stop
        if ($info.State -eq "Enabled") {
            Write-Host "[REMOVE] $feature" -ForegroundColor Yellow
            Disable-WindowsOptionalFeature -Online -FeatureName $feature `
                -Remove -NoRestart -ErrorAction Stop | Out-Null
            Write-Host "  -> Disabled and payload removed." -ForegroundColor Green
        } else {
            Write-Host "[SKIP]   $feature (already disabled)" -ForegroundColor DarkGray
        }
    } catch {
        Write-Host "[ERROR]  $feature -- $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`nSafe feature removal complete. Reboot recommended." -ForegroundColor Cyan
```

---

## 3. Features NEVER to Remove

These features are either core OS components or removing them causes
cascading failures. **Do not disable these features even if they appear
in the DISM feature list.**

| Feature Name | Why You Must Not Remove It |
|---|---|
| `BitLocker` / `BitLocker-Utilities` | BitLocker depends on the Trusted Platform Module (TPM) subsystem and is tied to boot integrity. Disabling the feature breaks recovery key management and can leave drives in an unrecoverable encrypted state. If you do not want BitLocker, simply do not enable encryption -- do not remove the feature. |
| `Microsoft-Windows-Client-LanguagePack` | Core OS language support. Removing causes display issues, broken UWP apps, and Start Menu failures. |
| `Fonts-Extended` / `Fonts-Core` | System font packages. Removing causes rendering failures across the OS and applications. |
| `Windows-Defender-DefaultDefinitions` | Windows Defender definition infrastructure. Removing breaks real-time protection and Windows Security UI. |
| `Microsoft-Windows-NetFx3` | .NET Framework 3.5. Thousands of applications (including Windows components) depend on this. Do not remove. |
| `Microsoft-Windows-NetFx4` | .NET Framework 4.x. The runtime for virtually all modern Windows applications. |
| `Recall-Bootstrapper` / `Recall-*` | Windows Recall components (Copilot+ PCs). Removing breaks system integrity checks even if you do not use Recall. |
| `Windows-Hello-Face` / `Windows-Hello-Fingerprint` | Biometric authentication infrastructure. Removing disables fingerprint/face sign-in and can cause lock screen issues. |
| `OpenSSH-*` | OpenSSH client/server (built into Windows 11). Removing breaks SSH from command line if you rely on it. |
| `Printing-PrintService` | Core printing subsystem. Removing disables ALL printing, including Print to PDF and virtual printers. |
| `MediaFoundation` | Media foundation framework. Removing breaks media playback across the OS, including browsers. |

### Hard Rule

If you are unsure whether a feature is safe to remove, **do not remove it**.
The disk space savings from most features are negligible (often under 100 MB)
and are not worth the risk of breaking OS functionality.

---

## 4. Edition Differences

Windows 10/11 feature availability varies significantly by edition. Features
that are present in the DISM list on one edition may be absent or
non-functional on another.

### 4.1 Feature Availability Matrix

| Feature | Home | Pro | Enterprise | Education | Notes |
|---|---|---|---|---|---|
| Hyper-V | No | Yes | Yes | Yes | Home edition does not include Hyper-V at all. Not present in DISM list. |
| BitLocker Drive Encryption | No | Yes | Yes | Yes | Home edition lacks full BitLocker. "Device Encryption" is a limited subset available on supported hardware. |
| Windows Sandbox | No | Yes | Yes | Yes | Isolated desktop environment for running untrusted software. Not available on Home. |
| Remote Desktop (Host) | No | Yes (1 connection) | Yes (unlimited) | Yes | Home can be an RDP *client* but cannot *host* RDP sessions. |
| Group Policy Editor (gpedit.msc) | No | Yes | Yes | Yes | Home edition lacks the MMC snap-in. Registry edits must be done manually or via scripts. |
| Hyper-V Isolated Containers | No | Yes | Yes | Yes | Windows container isolation modes require Hyper-V support. |
| Credential Guard | No | No* | Yes | Yes | Hardware-based isolation for credentials. *Some Pro configurations with VBS enabled. |
| Device Guard | No | No | Yes | Yes | Code integrity policies for enterprise security. |
| AppLocker | No | No | Yes | Yes | Application control policies. Pro has basic WDAC support. |
| DirectAccess | No | No | Yes | Yes | Always-on VPN replacement. Enterprise only. |
| BranchCache | No | No | Yes | Yes | WAN optimization for branch offices. Enterprise only. |
| Windows To Go | No | No | Yes (deprecated) | Yes (deprecated) | Bootable Windows on USB. Removed in Windows 11. |
| Assigned Access (Kiosk Mode) | Limited | Yes | Yes | Yes | Home has basic single-app kiosk. Pro/Enterprise have full multi-app kiosk. |
| Dynamic Provisioning | No | Yes | Yes | Yes | Bulk device provisioning via provisioning packages. |
| WSUS Support | No | Yes | Yes | Yes | Windows Server Update Services integration. Home uses Windows Update only. |
| SMB Direct (RDMA) | Yes | Yes | Yes | Yes | High-performance SMB over RDMA NICs. All editions. |
| WSL 1 | Yes | Yes | Yes | Yes | Legacy WSL (syscall translation). Available on all editions. |
| WSL 2 | Yes | Yes | Yes | Yes | WSL with real Linux kernel. Available on all editions with appropriate hardware. |
| Delivery Optimization | Yes | Yes | Yes | Yes | P2P Windows Update distribution. All editions. |
| Tamper Protection | Yes | Yes | Yes | Yes | Protects Windows Security settings. All editions. |
| Microsoft Defender Firewall | Yes | Yes | Yes | Yes | Built-in firewall. All editions. |

### 4.2 Feature Name Differences

Some features have slightly different DISM names between editions. Always
verify the exact feature name on your system before scripting.

```powershell
# Check if a feature exists before attempting to modify it
$featureName = "Containers"
$feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction SilentlyContinue

if ($null -eq $feature) {
    Write-Host "Feature '$featureName' not found on this edition." -ForegroundColor Red
} else {
    Write-Host "Feature '$featureName' state: $($feature.State)" -ForegroundColor Green
}
```

### 4.3 Home Edition Workarounds

For features not available on Home edition, there are limited workarounds:

- **Hyper-V**: Use VirtualBox, VMware Workstation Player, or WSL2 (which
  uses its own lightweight hypervisor on Home)
- **Group Policy**: Use registry edits directly, or third-party tools like
  `gpedit-portable`
- **BitLocker**: Use VeraCrypt or BitLocker To Go for external drives
  (limited); Device Encryption may be available on supported hardware via
  Microsoft Account login
- **Sandbox**: Use Sandboxie-Plus (free, open source) as an alternative

---

## 5. AppX Package Management

AppX packages are the modern UWP (Universal Windows Platform) app format.
Windows ships with many pre-installed AppX packages (bloatware), and managing
them requires a different approach than traditional Win32 programs.

### 5.1 Listing Packages

```powershell
# List all installed AppX packages for the current user
Get-AppxPackage | Format-Table Name, Version, InstallLocation -AutoSize

# List packages for ALL users (requires admin)
Get-AppxPackage -AllUsers | Format-Table Name, Version, InstallLocation -AutoSize

# Search for a specific package
Get-AppxPackage | Where-Object Name -like "*Solitaire*"

# List provisioned packages (templates for new user profiles)
Get-AppxProvisionedPackage -Online | Format-Table DisplayName, PackageName -AutoSize

# Count packages
(Get-AppxPackage).Count
(Get-AppxPackage -AllUsers).Count
```

### 5.2 Removing Packages

```powershell
# Remove for current user only
Remove-AppxPackage -Package "Microsoft.BingWeather_4.53.6201.0_x64__8wekyb3d8bbwe"

# Remove for ALL users (admin required)
Remove-AppxPackage -Package "Microsoft.BingWeather_4.53.6201.0_x64__8wekyb3d8bbwe" -AllUsers

# Remove provisioned package (prevents reinstall for new users)
Remove-AppxProvisionedPackage -Online -PackageName "Microsoft.BingWeather_4.53.6201.0_x64__8wekyb3d8bbwe"
```

**Important**: You must remove BOTH the installed package AND the provisioned
package. Removing only the installed package means it will reappear when a
new user profile is created.

### 5.3 Bulk Removal Script

```powershell
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Removes common bloatware AppX packages from Windows 10/11.
.DESCRIPTION
    Removes both installed and provisioned packages for all users.
    Creates a restore manifest before removal.
#>

# --- Packages to Remove ---
$bloatware = @(
    # Bing / Search
    "Microsoft.BingWeather"
    "Microsoft.BingNews"
    "Microsoft.BingFinance"
    "Microsoft.BingSports"
    "Microsoft.BingWallpaper"

    # Communication (non-essential)
    "Microsoft.SkypeApp"
    "Microsoft.GetHelp"
    "Microsoft.Getstarted"

    # Entertainment
    "Microsoft.MicrosoftSolitaireCollection"
    "Microsoft.MicrosoftOfficeHub"       # Office web shortcuts
    "Microsoft.Office.OneNote"

    # Xbox (keep if you game on PC)
    "Microsoft.XboxApp"
    "Microsoft.XboxGamingOverlay"
    "Microsoft.XboxGameOverlay"
    "Microsoft.XboxIdentityProvider"
    "Microsoft.XboxSpeechToTextOverlay"
    "Microsoft.Xbox.TCUI"
    "Microsoft.GamingApp"

    # Social
    "Microsoft.YourPhone"
    "Microsoft.People"
    "Microsoft.WindowsCommunicationsApps"  # Mail & Calendar

    # Third-party bloatware
    "king.com.CandyCrushSaga"
    "king.com.CandyCrushSodaSaga"
    "Disney.37853FC22B2CE"
    "SpotifyAB.SpotifyMusic"

    # Other Microsoft apps
    "Microsoft.WindowsMaps"
    "Microsoft.MicrosoftStickyNotes"
    "Microsoft.WindowsFeedbackHub"
    "Microsoft.PowerAutomateDesktop"
    "Microsoft.Todos"
    "Microsoft.549981C3F5F10"              # Cortana
    "Microsoft.ZuneVideo"
    "Microsoft.ZuneMusic"
    "Clipchamp.Clipchamp"
    "Microsoft Teams"                       # New Teams (if pre-installed)

    # News and widgets
    "Microsoft.Start.1410800030"            # News and Interests / Start
    "Microsoft.WindowsAlarms"
)

# --- Backup current state ---
$backupPath = "$env:USERPROFILE\Desktop\AppxBackup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
New-Item -Path $backupPath -ItemType Directory -Force | Out-Null

# Save list of currently installed packages
Get-AppxPackage -AllUsers | Select-Object Name, Version, PackageFullName |
    Export-Csv -Path "$backupPath\installed_packages.csv" -NoTypeInformation

Write-Host "Backup saved to: $backupPath" -ForegroundColor Cyan
Write-Host ""

# --- Remove installed packages ---
$removed = 0
$skipped = 0
$failed = 0

foreach ($package in $bloatware) {
    $apps = Get-AppxPackage -AllUsers -Name "*$package*" -ErrorAction SilentlyContinue
    foreach ($app in $apps) {
        Write-Host "[REMOVE] $($app.Name)" -ForegroundColor Yellow
        try {
            Remove-AppxPackage -Package $app.PackageFullName -AllUsers -ErrorAction Stop
            $removed++
            Write-Host "  -> Removed." -ForegroundColor Green
        } catch {
            Write-Host "  -> Failed: $($_.Exception.Message)" -ForegroundColor Red
            $failed++
        }
    }

    if (-not $apps) {
        Write-Host "[SKIP]   $package (not installed)" -ForegroundColor DarkGray
        $skipped++
    }
}

# --- Remove provisioned packages ---
Write-Host "`n=== Removing Provisioned Packages ===" -ForegroundColor Cyan

foreach ($package in $bloatware) {
    $provisioned = Get-AppxProvisionedPackage -Online |
        Where-Object DisplayName -like "*$package*" -ErrorAction SilentlyContinue
    foreach ($prov in $provisioned) {
        Write-Host "[REMOVE-PROV] $($prov.DisplayName)" -ForegroundColor Yellow
        try {
            Remove-AppxProvisionedPackage -Online -PackageName $prov.PackageName -ErrorAction Stop
            Write-Host "  -> Provisioned package removed." -ForegroundColor Green
        } catch {
            Write-Host "  -> Failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

# --- Summary ---
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Removed:     $removed" -ForegroundColor Green
Write-Host "Skipped:     $skipped" -ForegroundColor Gray
Write-Host "Failed:      $failed" -ForegroundColor Red
Write-Host "`nA reboot is recommended to complete cleanup." -ForegroundColor White
```

### 5.4 Removing a Package by Partial Name Match

```powershell
# Remove all packages matching a partial name
Get-AppxPackage -AllUsers -Name "*Xbox*" | Remove-AppxPackage -AllUsers
Get-AppxProvisionedPackage -Online |
    Where-Object DisplayName -like "*Xbox*" |
    ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName }
```

### 5.5 Reinstallation Methods

If you remove a package and need it back later:

```powershell
# Method 1: Reinstall from Microsoft Store (open store, search, install)

# Method 2: Reinstall via AppX manifest (if you have the .appx file)
Add-AppxPackage -Path "C:\path\to\package.appx"

# Method 3: Reinstall provisioned + install for user
Add-AppxProvisionedPackage -Online -Package "C:\path\to\package.appx" -SkipLicense
Add-AppxPackage -Path "C:\path\to\package.appx"

# Method 4: Restore all default provisioned packages via DISM
DISM /Online /Add-Capability /CapabilityName:App.Experience~~~~0.0.1.0

# Method 5: Reset the Microsoft Store cache and reinstall
wsreset.exe
# Then open Store and search for the app

# Method 6: PowerShell - install from Store by package family name
# This requires the Store app to be functional
$packageFamilyName = "Microsoft.WindowsCalculator_8wekyb3d8bbwe"
Start-Process "ms-windows-store://pdp/?productid=$packageFamilyName"
```

### 5.6 Finding Package Details

```powershell
# Get full details for a package
Get-AppxPackage -Name "*Calculator*" | Select-Object *

# Get the package family name (needed for some reinstalls)
Get-AppxPackage -Name "*Calculator*" | Select-Object Name, PackageFamilyName, PackageFullName

# List all package dependencies
Get-AppxPackage -Name "*Calculator*" | ForEach-Object {
    $_.Dependencies | Select-Object Name
}

# Find the install location to check package contents
(Get-AppxPackage -Name "*Calculator*").InstallLocation
```

---

## 6. Default Windows 11 AppX Packages

This table covers the default AppX packages shipped with a clean Windows 11
installation (build 22H2/23H2/24H2). Package names are the prefix before
the version number.

### 6.1 Microsoft First-Party Apps

| Package Name | What It Is | Safe to Remove? | Notes |
|---|---|---|---|
| `Microsoft.WindowsStore` | Microsoft Store | **DO NOT REMOVE** | Core app distribution platform. Removing breaks ability to install/update Store apps and some system components. |
| `Microsoft.WindowsCalculator` | Calculator | **DO NOT REMOVE** | Simple, clean calculator. Removing provides zero benefit and breaks Start Menu search integration. |
| `Microsoft.WindowsTerminal` | Windows Terminal | **DO NOT REMOVE** | Modern terminal emulator. Removing loses tabs, profiles, and integrated PowerShell/CMD experience. |
| `Microsoft.WindowsNotepad` | Notepad | **DO NOT REMOVE** | Core text editor. Removing breaks `.txt` file association and right-click "Edit" in Explorer. |
| `Microsoft.Windows.Photos` | Photos | **DO NOT REMOVE** | Default image viewer. Removing breaks image file associations. |
| `Microsoft.WindowsCamera` | Camera | SAFE | UWP camera app. Remove if you use a third-party camera app or have no camera. |
| `Microsoft.WindowsAlarms` | Alarms & Clock | SAFE | World clock, alarm, timer, stopwatch. Remove if you use alternatives. |
| `Microsoft.WindowsMaps` | Maps | SAFE | Offline-capable maps. Most users rely on Google/Apple Maps. Safe to remove. |
| `Microsoft.WindowsSoundRecorder` | Voice Recorder | SAFE | Basic audio recorder. Remove if you use Audacity or other tools. |
| `Microsoft.BingWeather` | Bing Weather | SAFE | Weather app with Bing data. Remove if you use other weather apps. |
| `Microsoft.BingNews` | Bing News | SAFE | News aggregator. Remove if you do not use it. |
| `Microsoft.BingFinance` | Bing Finance | SAFE | Stock/finance tracker. Remove if not needed. |
| `Microsoft.BingSports` | Bing Sports | SAFE | Sports scores and news. Remove if not needed. |
| `Microsoft.BingWallpaper` | Bing Wallpaper | SAFE | Daily Bing wallpapers on lock screen/desktop. Remove if not needed. |
| `Microsoft.MicrosoftSolitaireCollection` | Solitaire Collection | SAFE | Card games (Klondike, Spider, FreeCell, etc.). Safe to remove. |
| `Microsoft.MicrosoftOfficeHub` | Office | SAFE | Just shortcuts to Office web apps. Remove if you have Office installed or do not use it. |
| `Microsoft.GetHelp` | Get Help | SAFE | Help articles and support links. Remove if not used. |
| `Microsoft.Getstarted` | Tips | SAFE | Windows tips and tutorials. Remove if not used. |
| `Microsoft.WindowsFeedbackHub` | Feedback Hub | SAFE | Microsoft feedback app. Remove if you do not file feedback. |
| `Microsoft.MicrosoftStickyNotes` | Sticky Notes | SAFE | Digital sticky notes. Remove if you use alternatives. |
| `Microsoft.Todos` | Microsoft To Do | SAFE | Task management app. Remove if you use other task managers. |
| `Microsoft.PowerAutomateDesktop` | Power Automate | SAFE | RPA/automation tool. Remove if not used for workflow automation. |
| `Microsoft.Clipchamp` | Clipchamp | SAFE | Video editor (freemium). Remove if you use other video editors. |
| `Microsoft.WindowsNotepad` | Notepad (new) | **DO NOT REMOVE** | Updated Notepad with tabs. Core OS integration. |
| `Microsoft.Paint` | Paint | **DO NOT REMOVE** | Classic Paint app (updated in Win 11). Core OS component. |
| `Microsoft.ScreenSketch` | Snipping Tool | **DO NOT REMOVE** | Screenshot/screen recording tool. Removing breaks Win+Shift+S shortcut. |
| `Microsoft.SkypeApp` | Skype | SAFE | Legacy communication app. Microsoft is sunsetting Skype. Safe to remove. |
| `Microsoft.YourPhone` | Phone Link | SAFE | Link Android/iPhone to PC. Remove if you do not use cross-device features. |
| `Microsoft.People` | People | SAFE | Contact management. Remove if you manage contacts elsewhere. |
| `Microsoft.WindowsCommunicationsApps` | Mail and Calendar | SAFE | UWP mail/calendar client. Remove if you use Outlook, Thunderbird, or webmail. |
| `Microsoft.ZuneVideo` | Movies & TV | SAFE | Media playback app. Remove if you use VLC or other players. |
| `Microsoft.ZuneMusic` | Groove Music | SAFE | Music player. Remove if you use Spotify, iTunes, etc. |
| `Microsoft.549981C3F5F10` | Cortana | SAFE | Cortana assistant (mostly deprecated in Win 11). Safe to remove. |
| `Microsoft.WindowsMaps` | Windows Maps | SAFE | UWP maps app. Remove if not used. |

### 6.2 Xbox Packages

| Package Name | What It Is | Safe to Remove? | Notes |
|---|---|---|---|
| `Microsoft.XboxApp` | Xbox App | SAFE | Main Xbox app for game library, social, and Game Pass. **Remove only if you do not game on PC.** |
| `Microsoft.XboxGamingOverlay` | Game Bar | SAFE | In-game overlay (Win+G). Remove if you do not use Game Bar for recording/screen capture. |
| `Microsoft.XboxGameOverlay` | Game Overlay | SAFE | Companion to Game Bar. Remove with XboxGamingOverlay. |
| `Microsoft.XboxIdentityProvider` | Xbox Identity | ADVANCED | Xbox authentication provider. Removing may break Microsoft Store sign-in for some users. Test carefully. |
| `Microsoft.XboxSpeechToTextOverlay` | Xbox Speech-to-Text | SAFE | In-game speech-to-text. Remove if not using voice features. |
| `Microsoft.Xbox.TCUI` | Xbox TCUI | ADVANCED | Xbox Title Call UI. Some games may not display overlays or friend lists correctly without it. |
| `Microsoft.GamingApp` | Gaming Services | ADVANCED | Core gaming services. Removing breaks Game Pass, Game Bar recording, and some game launches. Only remove if you do not game on Windows at all. |

### 6.3 Third-Party Bloatware

These are third-party apps pre-installed by OEMs or Windows via promotional
agreements. They are universally safe to remove.

| Package Name | What It Is | Safe to Remove? | Notes |
|---|---|---|---|
| `king.com.CandyCrushSaga` | Candy Crush Saga | SAFE | Game promoted by Microsoft. No functional purpose. Remove. |
| `king.com.CandyCrushSodaSaga` | Candy Crush Soda Saga | SAFE | Another king.com game. Remove. |
| `king.com.CandyCrushFriendsSaga` | Candy Crush Friends Saga | SAFE | Yet another king.com game. Remove. |
| `king.com.BubbleWitch3Saga` | Bubble Witch 3 Saga | SAFE | king.com puzzle game. Remove. |
| `king.com.PetRescueSaga` | Pet Rescue Saga | SAFE | king.com puzzle game. Remove. |
| `Disney.37853FC22B2CE` | Disney+ | SAFE | Streaming app shortcut. Remove if you do not use Disney+. |
| `SpotifyAB.SpotifyMusic` | Spotify | SAFE | Music streaming app. Remove if you do not use Spotify or prefer the desktop Win32 version. |
| `BytedancePte.Ltd.TikTok` | TikTok | SAFE | Social media app. Remove. |
| `Facebook.310192332562` | Facebook | SAFE | Social media app. Remove. |
| `WhatsApp` | WhatsApp | SAFE | Messaging app. Remove if not used. |
| `Adobe SystemsIncorporated.AdobeCreativeCloudExpress` | Adobe Express | SAFE | Creative/design tool. Remove if not used. |
| `Clipchamp.Clipchamp` | Clipchamp | SAFE | Microsoft-acquired video editor. Remove if you have a preferred video editor. |

### 6.4 System Packages (Do Not Remove)

These packages are integral to Windows functionality and must not be removed.

| Package Name | What It Is | Safe to Remove? | Notes |
|---|---|---|---|
| `Microsoft.NET.Native.Framework` | .NET Native Runtime | **DO NOT REMOVE** | Required by UWP apps. Removing breaks most Store apps. |
| `Microsoft.NET.Native.Runtime` | .NET Native Runtime | **DO NOT REMOVE** | Required by UWP apps. |
| `Microsoft.UI.Xaml` | WinUI 2.x | **DO NOT REMOVE** | UI framework used by modern Windows apps. |
| `Microsoft.VCLibs` | Visual C++ Runtime | **DO NOT REMOVE** | Required by many apps. |
| `Microsoft.WindowsAppRuntime` | Windows App SDK | **DO NOT REMOVE** | WinUI 3 / Windows App SDK runtime. |
| `Microsoft.AAD.BrokerPlugin` | Azure AD Broker | **DO NOT REMOVE** | Required for Microsoft 365 and enterprise sign-in. |
| `Microsoft.AccountsControl` | Accounts Control | **DO NOT REMOVE** | Microsoft account sign-in UI. |
| `Microsoft.Windows.CloudExperienceHost` | Cloud Experience | **DO NOT REMOVE** | OOBE and cloud account setup. |
| `Microsoft.Windows.Search` | Search | **DO NOT REMOVE** | Windows Search indexing and UI. |
| `Microsoft.Windows.ShellExperienceHost` | Shell Experience | **DO NOT REMOVE** | Start Menu, Taskbar, Action Center. |
| `Microsoft.Windows.StartMenuExperienceHost` | Start Menu | **DO NOT REMOVE** | Start Menu host process. |
| `Windows.ClientCBS` | Client CBS | **DO NOT REMOVE** | Component-Based Servicing client. |
| `Microsoft.Windows.OOBENetworkCaptivePortal` | OOBE Network | **DO NOT REMOVE** | Network captive portal detection during setup. |
| `Microsoft.Windows.OOBENetworkConnectionFlow` | OOBE Network Flow | **DO NOT REMOVE** | Network connection during setup. |
| `Microsoft.Windows.PinningConfirmationDialog` | Pin Confirmation | **DO NOT REMOVE** | Pin-to-Taskbar confirmation dialog. |
| `Microsoft.Windows.SecHealthUI` | Windows Security | **DO NOT REMOVE** | Windows Security (Defender) UI. |
| `Microsoft.Windows.XGP` | Xbox Game Pass Core | ADVANCED | Game Pass infrastructure. Only remove if you never use Game Pass. |
| `Microsoft.Xbox.TCUI` | Xbox TCUI | ADVANCED | Some games need this for overlays. |

---

## 7. Preventing Reinstall

Windows has multiple mechanisms that can automatically reinstall removed apps.
These must be disabled to maintain a clean system after AppX removal.

### 7.1 DisableWindowsConsumerFeatures

This registry value controls Windows automatically installing suggested apps
and "recommended" software from the Microsoft Store.

```powershell
# Disable Windows Consumer Features (prevents automatic app installs)
$path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent"

# Create the key if it does not exist
if (-not (Test-Path $path)) {
    New-Item -Path $path -Force | Out-Null
}

# Disable consumer features
Set-ItemProperty -Path $path -Name "DisableWindowsConsumerFeatures" -Value 1 -Type DWord

# Verify
Get-ItemProperty -Path $path -Name "DisableWindowsConsumerFeatures"
```

### 7.2 ContentDeliveryManager Settings

ContentDeliveryManager controls suggested apps, auto-installs, and content
suggestions in the Start Menu, Lock Screen, and elsewhere.

```powershell
# --- Per-user settings (run without elevation) ---
$userPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"

# Disable suggested apps in Start Menu
Set-ItemProperty -Path $userPath -Name "SilentInstalledAppsEnabled" -Value 0 -Type DWord

# Disable Start Menu content suggestions
Set-ItemProperty -Path $userPath -Name "SubscribedContent-338388Enabled" -Value 0 -Type DWord
Set-ItemProperty -Path $userPath -Name "SubscribedContent-310093Enabled" -Value 0 -Type DWord

# Disable lock screen spotlight / tips
Set-ItemProperty -Path $userPath -Name "RotatingLockScreenEnabled" -Value 0 -Type DWord
Set-ItemProperty -Path $userPath -Name "RotatingLockScreenOverlayEnabled" -Value 0 -Type DWord

# Disable Start Menu suggested/bloated tiles
Set-ItemProperty -Path $userPath -Name "SubscribedContent-338389Enabled" -Value 0 -Type DWord
Set-ItemProperty -Path $userPath -Name "SubscribedContent-353694Enabled" -Value 0 -Type DWord
Set-ItemProperty -Path $userPath -Name "SubscribedContent-353696Enabled" -Value 0 -Type DWord

# --- Machine-wide settings (admin required) ---
$machinePath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\ContentDeliveryManager"

if (-not (Test-Path $machinePath)) {
    New-Item -Path $machinePath -Force | Out-Null
}

# Block all automatic app installs via ContentDeliveryManager
Set-ItemProperty -Path $machinePath -Name "RotatingLockScreenEnabled" -Value 0 -Type DWord
Set-ItemProperty -Path $machinePath -Name "RotatingLockScreenOverlayEnabled" -Value 0 -Type DWord
```

### 7.3 DisableSoftLanding

SoftLanding controls the "Get to know your PC" and introductory experience
that can reinstall or re-pin apps.

```powershell
$path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent"

if (-not (Test-Path $path)) {
    New-Item -Path $path -Force | Out-Null
}

# Disable soft landing (introductory experiences and tips)
Set-ItemProperty -Path $path -Name "DisableSoftLanding" -Value 1 -Type DWord
```

### 7.4 Group Policy Settings

For Pro/Enterprise/Education editions, Group Policy provides additional
control over app installation and bloatware.

#### Via Group Policy Editor (gpedit.msc)

```
Computer Configuration
  -> Administrative Templates
    -> Windows Components
      -> Cloud Content
        -> "Turn off Microsoft consumer experiences"          = Enabled
        -> "Do not show Windows tips"                         = Enabled
        -> "Turn off cloud optimized content"                 = Enabled
        -> "Turn off cloud consumer content"                  = Enabled
```

```
Computer Configuration
  -> Administrative Templates
    -> Windows Components
      -> Store
        -> "Turn off the Store application"                   = Enabled  (extreme)
        -> "Disable all apps from Microsoft Store"            = Enabled  (extreme)
```

```powershell
# Via Registry (equivalent to Group Policy)
$gpPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent"
Set-ItemProperty -Path $gpPath -Name "DisableWindowsConsumerFeatures" -Value 1 -Type DWord
Set-ItemProperty -Path $gpPath -Name "DisableSoftLanding" -Value 1 -Type DWord
Set-ItemProperty -Path $gpPath -Name "DisableWindowsSpotlightFeatures" -Value 1 -Type DWord

$storePath = "HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore"
if (-not (Test-Path $storePath)) {
    New-Item -Path $storePath -Force | Out-Null
}
Set-ItemProperty -Path $storePath -Name "AutoDownload" -Value 2 -Type DWord  # 2 = Disable auto-download
Set-ItemProperty -Path $storePath -Name "DisableStoreApps" -Value 0 -Type DWord  # Keep Store functional
```

### 7.5 Complete Prevention Script

```powershell
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Applies all known settings to prevent bloatware reinstall on Windows 11.
.DESCRIPTION
    Disables ContentDeliveryManager, Consumer Features, Soft Landing,
    and applies Group Policy settings via registry.
#>

Write-Host "=== Preventing Bloatware Reinstall ===" -ForegroundColor Cyan

# --- 1. Consumer Features (Machine) ---
$cloudPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent"
if (-not (Test-Path $cloudPath)) {
    New-Item -Path $cloudPath -Force | Out-Null
}

$consumerSettings = @{
    "DisableWindowsConsumerFeatures"      = 1
    "DisableSoftLanding"                  = 1
    "DisableWindowsSpotlightFeatures"     = 1
}

foreach ($name in $consumerSettings.Keys) {
    Set-ItemProperty -Path $cloudPath -Name $name -Value $consumerSettings[$name] -Type DWord
    Write-Host "[SET] $name = $($consumerSettings[$name])" -ForegroundColor Green
}

# --- 2. ContentDeliveryManager (User) ---
$userCDM = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"

$userSettings = @{
    "SilentInstalledAppsEnabled"          = 0
    "SubscribedContent-338388Enabled"     = 0
    "SubscribedContent-310093Enabled"     = 0
    "SubscribedContent-338389Enabled"     = 0
    "SubscribedContent-353694Enabled"     = 0
    "SubscribedContent-353696Enabled"     = 0
    "RotatingLockScreenEnabled"           = 0
    "RotatingLockScreenOverlayEnabled"    = 0
    "SystemPaneSuggestionsEnabled"        = 0
}

foreach ($name in $userSettings.Keys) {
    Set-ItemProperty -Path $userCDM -Name $name -Value $userSettings[$name] -Type DWord
    Write-Host "[SET] $name = $($userSettings[$name])" -ForegroundColor Green
}

# --- 3. Store Auto-Download ---
$storePath = "HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore"
if (-not (Test-Path $storePath)) {
    New-Item -Path $storePath -Force | Out-Null
}

Set-ItemProperty -Path $storePath -Name "AutoDownload" -Value 2 -Type DWord
Write-Host "[SET] Store AutoDownload = 2 (Disabled)" -ForegroundColor Green

# --- 4. Provisioned Package Removal (optional, requires admin) ---
$provisionedBloat = @(
    "Microsoft.BingWeather"
    "Microsoft.BingNews"
    "Microsoft.BingFinance"
    "Microsoft.BingSports"
    "Microsoft.BingWallpaper"
    "Microsoft.SkypeApp"
    "Microsoft.GetHelp"
    "Microsoft.Getstarted"
    "Microsoft.MicrosoftSolitaireCollection"
    "Microsoft.MicrosoftOfficeHub"
    "Microsoft.YourPhone"
    "Microsoft.People"
    "Microsoft.WindowsFeedbackHub"
    "Microsoft.WindowsMaps"
    "Microsoft.ZuneVideo"
    "Microsoft.ZuneMusic"
    "Microsoft.Todos"
    "Microsoft.PowerAutomateDesktop"
    "Microsoft.549981C3F5F10"
    "king.com.CandyCrushSaga"
    "king.com.CandyCrushSodaSaga"
    "Disney.37853FC22B2CE"
    "SpotifyAB.SpotifyMusic"
    "Clipchamp.Clipchamp"
)

Write-Host "`n=== Removing Provisioned Packages ===" -ForegroundColor Cyan

foreach ($package in $provisionedBloat) {
    $provisioned = Get-AppxProvisionedPackage -Online |
        Where-Object DisplayName -like "*$package*" -ErrorAction SilentlyContinue
    foreach ($prov in $provisioned) {
        try {
            Remove-AppxProvisionedPackage -Online -PackageName $prov.PackageName -ErrorAction Stop
            Write-Host "[PROV-REMOVE] $($prov.DisplayName)" -ForegroundColor Green
        } catch {
            Write-Host "[PROV-SKIP] $($prov.DisplayName) -- $($_.Exception.Message)" -ForegroundColor DarkGray
        }
    }
}

Write-Host "`n=== All Prevention Settings Applied ===" -ForegroundColor Cyan
Write-Host "Restart recommended for full effect." -ForegroundColor Yellow
```

### 7.6 Verifying Prevention

```powershell
# Verify consumer features are disabled
$path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent"
$value = Get-ItemProperty -Path $path -Name "DisableWindowsConsumerFeatures" -ErrorAction SilentlyContinue
if ($value.DisableWindowsConsumerFeatures -eq 1) {
    Write-Host "Consumer Features: DISABLED (good)" -ForegroundColor Green
} else {
    Write-Host "Consumer Features: ENABLED (fix needed)" -ForegroundColor Red
}

# Verify ContentDeliveryManager is locked down
$cdmPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
$silent = Get-ItemProperty -Path $cdmPath -Name "SilentInstalledAppsEnabled" -ErrorAction SilentlyContinue
if ($silent.SilentInstalledAppsEnabled -eq 0) {
    Write-Host "Silent App Installs: DISABLED (good)" -ForegroundColor Green
} else {
    Write-Host "Silent App Installs: ENABLED (fix needed)" -ForegroundColor Red
}

# Verify Store auto-download is disabled
$storePath = "HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore"
$autoDl = Get-ItemProperty -Path $storePath -Name "AutoDownload" -ErrorAction SilentlyContinue
if ($autoDl.AutoDownload -eq 2) {
    Write-Host "Store Auto-Download: DISABLED (good)" -ForegroundColor Green
} else {
    Write-Host "Store Auto-Download: ENABLED (fix needed)" -ForegroundColor Red
}
```

---

## Appendix: Quick Reference Commands

| Task | Command |
|---|---|
| List all features | `DISM /Online /Get-Features` |
| Find a feature | `DISM /Online /Get-Features \| findstr /i "Name"` |
| Disable feature | `DISM /Online /Disable-Feature /FeatureName:X /Remove /NoRestart` |
| Enable feature | `DISM /Online /Enable-Feature /FeatureName:X /All /NoRestart` |
| PowerShell list features | `Get-WindowsOptionalFeature -Online \| Format-Table FeatureName, State` |
| List AppX packages | `Get-AppxPackage -AllUsers \| Select Name, Version` |
| Remove AppX package | `Remove-AppxPackage -Package "FullName" -AllUsers` |
| Remove provisioned | `Remove-AppxProvisionedPackage -Online -PackageName "Name"` |
| List provisioned packages | `Get-AppxProvisionedPackage -Online \| Select DisplayName` |
| Prevent consumer features | `Set-ItemProperty "HKLM:\...\CloudContent" DisableWindowsConsumerFeatures 1` |
| Disable silent installs | `Set-ItemProperty "HKCU:\...\ContentDeliveryManager" SilentInstalledAppsEnabled 0` |
