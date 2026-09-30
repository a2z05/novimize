# Windows 11 AppX/MSIX Package Inventory
# Comprehensive Reference Guide
# Generated: 2026-08-31

---

## TABLE OF CONTENTS

1. [How to List and Manage AppX Packages](#part-1-how-to-list-and-manage-appx-packages)
2. [Removal Methods: PowerShell, Group Policy, Registry](#part-2-removal-methods)
3. [Packages That CANNOT Be Safely Removed](#part-3-packages-that-cannot-be-safely-removed)
4. [Complete Package Inventory](#part-4-complete-package-inventory)
5. [Quick Reference: Keep / Optional / Remove](#part-5-quick-reference)

---

# PART 1: HOW TO LIST AND MANAGE APPX PACKAGES

## Listing All Installed AppX Packages (Current User)

```powershell
# List all AppX packages for the current user
Get-AppxPackage | Format-Table Name, PackageFullName, Status -AutoSize

# List with more detail
Get-AppxPackage | Select-Object Name, Version, PackageFullName, InstallLocation, Status | Format-List

# Search for a specific package
Get-AppxPackage *calculator*

# List all packages with their dependencies
Get-AppxPackage | ForEach-Object {
    $deps = (Get-AppxPackage -DependencyPackage $_.PackageFullName).Name
    [PSCustomObject]@{
        Name = $_.Name
        Version = $_.Version
        Dependencies = ($deps -join ', ')
    }
}
```

## Listing Provisioned Packages (installed for all future users)

```powershell
# List all provisioned packages (applied to new user profiles)
Get-AppxProvisionedPackage -Online | Format-Table DisplayName, PackageName, Version -AutoSize
```

## Listing All Packages System-Wide (All Users)

```powershell
# Requires elevation (Run as Administrator)
Get-AppxPackage -AllUsers | Format-Table Name, PackageFullName -AutoSize

# Count of all packages
(Get-AppxPackage -AllUsers).Count
```

---

# PART 2: REMOVAL METHODS

## Method 1: PowerShell - Remove for Current User Only

```powershell
# Remove for current user only (no admin required for user-scoped packages)
Get-AppxPackage *packagename* | Remove-AppxPackage

# Example: Remove Solitaire for current user
Get-AppxPackage *Microsoft.MicrosoftSolitaireCollection* | Remove-AppxPackage

# Remove multiple packages at once
$packages = @(
    "Microsoft.BingNews"
    "Microsoft.BingWeather"
    "Microsoft.MicrosoftSolitaireCollection"
    "Microsoft.People"
    "Microsoft.GetHelp"
    "Microsoft.Getstarted"
    "Microsoft.ZuneVideo"
    "Microsoft.WindowsFeedbackHub"
)
foreach ($pkg in $packages) {
    Get-AppxPackage $pkg | Remove-AppxPackage
}
```

**What this does:**
- Removes the package for the currently logged-in user only
- Does NOT require admin privileges for user-scoped packages
- The package remains available for other users on the same machine
- The provisioned package entry remains (new user accounts will still get it)

## Method 2: PowerShell - Remove for All Users (Requires Admin)

```powershell
# Remove for ALL users (requires Administrator PowerShell)
Get-AppxPackage -AllUsers *packagename* | Remove-AppxPackage -AllUsers

# Example: Remove Solitaire for all users
Get-AppxPackage -AllUsers *Microsoft.MicrosoftSolitaireCollection* | Remove-AppxPackage -AllUsers
```

## Method 3: Remove Provisioned Packages (Prevents Reinstall on New Accounts)

```powershell
# CRITICAL: This removes the provisioned package so new user accounts
# will NOT get this package. Requires Administrator.
Get-AppxProvisionedPackage -Online | Where-Object {$_.PackageName -like "*packagename*"} | Remove-AppxProvisionedPackage -Online

# Example: Remove Solitaire provisioned package
Get-AppxProvisionedPackage -Online | Where-Object {$_.PackageName -like "*MicrosoftSolitaire*"} | Remove-AppxProvisionedPackage -Online

# Remove ALL provisioned bloatware at once (use with caution!)
$bloatware = @(
    "Microsoft.BingNews"
    "Microsoft.BingWeather"
    "Microsoft.BingSearch"
    "Microsoft.MicrosoftSolitaireCollection"
    "Microsoft.People"
    "Microsoft.GetHelp"
    "Microsoft.Getstarted"
    "Microsoft.ZuneVideo"
    "Microsoft.WindowsFeedbackHub"
    "Microsoft.MicrosoftOfficeHub"
    "Microsoft.PowerAutomateDesktop"
    "Microsoft.Clipchamp"
    "Microsoft.549981C3F5F10"
    "Microsoft.YourPhone"
    "Microsoft.WindowsMaps"
    "Microsoft.Todos"
    "Microsoft.WindowsAlarms"
    "Microsoft.WindowsSoundRecorder"
    "Microsoft.WindowsCamera"
)
foreach ($pkg in $bloatware) {
    Get-AppxProvisionedPackage -Online | Where-Object {$_.PackageName -like "*$pkg*"} | Remove-AppxProvisionedPackage -Online
}
```

## Method 4: Group Policy (Enterprise/Education/Pro)

```
Computer Configuration > Administrative Templates >
  Windows Components > Desktop App X Installer

- "Turn off the Store application" - Disables Microsoft Store
- "Disable all apps from Microsoft Store" - Blocks Store apps
- "Allow Microsoft Store to update apps" - Controls auto-updates

Computer Configuration > Administrative Templates >
  Windows Components > Store

- "Turn off the Store application" = Enabled (hides/disables Store)
```

**Limitations:** Group Policy cannot remove individual AppX packages. It can only control Store access and app execution. For per-package control, you need PowerShell or registry edits.

## Method 5: Registry Edits

```reg
; Disable specific UWP apps via registry
; Path: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\Package States

; To prevent Store auto-updates:
[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\WindowsStore]
"AutoDownload"=dword:00000002

; To disable Windows Update delivering Store app updates:
[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\WindowsStore]
"DisableStoreApps"=dword:00000001

; To block specific apps from launching (use Package Family Name):
; This is generally NOT recommended as a primary method
```

**Important:** Registry edits for individual AppX packages are fragile and not officially supported by Microsoft. PowerShell removal is the recommended approach.

## Method 6: Reinstall from Microsoft Store

```powershell
# Reinstall a package from the Microsoft Store
# Method A: Via Store app - search for the app name and click Install

# Method B: Via PowerShell using Store REST API
# (Limited - most consumer apps must be reinstalled through the Store UI)

# Reinstall a provisioned package that was removed
# You would need the .appx or .msix installer file
Add-AppxPackage -Path "C:\path\to\package.appx"

# Reset an app instead of removing it (often fixes issues)
Get-AppxPackage *packagename* | Reset-AppxPackage

# Re-register all built-in apps (fixes broken apps)
Get-AppxPackage -AllUsers | ForEach-Object {
    Add-AppxPackage -DisableDevelopmentMode -Register "$($_.InstallLocation)\AppXManifest.xml" -ErrorAction SilentlyContinue
}
```

---

# PART 3: PACKAGES THAT CANNOT BE SAFELY REMOVED

The following packages are critical to Windows 11 operation and should NEVER be removed:

## CRITICAL SYSTEM PACKAGES (DO NOT REMOVE)

| Package | Why It Cannot Be Removed |
|---------|------------------------|
| Microsoft.Windows.ShellExperienceHost | Start Menu, Taskbar, Action Center |
| Microsoft.Windows.SearchUI | Windows Search functionality |
| Microsoft.Windows.Cortana | Cortana/Search integration (deeply embedded) |
| Microsoft.Windows.Client.CBS | Component-Based Servicing, Windows Update |
| Microsoft.WindowsStore | Microsoft Store (needed for app reinstalls) |
| Microsoft.StorePurchaseApp | Store purchase/licensing infrastructure |
| Microsoft.Windows.Defender | Windows Security integration |
| Microsoft.Windows.SecHealthUI | Windows Security dashboard |
| Microsoft.Windows.OOBENetworkCaptivePortal | Out-of-box network setup |
| Microsoft.Windows.OOBENetworkConnectionFlow | Out-of-box network setup |
| Microsoft.Windows.ContentDeliveryManager | Live Tiles, Lock Screen content, suggestions |
| Microsoft.WindowsAlarms | System clock/alarm service dependencies |
| Windows.PrintDialog | Print subsystem |
| Microsoft.Windows.NarratorQuickStart | Accessibility (Narrator) |
| Microsoft.Windows.AssignedAccessLockApp | Kiosk mode |
| Microsoft.Windows.StartMenuExperienceHost | Start Menu |
| Microsoft.Windows.AppConnectionsFilter | App connectivity |
| Microsoft.Windows.AppResolverUX | App resolution |
| Microsoft.Windows.AuthenticationClient | Auth/SSO |
| Microsoft.Windows.OOBENetworkCaptivePortal | Network captive portal |
| Microsoft.Windows.OOBEWelcomeScreen | OOBE screens |
| Microsoft.Windows.PrintDialog | Printing |
| Microsoft.Windows.SecureAssessmentBrowser | Secure assessment/testing |
| Microsoft.Windows.XGpuEjectDialog | GPU eject dialog |
| Microsoft.Windows.CloudExperienceHost | Cloud sign-in experience |
| Microsoft.Windows.ShellHost | Shell infrastructure |
| Microsoft.Windows.Internal.PlatformUnifiedCamera | Camera infrastructure |
| Microsoft.Windows.VoiceRecorderUltimate | Voice recorder (system audio API) |

## HIGH-RISK PACKAGES (Removal May Break Things)

| Package | Risk |
|---------|------|
| Microsoft.WindowsTerminal | Removing breaks PowerShell/CMD terminal if set as default |
| Microsoft.WindowsNotepad | Removing breaks .txt file associations |
| Microsoft.Windows.Photos | Removing breaks image file associations |
| Microsoft.WindowsCalculator | Removing breaks calculator: URI protocol |
| Microsoft.WindowsSearch | Core search indexing |
| Microsoft.DesktopAppInstaller | Winget / sideloading / app installation |
| Microsoft.Windows.AIFree | Copilot integration in Windows Shell |
| Microsoft.Copilot | Copilot (varies by Windows build) |
| Microsoft.Windows.StartMenuExperienceHost | Start Menu will break |
| Microsoft.Windows.ShellExperienceHost | Taskbar/Action Center will break |

---

# PART 4: COMPLETE PACKAGE INVENTORY

---

## 1. Microsoft.WindowsCalculator

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsCalculator |
| **Display Name** | Calculator |
| **What It Is** | The built-in Windows Calculator app. Includes standard, scientific, programmer, date calculation, and converter modes. |
| **Who Uses It / Dependencies** | Standalone. No other packages depend on it. The `calculator:` URI scheme may be used by some apps and scripts. |
| **What Breaks If Removed** | Nothing critical. The `calculator:` URI protocol handler is removed. Some enterprise scripts that invoke the calculator via URI will fail. Third-party calculator alternatives work fine. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **OPTIONAL** -- Safe to remove. Most users don't need it if they use a third-party calculator or web-based calculator. |

---

## 2. Microsoft.WindowsCamera

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsCamera |
| **Display Name** | Camera |
| **What It Is** | Built-in UWP camera app for taking photos and recording videos using the device's webcam or built-in camera. |
| **Who Uses It / Dependencies** | Standalone. Windows Hello may use camera hardware but does not depend on this app. Third-party camera apps (OBS, Zoom, etc.) do not depend on it. |
| **What Breaks If Removed** | Cannot use the built-in camera app. `ms-camera:` URI protocol removed. Windows Hello face recognition is NOT affected (uses its own subsystem). |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **OPTIONAL** -- Safe to remove if you use a third-party camera app or don't use the webcam frequently. |

---

## 3. Microsoft.WindowsAlarms

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsAlarms |
| **Display Name** | Alarms & Clock |
| **What It Is** | Built-in app providing alarms, timers, stopwatches, and world clock functionality. Uses UWP APIs. |
| **Who Uses It / Dependencies** | The Alarm/Timer APIs are used by some system functions. Removing the app removes the alarm/timer UI but the underlying Windows scheduling system is separate. However, some reports indicate removing it can cause issues with Focus Sessions (Clock app integration in Windows 11). |
| **What Breaks If Removed** | Alarms and Timers UI is gone. Focus Sessions (Clock app) may lose alarm functionality. Timer-based reminders in some apps may break. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP** -- Relatively lightweight. The alarm/timer features are useful, and the Focus Sessions integration in Windows 11 depends on it. |

---

## 4. Microsoft.WindowsSoundRecorder

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsSoundRecorder |
| **Display Name** | Voice Recorder |
| **What It Is** | Simple UWP voice recording app using the device microphone. Records, trims, and plays back audio. |
| **Who Uses It / Dependencies** | Standalone. No other packages depend on it. |
| **What Breaks If Removed** | Cannot use the built-in voice recorder. No system functionality is affected. Third-party recording apps work fine. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **OPTIONAL** -- Safe to remove if you use a third-party recording app or don't need voice recording. |

---

## 5. Microsoft.Windows.Photos

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Windows.Photos |
| **Display Name** | Microsoft Photos |
| **What It Is** | Built-in photo viewer and editor. Handles image browsing, basic editing (crop, rotate, filter, markup), video trimming, and photo organization. Manages file associations for common image formats. |
| **Who Uses It / Dependencies** | The Photos app is the default handler for many image formats (JPG, PNG, BMP, TIFF, HEIC, etc.) and the `ms-photos:` URI protocol. The Photos Hub in File Explorer depends on it. Windows Spotlight on lock screen may use it for image display. OneDrive photo backup integration works through it. |
| **What Breaks If Removed** | Double-clicking images does nothing until you set a new default. The Photo Hub in File Explorer breaks. `ms-photos:` URI protocol removed. HEIC/HEIF viewing may require installing additional codecs. OneDrive photo viewing flow is disrupted. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP** -- Unless you use an alternative photo viewer (IrfanView, XnView, etc.) AND have set it as the default handler. Even then, the Photos app integrates well with OneDrive. |

---

## 6. Microsoft.Paint

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Paint |
| **Display Name** | Paint |
| **What It Is** | The classic Microsoft Paint image editor, updated for Windows 11 with a modern UI, layers support, and transparency. Legacy MS Paint (mspaint.exe) also exists as a Win32 app. |
| **Who Uses It / Dependencies** | Standalone. The Win32 `mspaint.exe` is a separate component. Removing the AppX version does NOT remove `mspaint.exe`. Some screenshot workflows paste into Paint. |
| **What Breaks If Removed** | The modern Paint app is gone. `mspaint.exe` (classic Paint) still works. `mspaint:` URI protocol may be affected. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **OPTIONAL** -- Safe to remove. Classic mspaint.exe remains. If you use third-party image editors, this is redundant. |

---

## 7. Microsoft.WindowsNotepad

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsNotepad |
| **Display Name** | Notepad |
| **What It Is** | The modern UWP-based Notepad for Windows 11. Features tabs, dark mode, Bing search integration, auto-save, and improved performance over the classic Win32 Notepad. |
| **Who Uses It / Dependencies** | The UWP Notepad replaced the classic notepad.exe. Removing it may cause `notepad.exe` to revert to a legacy version or become unavailable. Many applications and scripts launch Notepad for text editing. The `notepad:` URI protocol is affected. Right-click "Edit" in some contexts opens Notepad. |
| **What Breaks If Removed** | `.txt` and other text file associations may break. Scripts calling `notepad` may fail. The `notepad:` URI protocol is removed. Some developer workflows that open files in Notepad break. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP** -- Notepad is lightweight and widely referenced by other software and user habits. Removing it causes more inconvenience than space savings. |

---

## 8. Microsoft.WindowsTerminal

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsTerminal |
| **Display Name** | Windows Terminal |
| **What It Is** | The modern terminal emulator for Windows. Supports tabs, panes, GPU-accelerated text rendering, Unicode/UTF-8, custom profiles, themes, and multiple shell integration (PowerShell, CMD, WSL, Azure Cloud Shell). |
| **Who Uses It / Dependencies** | This is the DEFAULT terminal app in Windows 11. If set as the default terminal, removing it means double-clicking .bat files or opening a terminal has no handler. PowerShell and CMD still exist but launching them opens a terminal -- if Windows Terminal is the default, nothing opens. |
| **What Breaks If Removed** | If it was set as the default terminal application, opening CMD, PowerShell, or any terminal-requiring action may fail or fall back to the legacy console host (conhost.exe). WSL users lose their primary terminal interface. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP** -- Windows Terminal is a major improvement over conhost.exe and is the default in Windows 11. Only remove if you have a specific alternative terminal installed AND have changed the default. |

---

## 9. Microsoft.WindowsTerminalPreview

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsTerminalPreview |
| **Display Name** | Windows Terminal Preview |
| **What It Is** | Preview/beta version of Windows Terminal with early access to new features. Runs alongside the stable version. |
| **Who Uses It / Dependencies** | Standalone. Does not affect the stable Windows Terminal. Only developers/enthusiasts who want early features use it. |
| **What Breaks If Removed** | Nothing. It's a separate app from stable Windows Terminal. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Safe to remove for virtually all users. It's a developer preview channel app with no production dependency. |

---

## 10. Microsoft.WindowsMaps

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsMaps |
| **Display Name** | Windows Maps |
| **What It Is** | Built-in UWP maps application powered by Bing Maps. Provides map viewing, directions, traffic info, and location search. |
| **Who Uses It / Dependencies** | The `ms-map:` URI protocol is handled by this app. Some Location-based services and apps that invoke map viewing depend on this URI handler. Cortana/Windows Search may surface directions via this app. |
| **What Breaks If Removed** | `ms-map:` URI protocol stops working. Apps that try to open a location in Maps will fail or prompt for a new default. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Most users use Google Maps, Apple Maps, or browser-based mapping. The app is rarely used and Google Maps in browser covers all needs. |

---

## 11. Microsoft.MicrosoftStickyNotes

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.MicrosoftStickyNotes (also listed as Microsoft.MicrosoftStickyNotes_<version> and Microsoft.Windows.StickyNotes) |
| **Display Name** | Microsoft Sticky Notes |
| **What It Is** | Digital sticky notes app. Notes sync across devices via Microsoft account/OneNote integration. Now merged into the OneNote app on newer Windows 11 builds. |
| **Who Uses It / Dependencies** | In recent Windows 11 builds, Sticky Notes is integrated into OneNote. Removing it may affect the OneNote sidebar/sticky notes feature. Notes are stored in the cloud via Microsoft account. |
| **What Breaks If Removed** | Sticky Notes UI is gone. On newer builds, the sticky notes section of OneNote may be affected. Existing notes remain accessible via OneNote or the web. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **OPTIONAL** -- Safe to remove if you don't use sticky notes. If you use OneNote, test removal first as integration varies by build. |

---

## 12. Microsoft.ScreenSketch

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.ScreenSketch |
| **Display Name** | Snipping Tool |
| **What It Is** | The modern Windows 11 Snipping Tool for screen capture. Supports rectangular, freeform, window, and fullscreen snips, plus screen recording (video capture added in Windows 11 22H2+). |
| **Who Uses It / Dependencies** | The `Win+Shift+S` keyboard shortcut invokes screen snipping (part of Shell infrastructure, not the app itself, but the results open in Snipping Tool). The `ms-screenshot:` URI protocol is handled by this app. Screen recording feature is only in this app. |
| **What Breaks If Removed** | `Win+Shift+S` still captures to clipboard but cannot open the annotation/edit window. Screen recording is lost. `ms-screenshot:` URI protocol removed. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP** -- Very useful for screenshots and now screen recording. Lightweight. Alternatives exist (ShareX, Greenshot) but the integration with Win+Shift+S is seamless. |

---

## 13. Microsoft.ZuneVideo

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.ZuneVideo |
| **Display Name** | Movies & TV (now "Media Player") |
| **What It Is** | Windows' default video player for playing local video files. Supports MP4, MKV, AVI, and other formats. In Windows 11, this has been rebranded/replaced by "Media Player" (Microsoft.ZuneVideo or Microsoft.MediaPlayer depending on build). |
| **Who Uses It / Dependencies** | The default handler for many video file types (.mp4, .mkv, .avi, .mov). Removing it breaks double-click video playback until a new default is set. |
| **What Breaks If Removed** | Double-clicking video files does nothing until you set a new default. Video file associations are lost. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **OPTIONAL** -- Safe to remove if you use VLC, MPC-HC, or another media player AND set it as the default. Otherwise keep for convenient video playback. |

---

## 14. Microsoft.ZuneMusic

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.ZuneMusic |
| **Display Name** | Groove Music / Media Player (Music) |
| **What It Is** | Windows' built-in music player. Originally "Groove Music" for Xbox Music streaming, now rebranded as "Media Player" in Windows 11. Plays local music files and integrates with music libraries. |
| **Who Uses It / Dependencies** | Default handler for audio file types (.mp3, .flac, .m4a, .wma, .wav). Music library integration. |
| **What Breaks If Removed** | Double-clicking music files does nothing until you set a new default. Audio file associations lost. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **OPTIONAL** -- Safe to remove if you use Spotify, foobar2000, or another music player AND set it as the default. |

---

## 15. Microsoft.Clipchamp

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Clipchamp |
| **Display Name** | Clipchamp - Video Editor |
| **What It Is** | Web-based video editor acquired by Microsoft. Provides timeline editing, templates, stock media, text-to-speech, screen recording, and webcam recording. Requires sign-in. Has free and premium (included with Microsoft 365) tiers. |
| **Who Uses It / Dependencies** | Standalone. No system dependencies. Some Windows 11 builds may reference it in the "Create" section of the right-click context menu. |
| **What Breaks If Removed** | Video editing via Clipchamp is unavailable. The "Video clip" option in some contexts may disappear. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free, with optional premium features). |
| **Recommended Action** | **REMOVE** -- Safe to remove for most users. It's essentially a web wrapper. Most users use DaVinci Resolve, Premiere, or other editors if they do video work. |

---

## 16. Microsoft.Xbox.TCUI

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Xbox.TCUI (Title Callable UI) |
| **Display Name** | Xbox TCUI |
| **What It Is** | Xbox Title Callable UI framework. Provides the Xbox overlay UI components used by games for sign-in prompts, friend lists, achievement notifications, and other Xbox Live social features. This is an API/framework, not a user-facing app. |
| **Who Uses It / Dependencies** | ALL Xbox Live-enabled games and apps depend on this. Steam games that use Xbox Live integration, Microsoft Store games, Game Pass games, and any game using the Xbox services API. |
| **What Breaks If Removed** | Xbox Live sign-in prompts fail. Achievement tracking breaks. Friend/party features in games break. Game Pass cloud gaming may fail. Games may crash on launch if they try to initialize Xbox Live UI. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP** -- Critical for gaming. Even Steam gamers often have Xbox Live integration. Removing this breaks games. |

---

## 17. Microsoft.XboxApp

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.XboxApp |
| **Display Name** | Xbox Console Companion |
| **What It Is** | The legacy Xbox Console Companion app for managing Xbox consoles, game DVR, friends, parties, and achievements. Largely superseded by the new Xbox app (Microsoft.GamingApp). |
| **Who Uses It / Dependencies** | Standalone. The new Xbox app has replaced most of its functionality. Game Bar (Microsoft.XboxGamingOverlay) is separate. |
| **What Breaks If Removed** | Very little. Some legacy Xbox console streaming features may be affected. Game DVR via Console Companion is removed (Game Bar handles this separately). |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- This is the legacy Xbox app, superseded by Microsoft.GamingApp. Safe to remove unless you specifically use console companion features. |

---

## 18. Microsoft.XboxGamingOverlay

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.XboxGamingOverlay |
| **Display Name** | Xbox Game Bar |
| **What It Is** | The Xbox Game Bar overlay (Win+G). Provides in-game widgets for performance monitoring (CPU, GPU, RAM, FPS), audio controls, screen capture, screen recording, Xbox Social panel, looking up info, and Spotify integration. |
| **Who Uses It / Dependencies** | Game Bar widgets depend on it. The `Win+G` shortcut opens it. Performance overlay in games uses it. Game DVR recording (Win+Alt+R) uses it. Some games explicitly reference Game Bar APIs. |
| **What Breaks If Removed** | `Win+G` shortcut does nothing. In-game performance overlay is lost. Game DVR recording via Game Bar is lost. Some games may show errors on launch if they try to hook into Game Bar. Spotify Game Bar widget lost. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP or OPTIONAL** -- If you use game recording, performance monitoring, or the social features, keep it. If you use OBS and don't need the overlay, it's safe to remove. Note: some game capture software (OBS, ShadowPlay) works independently. |

---

## 19. Microsoft.XboxIdentityProvider

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.XboxIdentityProvider |
| **Display Name** | Xbox Identity Provider |
| **What It Is** | Authentication provider for Xbox Live services. Handles sign-in, token management, and identity verification for Xbox Live, Game Pass, and Microsoft gaming services. This is a background service/dependency, not a user-facing app. |
| **Who Uses It / Dependencies** | CRITICAL dependency for: Xbox Live games, Game Pass (including Cloud Gaming), Microsoft Store games, Xbox app, Game Bar social features. Any game or app that authenticates with Xbox Live depends on this. |
| **What Breaks If Removed** | Xbox Live authentication fails completely. Game Pass games cannot sign in. Microsoft Store games that require Xbox Live fail. Games may crash at launch. Xbox app cannot authenticate. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP** -- Absolutely critical for Xbox services and gaming. Do not remove. |

---

## 20. Microsoft.XboxSpeechToText

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.XboxSpeechToText |
| **Display Name** | Xbox Speech to Text |
| **What It Is** | Speech-to-text engine for Xbox party chat and in-game voice communication. Converts voice to text for accessibility and chat features. |
| **Who Uses It / Dependencies** | Xbox party chat, in-game text chat from voice, Game Bar voice typing for gaming contexts. |
| **What Breaks If Removed** | Voice-to-text in Xbox parties fails. In-game speech-to-text features are lost. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Only needed if you use voice-to-text in Xbox parties. Most gamers don't use this feature. Safe to remove. |

---

## 21. Microsoft.GamingApp

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.GamingApp |
| **Display Name** | Xbox |
| **What It Is** | The NEW Xbox app for Windows. Central hub for Xbox Game Pass (PC and Cloud), game library management, game installation, social features, and Microsoft Store game browsing. Replaced the old Xbox Console Companion. |
| **Who Uses It / Dependencies** | Xbox Game Pass subscribers, PC gaming via Microsoft ecosystem, game library management, Cloud Gaming. |
| **What Breaks If Removed** | Cannot manage Game Pass library. Cannot browse/install Game Pass games. Cloud Gaming access is affected (may still work via browser). Game library management is gone. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP if you use Game Pass / REMOVE if you don't** -- If you have Game Pass or use the Xbox ecosystem for PC gaming, this is essential. If you only use Steam/Epic/GOG, safe to remove. |

---

## 22. Microsoft.OneDriveSync

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.OneDriveSync (also Microsoft.MicrosoftOneDrive) |
| **Display Name** | OneDrive |
| **What It Is** | Microsoft OneDrive cloud storage integration. Provides file sync, backup, On-Demand files, and Explorer integration. Deeply integrated into Windows 11. |
| **Who Uses It / Dependencies** | File Explorer sidebar integration, "Save to OneDrive" in Office apps, Windows Backup, Photos app cloud sync, PC backup (Documents/Pictures/Desktop), Office 365 apps depend on OneDrive for cloud file access. |
| **What Breaks If Removed** | File Explorer loses OneDrive sidebar entries. Cloud files are no longer accessible from Explorer. Office apps lose "Save to OneDrive" capability. Windows Backup to OneDrive fails. On-Demand (placeholder) files become inaccessible. Your Phone photos sync may break. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store or OneDrive.com installer. |
| **Recommended Action** | **KEEP** -- Unless you absolutely do not use OneDrive. Deeply integrated into Windows 11 and Microsoft 365. Removing requires careful planning if you have files in OneDrive. |

---

## 23. Microsoft.YourPhone

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.YourPhone (now Microsoft.PhoneLink) |
| **Display Name** | Phone Link |
| **What It Is** | Links your Android or iPhone to your PC. Provides phone notifications on PC, SMS/messaging from PC, phone calls from PC, photo access, screen mirroring (Samsung/selected Android), and app usage. |
| **Who Uses It / Dependencies** | Standalone. No system dependencies. Requires the "Link to Windows" app on your phone. |
| **What Breaks If Removed** | Phone-to-PC linking is completely lost. Phone notifications on PC stop. Cannot make calls or send texts from PC. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE if unused** -- If you don't use Phone Link, remove it. It runs background services and uses resources. If you actively use it, keep it. |

---

## 24. Microsoft.GetHelp

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.GetHelp |
| **Display Name** | Get Help |
| **What It Is** | Microsoft's support/troubleshooting app. Provides access to Microsoft support articles, virtual agents, and troubleshooting wizards. Replaced the older "Contact Support" app. |
| **Who Uses It / Dependencies** | Standalone. Windows Settings "Troubleshoot" may link to it for guided troubleshooting. |
| **What Breaks If Removed** | Cannot use the Get Help app for support. Some Windows Settings troubleshooting links may show errors or redirect. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Safe to remove. All Microsoft support is available via web browser. Troubleshooting in Settings still works for core issues. |

---

## 25. Microsoft.Getstarted

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Getstarted |
| **Display Name** | Tips |
| **What It Is** | Windows Tips app that displays tips, tricks, and feature highlights for Windows 11. Runs as a background notification source. |
| **Who Uses It / Dependencies** | Standalone. Background tips service pushes notifications. |
| **What Breaks If Removed** | Tips notifications stop. No functional impact whatsoever. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Pure bloatware. No functionality lost. Tips are available online. |

---

## 26. Microsoft.BingWeather

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.BingWeather |
| **Display Name** | Weather |
| **What It Is** | Built-in weather app showing forecasts, radar maps, severe weather alerts, and weather news. Powered by MSN/Bing Weather. |
| **Who Uses It / Dependencies** | Standalone. The `ms-weather:` URI protocol is handled by it. Windows Widgets weather card may reference it. |
| **What Breaks If Removed** | `ms-weather:` URI protocol removed. Widgets weather card may break or show a different source. Weather app is gone. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE or KEEP** -- Personal preference. If you use a weather website or widget, the standalone app is unnecessary. If you like the radar/severe alerts feature, keep it. |

---

## 27. Microsoft.BingNews

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.BingNews |
| **Display Name** | Microsoft Start (News) |
| **What It Is** | News aggregation app powered by MSN/Microsoft Start. Displays personalized news feed, trending stories, and breaking news. |
| **Who Uses It / Dependencies** | Standalone. Windows Widgets news feed may source from it. The `ms-news:` URI protocol. |
| **What Breaks If Removed** | News app is gone. `ms-news:` URI protocol removed. Widgets news feed may be affected. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Safe to remove for most users. News is available through browsers, RSS readers, and other apps. One of the most commonly removed bloatware items. |

---

## 28. Microsoft.BingSearch / Bing

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.BingSearch (also Microsoft.Bing or Microsoft.Bing_\_8wekyb3d8bbwe) |
| **Display Name** | Bing (Search Widget) |
| **What It Is** | The Bing search app/widget. In Windows 11, this appears as the search widget on the taskbar (in some builds) and integrates with Windows Search to provide web results. |
| **Who Uses It / Dependencies** | Windows Search web results depend on Bing infrastructure. The search widget on taskbar (if enabled). Copilot may use Bing search backend. |
| **What Breaks If Removed** | Search widget on taskbar disappears. Windows Search web results may stop working. Local file search still works. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE or KEEP** -- If you don't use the search widget and prefer local-only search, safe to remove. If you like the taskbar search widget, keep it. Note: some users report it reinstalls via Windows Update. |

---

## 29. Microsoft.MicrosoftSolitaireCollection

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.MicrosoftSolitaireCollection |
| **Display Name** | Microsoft Solitaire Collection |
| **What It Is** | Classic Solitaire card games collection: Klondike, Spider, FreeCell, Pyramid, and TriPeaks. Includes daily challenges, Xbox achievements, and ads. One of the most iconic Windows apps. |
| **Who Uses It / Dependencies** | Standalone. Uses Xbox Live for achievements (depends on Xbox.IdentityProvider). |
| **What Breaks If Removed** | Solitaire games are gone. No system impact. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free, ad-supported). |
| **Recommended Action** | **REMOVE** -- Classic bloatware. Safe to remove. Free alternatives exist if you want card games. |

---

## 30. Microsoft.People

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.People |
| **Display Name** | People |
| **What It Is** | Contacts management app integrated with Microsoft account, Outlook, and Skype. Provides a unified contacts view. |
| **Who Uses It / Dependencies** | Was integrated with Outlook Mail/Calendar UWP apps (now deprecated). Mail and Calendar app referenced it for contact autocomplete. In Windows 11, the Outlook PWA has largely replaced it. |
| **What Breaks If Removed** | People app is gone. Contact autocomplete in legacy Mail app may be affected. In modern Windows 11, impact is minimal. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Largely obsolete in Windows 11 as Outlook PWA handles contacts. Safe to remove. |

---

## 31. Microsoft.WindowsFeedbackHub

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsFeedbackHub |
| **Display Name** | Feedback Hub |
| **What It Is** | Microsoft's feedback and bug reporting app. Used to submit feedback, report bugs, and participate in Windows Insider surveys and quests. |
| **Who Uses It / Dependencies** | Windows Insider Program relies on it for surveys. Microsoft support may direct users to submit feedback through it. |
| **What Breaks If Removed** | Cannot submit feedback to Microsoft via the app. Windows Insider surveys/quests may not work. No functional system impact. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Unless you are a Windows Insider who actively provides feedback. Safe to remove for all other users. |

---

## 32. Microsoft.MicrosoftOfficeHub

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.MicrosoftOfficeHub (also Microsoft.MicrosoftOfficeHub_8wekyb3d8bbwe) |
| **Display Name** | Microsoft 365 (Office) |
| **What It Is** | Office Hub app that serves as a launcher/dashboard for Microsoft 365 apps. Shows recent documents, provides quick access to Word, Excel, PowerPoint, and promotes Microsoft 365 subscriptions. |
| **Who Uses It / Dependencies** | Standalone launcher. Does NOT contain the actual Office apps. References OneDrive for recent documents. |
| **What Breaks If Removed** | The Office launcher/hub is gone. Actual Office apps (if installed) are NOT affected. Recent document list in the hub is lost. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- It's just a launcher/promotional app. Office apps work fine without it. Click the Start Menu or taskbar pin for Office apps instead. |

---

## 33. Microsoft.PowerAutomateDesktop

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.PowerAutomateDesktop |
| **Display Name** | Power Automate |
| **What It Is** | Robotic Process Automation (RPA) tool for creating automated workflows. Can automate repetitive desktop tasks, web scraping, file operations, and integrate with cloud services. Free for personal use; requires license for enterprise. |
| **Who Uses It / Dependencies** | Standalone. No system dependencies. Used by power users and enterprises for workflow automation. |
| **What Breaks If Removed** | All Power Automate desktop flows are inaccessible. Cloud flows (if any) still work via web browser. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Unless you actively use Power Automate. It's preinstalled but most home users never use it. Enterprise users should consult IT policy before removing. |

---

## 34. Microsoft.549981C3F5F10

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.549981C3F5F10 (also known as Cortana) |
| **Display Name** | Cortana |
| **What It Is** | Microsoft's virtual assistant Cortana. In Windows 11 22H2+, Cortana has been deprecated and removed as a default experience. It exists as a standalone app in some builds. In later builds, it's been removed entirely. |
| **Who Uses It / Dependencies** | Was integrated into Windows Search and Shell. In Windows 11 23H2+, the Shell no longer depends on Cortana. Some third-party apps may still reference Cortana APIs. |
| **What Breaks If Removed** | Very little in modern Windows 11. Cortana is already deprecated. Voice activation via "Hey Cortana" stops (if it was still active). |
| **Can It Be Reinstalled** | Limited -- Microsoft has been phasing out Cortana. May not be available in Store in future builds. |
| **Recommended Action** | **REMOVE** -- Cortana is deprecated and being removed by Microsoft anyway. Safe to remove proactively. |

---

## 35. Microsoft.WindowsStore

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsStore (also Microsoft.WindowsStore_8wekyb3d8bbwe) |
| **Display Name** | Microsoft Store |
| **What It Is** | The Microsoft Store app for browsing, downloading, and updating apps, games, and content. Also handles app updates for all AppX packages. |
| **Who Uses It / Dependencies** | CRITICAL for: App updates (all UWP/AppX apps update through it), app reinstallation, Winget integration, app licensing verification, digital purchases. |
| **What Breaks If Removed** | Cannot install or update any Store apps. AppX package updates stop. Licensing verification may fail for some apps. Winget may have reduced functionality. |
| **Can It Be Reinstalled** | Difficult -- you need the Store to reinstall the Store. Can be reinstalled via PowerShell with the package file or via Windows Update repair. |
| **Recommended Action** | **KEEP** -- Absolutely keep the Microsoft Store. It's the mechanism for updating all your other apps. Removing it creates cascading problems. |

---

## 36. Microsoft.StorePurchaseApp

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.StorePurchaseApp |
| **Display Name** | Store Purchase App |
| **What It Is** | Background licensing and purchase verification service for the Microsoft Store. Handles in-app purchases, license checks, and digital rights for Store apps. |
| **Who Uses It / Dependencies** | Any paid app or app with in-app purchases from the Store. Game Pass licensing verification. Microsoft Store itself. |
| **What Breaks If Removed** | Paid app licensing breaks. In-app purchases fail. Game Pass license verification fails. Store may malfunction. |
| **Can It Be Reinstalled** | Yes, via PowerShell/Windows Update. |
| **Recommended Action** | **KEEP** -- Critical system component for Store functionality. Do not remove. |

---

## 37. Microsoft.Windows.AIFree / Microsoft.Copilot / Microsoft.WindowsAI.Copilot

| Field | Details |
|-------|---------|
| **Full Names** | Microsoft.Windows.AIFree, Microsoft.Copilot, Microsoft.WindowsAI.Copilot (varies by build) |
| **Display Name** | Copilot |
| **What It Is** | Microsoft Copilot AI assistant integrated into Windows 11. Provides AI chat, image generation, system control via natural language, and integration with Microsoft 365 Copilot. In Windows 11 24H2+, it's deeply integrated into the taskbar and system. |
| **Who Uses It / Dependencies** | The taskbar Copilot button, Win+C keyboard shortcut, right-click context menu Copilot integration, system settings Copilot integration. Edge browser may depend on Copilot sidebar. |
| **What Breaks If Removed** | Copilot taskbar button disappears. Win+C shortcut does nothing. AI assistant features are gone. Some users report the removal can cause Explorer instability in certain builds. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store or via Windows Update. |
| **Recommended Action** | **OPTIONAL** -- If you don't use Copilot, it's generally safe to remove. However, test stability after removal. In Windows 11 24H2+, Microsoft has made Copilot harder to remove as it's more tightly integrated. |

---

## 38. Microsoft.MicrosoftEdge (and variants)

| Field | Details |
|-------|---------|
| **Full Names** | Microsoft.MicrosoftEdge, Microsoft.MicrosoftEdge.Beta, Microsoft.MicrosoftEdge.Dev, Microsoft.MicrosoftEdgeCanary |
| **Display Name** | Microsoft Edge |
| **What It Is** | Microsoft's Chromium-based web browser. Deeply integrated into Windows 11. Used by Windows Search for web results, Widgets for content, some system components, and various Windows features. |
| **Who Uses It / Dependencies** | CRITICAL dependencies: Windows Search web results, Widgets content, some Settings pages open in Edge, "Learn more" links, Windows Update info pages, Store may use Edge for some operations, MS Protocol Handler. PDF viewing (Edge is default PDF viewer). |
| **What Breaks If Removed** | Web search from Start Menu may break. Widgets stop showing content. PDF viewing loses default handler. Various "Learn more" and support links throughout Windows fail. Some system apps that open URLs may fail. Windows Spotlight may have issues. |
| **Can It Be Reinstalled** | Difficult for the base version. Beta/Dev/Canary can be reinstalled from Microsoft Edge website. The base Edge is maintained via Windows Update. |
| **Recommended Action** | **KEEP (base) / REMOVE (Beta/Dev/Canary)** -- Keep Microsoft.MicrosoftEdge (stable) as it's integrated into Windows. Remove Beta, Dev, and Canary variants if you don't use them -- they're just alternative channels. |

---

## 39. Microsoft.DesktopAppInstaller

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.DesktopAppInstaller |
| **Display Name** | App Installer |
| **What It Is** | The App Installer package, which provides the Windows Package Manager (Winget) command-line tool and the ability to sideload app packages (.appx, .msix, .appxbundle). Also enables the "Install App" experience for sideloaded apps. |
| **Who Uses It / Dependencies** | CRITICAL for: `winget` command-line tool, sideloading apps, installing .appx/.msix packages, developer workflows, `Add-AppxPackage` from local files. |
| **What Breaks If Removed** | `winget` command stops working. Cannot sideload apps. Cannot install .appx/.msix packages from local files. Developer workflows that rely on app deployment break. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **KEEP** -- Essential for app management and developer workflows. Removing it limits your ability to install and manage apps. |

---

## 40. Microsoft.Windows.NarratorQuickStart

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Windows.NarratorQuickStart |
| **Display Name** | Narrator Quick Start |
| **What It Is** | Quick start guide/tutorial for the Narrator screen reader accessibility feature. Provides interactive tutorial for new Narrator users. |
| **Who Uses It / Dependencies** | Narrator (Windows screen reader) users only. No system dependencies. |
| **What Breaks If Removed** | The Narrator tutorial/guide is lost. Narrator itself is unaffected. |
| **Can It Be Reinstalled** | Yes, from Microsoft Store (free). |
| **Recommended Action** | **REMOVE** -- Tutorial content only. Narrator works without it. Safe to remove. |

---

## 41. Microsoft.Windows.OOBENetworkCaptivePortal

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Windows.OOBENetworkCaptivePortal |
| **Display Name** | (No visible name - system component) |
| **What It Is** | Out-of-Box Experience (OOBE) network captive portal detection. Shown during initial Windows setup when the network requires a captive portal login (hotel WiFi, airport WiFi, etc.). |
| **Who Uses It / Dependencies** | Only used during Windows initial setup (OOBE). After setup is complete, this is dormant. |
| **What Breaks If Removed** | Initial Windows setup may have issues detecting captive portals on restricted networks. Post-setup, nothing breaks. |
| **Can It Be Reinstalled** | Via Windows recovery/reset. |
| **Recommended Action** | **KEEP** -- It's a system setup component. Small footprint and only matters during initial setup. |

---

## 42. Microsoft.Windows.OOBENetworkConnectionFlow

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Windows.OOBENetworkConnectionFlow |
| **Display Name** | (No visible name - system component) |
| **What It Is** | OOBE network connection flow component. Manages the network connection step during Windows initial setup. |
| **Who Uses It / Dependencies** | Only used during Windows initial setup. |
| **What Breaks If Removed** | Windows initial setup may fail or skip the network connection step. |
| **Can It Be Reinstalled** | Via Windows recovery/reset. |
| **Recommended Action** | **KEEP** -- System setup component. Keep it. |

---

## 43. Windows.PrintDialog

| Field | Details |
|-------|---------|
| **Full Name** | Windows.PrintDialog |
| **Display Name** | Print Dialog |
| **What It Is** | The Windows print dialog system component. Provides the print UI shown when you press Ctrl+P or select Print in any application. |
| **Who Uses It / Dependencies** | ALL applications that support printing. Every Ctrl+P dialog. Print to PDF functionality. |
| **What Breaks If Removed** | Printing is completely broken across all applications. Ctrl+P does nothing. Print to PDF fails. |
| **Can It Be Reinstalled** | Via Windows recovery/reset. |
| **Recommended Action** | **KEEP** -- Absolutely critical. Never remove. |

---

## 44. Microsoft.Windows.ContentDeliveryManager

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Windows.ContentDeliveryManager |
| **Display Name** | (No visible name - system component) |
| **What It Is** | Manages content delivery across Windows: Start Menu suggestions, Lock Screen Spotlight images, Windows Tips, suggested apps in Start Menu, and content recommendations. |
| **Who Uses It / Dependencies** | Start Menu, Lock Screen, Windows Suggestions, App suggestions. |
| **What Breaks If Removed** | Lock Screen Spotlight stops. Start Menu suggestions stop. App recommendations in Start Menu disappear. These are all "features" many users disable anyway. |
| **Can It Be Reinstalled** | Via Windows recovery/reset. |
| **Recommended Action** | **KEEP or OPTIONAL** -- It's a low-impact component. Many users disable its features via Settings > Personalization, but removing the package entirely is aggressive. Keep if in doubt. |

---

## 45. Microsoft.Client.CBS

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Client.CBS |
| **Display Name** | (No visible name - system component) |
| **What It Is** | Client Component-Based Servicing package. Part of the Windows servicing stack that handles Windows updates, feature updates, and component installation/removal. CBS is fundamental to Windows Update and system maintenance. |
| **Who Uses It / Dependencies** | Windows Update, DISM, System File Checker (sfc), component servicing, Windows features management. |
| **What Breaks If Removed** | Windows Update fails. System file repair tools break. Windows features cannot be added/removed. The servicing stack is compromised. |
| **Can It Be Reinstalled** | Via Windows recovery/reset or in-place upgrade repair. |
| **Recommended Action** | **KEEP** -- Absolute critical system component. Never remove. |

---

## 46. Microsoft.WindowsSearch

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.WindowsSearch |
| **Display Name** | Windows Search |
| **What It Is** | Windows Search indexing service and UI. Provides file search, app search, settings search, web search integration, and the search box in the taskbar. |
| **Who Uses It / Dependencies** | Taskbar search, File Explorer search, Start Menu search, Cortana search, web search integration, Windows Search protocol. |
| **What Breaks If Removed** | ALL search functionality in Windows breaks. Start Menu search, Explorer search, taskbar search -- all gone. |
| **Can It Be Reinstalled** | Via Windows recovery/reset. |
| **Recommended Action** | **KEEP** -- Critical system component. Never remove. |

---

## 47. Additional Notable Packages

### Microsoft.Windows.SecHealthUI (Windows Security)

| Field | Details |
|-------|---------|
| **Display Name** | Windows Security |
| **What It Is** | The Windows Security (Defender) dashboard app. Manages antivirus, firewall, device security, and account protection. |
| **Recommended Action** | **KEEP** -- Critical security component. |

### Microsoft.Windows.StartMenuExperienceHost

| Field | Details |
|-------|---------|
| **Display Name** | Start Menu |
| **What It Is** | The Start Menu host process. The Start Menu literally will not open without this. |
| **Recommended Action** | **KEEP** -- Absolutely critical. Never remove. |

### Microsoft.Windows.ShellExperienceHost

| Field | Details |
|-------|---------|
| **Display Name** | Shell Experience Host |
| **What It Is** | Hosts Taskbar, Action Center, Notification Center, and other shell elements. |
| **Recommended Action** | **KEEP** -- Absolutely critical. Never remove. |

### Microsoft.Windows.AugmentedRealitySurfaceReconstruction

| Field | Details |
|-------|---------|
| **Display Name** | Mixed Reality components |
| **What It Is** | Mixed Reality/VR components for Windows Mixed Reality headsets. |
| **Recommended Action** | **REMOVE if not using VR** -- Safe to remove if you don't own a Mixed Reality headset. |

### Microsoft.WindowsMaps

(Covered above - #10)

### Microsoft.Windows.Photos.BackgroundTask

| Field | Details |
|-------|---------|
| **Display Name** | (Background service for Photos) |
| **What It Is** | Background processing for the Photos app, including auto-enhancement and indexing. |
| **Recommended Action** | **KEEP if keeping Photos / REMOVE if removing Photos** |

### Microsoft.Windows.PPIProjection

| Field | Details |
|-------|---------|
| **Display Name** | Projecting to this PC |
| **What It Is** | Enables "Projecting to this PC" feature (Miracast/WiDi wireless display receiver). |
| **Recommended Action** | **OPTIONAL** -- Remove if you never use wireless display projection. |

### Microsoft.Windows SharingExperience

| Field | Details |
|-------|---------|
| **Display Name** | (Share UI) |
| **What It Is** | The Windows Share UI (Win+S or Share button). Provides the share sheet for sharing files/links to apps and people. |
| **Recommended Action** | **KEEP** -- Useful system feature for sharing. |

### Microsoft.HEIFImageExtension

| Field | Details |
|-------|---------|
| **Display Name** | HEIF Image Extensions |
| **What It Is** | Codec for HEIF/HEIC image format support. |
| **Recommended Action** | **KEEP** -- Needed for iPhone photos (HEIC format). |

### Microsoft.WebMediaExtensions

| Field | Details |
|-------|---------|
| **Display Name** | Web Media Extensions |
| **What It Is** | Adds support for open-source web media formats (OGG, WebM, VP9, AV1) to Windows. |
| **Recommended Action** | **KEEP** -- Enables WebM and other web video formats in Windows Media Player and Edge. |

### Microsoft.WebpImageExtension

| Field | Details |
|-------|---------|
| **Display Name** | WebP Image Extensions |
| **What It Is** | Adds WebP image format support to Windows. |
| **Recommended Action** | **KEEP** -- WebP is widely used on the web. Needed for viewing WebP images. |

### Microsoft.VP9VideoExtensions

| Field | Details |
|-------|---------|
| **Display Name** | VP9 Video Extensions |
| **What It Is** | Hardware-accelerated VP9 video codec. Used by YouTube and many streaming services. |
| **Recommended Action** | **KEEP** -- Enables efficient video playback from YouTube and similar sites. |

### Microsoft.AV1VideoExtensions

| Field | Details |
|-------|---------|
| **Display Name** | AV1 Video Extension |
| **What It Is** | AV1 video codec support for next-generation video compression. |
| **Recommended Action** | **KEEP** -- Future-proof video codec support. |

### Microsoft.RawImageExtension

| Field | Details |
|-------|---------|
| **Display Name** | Raw Image Extension |
| **What It Is** | RAW image format support for camera RAW files (CR2, NEF, ARW, etc.). |
| **Recommended Action** | **KEEP if you shoot RAW photos / REMOVE if you don't** |

### Microsoft.Todos

| Field | Details |
|-------|---------|
| **Full Name** | Microsoft.Todos |
| **Display Name** | Microsoft To Do |
| **What It Is** | Task management app from Microsoft. Syncs across devices via Microsoft account. Successor to Wunderlist. |
| **Who Uses It / Dependencies** | Standalone. Outlook may reference it for task sync. |
| **Recommended Action** | **REMOVE if unused** -- Safe to remove if you don't use it for task management. |

---

# PART 5: QUICK REFERENCE TABLE

## KEEP (Critical / Do Not Remove)

| Package | Reason |
|---------|--------|
| Microsoft.WindowsStore | App installation and updates |
| Microsoft.StorePurchaseApp | App licensing |
| Microsoft.DesktopAppInstaller | Winget / app sideloading |
| Microsoft.Xbox.TCUI | Xbox Live game integration |
| Microsoft.XboxIdentityProvider | Xbox authentication |
| Microsoft.OneDriveSync | Cloud storage / file sync |
| Microsoft.WindowsSearch | All search functionality |
| Microsoft.Windows.StartMenuExperienceHost | Start Menu |
| Microsoft.Windows.ShellExperienceHost | Taskbar / Shell |
| Microsoft.Client.CBS | Windows Update / Servicing |
| Windows.PrintDialog | Printing |
| Microsoft.Windows.NarratorQuickStart | Accessibility |
| Microsoft.Windows.OOBENetworkCaptivePortal | OOBE setup |
| Microsoft.Windows.OOBENetworkConnectionFlow | OOBE setup |
| Microsoft.Windows.ContentDeliveryManager | Content delivery |
| Microsoft.Windows.SecHealthUI | Windows Security |
| Microsoft.WindowsAlarms | Alarm/timer APIs |
| Microsoft.WindowsTerminal | Default terminal |
| Microsoft.WindowsNotepad | Default text editor |
| Microsoft.Windows.Photos | Default image viewer |
| Microsoft.Windows.Photos.BackgroundTask | Photos background |
| Microsoft.WebMediaExtensions | Web media formats |
| Microsoft.WebpImageExtension | WebP support |
| Microsoft.VP9VideoExtensions | VP9 video codec |
| Microsoft.AV1VideoExtensions | AV1 video codec |
| Microsoft.HEIFImageExtension | HEIC/HEIF support |

## KEEP (Recommended Unless You Have Alternatives)

| Package | Condition |
|---------|-----------|
| Microsoft.WindowsCamera | Keep unless you use a dedicated camera app |
| Microsoft.ScreenSketch | Keep for screenshot/screen recording |
| Microsoft.XboxGamingOverlay | Keep if you use Game Bar features |
| Microsoft.GamingApp | Keep if you use Game Pass |
| Microsoft.Copilot / Windows.AIFree | Keep if you use Copilot (test stability before removing) |
| Microsoft.Todos | Keep if you use it for task management |
| Microsoft.MicrosoftStickyNotes | Keep if you use sticky notes |
| Microsoft.Windows.AIFree | Keep if you use Copilot |

## OPTIONAL (Safe to Remove Based on Preference)

| Package | Notes |
|---------|-------|
| Microsoft.WindowsCalculator | Use alternative calculator |
| Microsoft.ZuneVideo | Use VLC or other player |
| Microsoft.ZuneMusic | Use Spotify or other player |
| Microsoft.BingWeather | Use web weather |
| Microsoft.YourPhone | Remove if not using Phone Link |
| Microsoft.Clipchamp | Remove if not video editing |
| Microsoft.WindowsMaps | Remove if using Google Maps |
| Microsoft.RawImageExtension | Remove if you don't shoot RAW |
| Microsoft.Windows.PPIProjection | Remove if not using wireless display |

## REMOVE (Bloatware - Safe for Most Users)

| Package | Notes |
|---------|-------|
| Microsoft.BingNews | News available via browser |
| Microsoft.GetHelp | Support available via browser |
| Microsoft.Getstarted | Tips are unnecessary |
| Microsoft.MicrosoftSolitaireCollection | Classic bloatware |
| Microsoft.People | Obsolete in Windows 11 |
| Microsoft.WindowsFeedbackHub | Unless Windows Insider |
| Microsoft.MicrosoftOfficeHub | Just a launcher/promotional |
| Microsoft.PowerAutomateDesktop | Unless you use RPA |
| Microsoft.549981C3F5F10 | Deprecated Cortana |
| Microsoft.XboxApp | Legacy, replaced by GamingApp |
| Microsoft.XboxSpeechToText | Rarely used |
| Microsoft.WindowsTerminalPreview | Beta app, not needed |
| Microsoft.BingSearch | Unless you use the search widget |

---

# APPENDIX A: COMPLETE REMOVAL SCRIPT (PowerShell)

```powershell
# ============================================================
# Windows 11 Bloatware Removal Script
# Run as Administrator in PowerShell
# Use at your own risk - test on a non-production machine first
# ============================================================

# List of packages considered safe to remove for most users
$bloatware = @(
    # News and Weather
    "Microsoft.BingNews"
    "Microsoft.BingWeather"
    "Microsoft.BingSearch"
    
    # Entertainment
    "Microsoft.MicrosoftSolitaireCollection"
    "Microsoft.ZuneVideo"
    "Microsoft.ZuneMusic"
    "Microsoft.Clipchamp"
    
    # Productivity (that most users don't need)
    "Microsoft.GetHelp"
    "Microsoft.Getstarted"
    "Microsoft.People"
    "Microsoft.MicrosoftOfficeHub"
    "Microsoft.PowerAutomateDesktop"
    "Microsoft.Todos"
    "Microsoft.MicrosoftStickyNotes"
    
    # Xbox (non-essential)
    "Microsoft.XboxApp"
    "Microsoft.XboxSpeechToText"
    
    # System extras
    "Microsoft.WindowsFeedbackHub"
    "Microsoft.WindowsMaps"
    "Microsoft.549981C3F5F10"
    "Microsoft.YourPhone"
    
    # Preview/Beta
    "Microsoft.WindowsTerminalPreview"
)

# Step 1: Remove for current user
Write-Host "Removing packages for current user..." -ForegroundColor Yellow
foreach ($pkg in $bloatware) {
    $app = Get-AppxPackage -Name $pkg -ErrorAction SilentlyContinue
    if ($app) {
        Remove-AppxPackage -Package $app.PackageFullName -ErrorAction SilentlyContinue
        Write-Host "  Removed: $pkg" -ForegroundColor Green
    } else {
        Write-Host "  Not found: $pkg" -ForegroundColor Gray
    }
}

# Step 2: Remove provisioned packages (prevents reinstall for new users)
Write-Host "`nRemoving provisioned packages..." -ForegroundColor Yellow
foreach ($pkg in $bloatware) {
    $prov = Get-AppxProvisionedPackage -Online | Where-Object {$_.PackageName -like "*$pkg*"}
    if ($prov) {
        Remove-AppxProvisionedPackage -Online -PackageName $prov.PackageName -ErrorAction SilentlyContinue
        Write-Host "  Removed provisioned: $pkg" -ForegroundColor Green
    } else {
        Write-Host "  Not provisioned: $pkg" -ForegroundColor Gray
    }
}

Write-Host "`nDone! Restart your computer for changes to take full effect." -ForegroundColor Cyan
```

# APPENDIX B: REINSTALL ALL DEFAULT APPS

```powershell
# Re-register all built-in Windows apps (fixes broken/missing apps)
Get-AppxPackage -AllUsers | ForEach-Object {
    Add-AppxPackage -DisableDevelopmentMode -Register "$($_.InstallLocation)\AppXManifest.xml" -ErrorAction SilentlyContinue
}

# Or re-install specific apps from Microsoft Store
# Open Microsoft Store > Search for the app > Click Install

# Reinstall a specific package using its PackageFamilyName
# Example: Microsoft.WindowsCalculator
# Just search "Calculator" in Microsoft Store and install
```

# APPENDIX C: COMMON PACKAGE FAMILY NAMES

For reference, here are the Package Family Names (PFN) used in some operations:

```
Microsoft.WindowsCalculator_8wekyb3d8bbwe
Microsoft.WindowsCamera_8wekyb3d8bbwe
Microsoft.WindowsAlarms_8wekyb3d8bbwe
Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe
Microsoft.Windows.Photos_8wekyb3d8bbwe
Microsoft.Paint_8wekyb3d8bbwe
Microsoft.WindowsNotepad_8wekyb3d8bbwe
Microsoft.WindowsTerminal_8wekyb3d8bbwe
Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe
Microsoft.WindowsMaps_8wekyb3d8bbwe
Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe
Microsoft.ScreenSketch_8wekyb3d8bbwe
Microsoft.ZuneVideo_8wekyb3d8bbwe
Microsoft.ZuneMusic_8wekyb3d8bbwe
Microsoft.Clipchamp_8wekyb3d8bbwe
Microsoft.Xbox.TCUI_8wekyb3d8bbwe
Microsoft.XboxApp_8wekyb3d8bbwe
Microsoft.XboxGamingOverlay_8wekyb3d8bbwe
Microsoft.XboxIdentityProvider_8wekyb3d8bbwe
Microsoft.XboxSpeechToText_8wekyb3d8bbwe
Microsoft.GamingApp_8wekyb3d8bbwe
Microsoft.OneDriveSync_8wekyb3d8bbwe
Microsoft.YourPhone_8wekyb3d8bbwe
Microsoft.GetHelp_8wekyb3d8bbwe
Microsoft.Getstarted_8wekyb3d8bbwe
Microsoft.BingWeather_8wekyb3d8bbwe
Microsoft.BingNews_8wekyb3d8bbwe
Microsoft.BingSearch_8wekyb3d8bbwe
Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe
Microsoft.People_8wekyb3d8bbwe
Microsoft.WindowsFeedbackHub_8wekyb3d8bbwe
Microsoft.MicrosoftOfficeHub_8wekyb3d8bbwe
Microsoft.PowerAutomateDesktop_8wekyb3d8bbwe
Microsoft.549981C3F5F10_8wekyb3d8bbwe
Microsoft.WindowsStore_8wekyb3d8bbwe
Microsoft.Windows.AIFree_8wekyb3d8bbwe
Microsoft.Copilot_8wekyb3d8bbwe
Microsoft.WindowsAI.Copilot_8wekyb3d8bbwe
Microsoft.DesktopAppInstaller_8wekyb3d8bbwe
Microsoft.StorePurchaseApp_8wekyb3d8bbwe
Microsoft.Windows.NarratorQuickStart_8wekyb3d8bbwe
Microsoft.Windows.OOBENetworkCaptivePortal_8wekyb3d8bbwe
Microsoft.Windows.OOBENetworkConnectionFlow_8wekyb3d8bbwe
Windows.PrintDialog
Microsoft.Windows.ContentDeliveryManager
Microsoft.Client.CBS
Microsoft.Todos_8wekyb3d8bbwe
```

---

# APPENDIX D: DISABLING WITHOUT REMOVING

If you want to disable apps without removing them (easier to reverse):

```powershell
# Disable an app via Group Policy (registry)
# This prevents the app from running but keeps it installed

# To disable specific apps via registry (HKCU):
$appDisablePath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
# Note: This approach is limited and not all apps support it

# Better approach: Just remove the Start Menu pin and don't use it
# Or use Group Policy in Windows Pro/Enterprise:
# Computer Config > Admin Templates > Windows Components > Store
# "Disable all apps from Microsoft Store" - but this is all-or-nothing

# Windows Pro/Enterprise: Use AppLocker or WDAC for granular control
# Computer Config > Windows Settings > Security Settings >
#   Application Control Policies > AppLocker > Packaged app Rules

# Simplest approach: Remove from Start Menu, stop worrying about it
```

---

*This inventory covers the major default AppX/MSIX packages in Windows 11 as of 2024-2025. Package names and availability may vary slightly between Windows 11 versions (22H2, 23H2, 24H2). Always create a System Restore point before removing packages.*
