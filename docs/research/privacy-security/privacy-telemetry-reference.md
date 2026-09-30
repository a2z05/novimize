# Windows 10/11 Privacy & Telemetry - Complete Technical Reference

> Compiled from official Microsoft documentation and technical analysis for PC optimization utility development.

---

## Table of Contents

1. [Telemetry Levels](#1-telemetry-levels)
2. [Telemetry Registry Settings](#2-telemetry-registry-settings)
3. [Advertising ID](#3-advertising-id)
4. [Activity History / Timeline](#4-activity-history--timeline)
5. [Cortana and Cloud Search](#5-cortana-and-cloud-search)
6. [Windows Spotlight / Lock Screen Content](#6-windows-spotlight--lock-screen-content)
7. [Other Privacy-Related Registry Keys](#7-other-privacy-related-registry-keys)
8. [Privacy Impact vs Performance Impact](#8-privacy-impact-vs-performance-impact)
9. [What Telemetry/Privacy Changes Can Break](#9-what-telemetryprivacy-changes-can-break)
10. [Windows 11 Specific Privacy Changes](#10-windows-11-specific-privacy-changes)

---

## 1. Telemetry Levels

Microsoft collects Windows diagnostic data to keep devices secure, up to date, and performing properly. There are four diagnostic data collection settings, though not all are available on all editions.

### Level 0 - Diagnostic Data Off (Security)

| Attribute | Details |
|-----------|---------|
| **Current Name** | "Diagnostic data off (Security)" |
| **Previous Name** | "Security" |
| **Registry Value** | `AllowTelemetry = 0` |
| **What is collected** | No Windows diagnostic data is sent whatsoever |
| **Crash Metadata** | N/A (not collected) |
| **Crash Dumps** | N/A (not collected) |
| **Diagnostic Logs** | N/A (not collected) |
| **Data Collection Rate** | N/A |
| **Editions Available** | Windows Enterprise, Windows Education, Windows Server 2022+ |
| **NOT Available On** | Windows Home, Windows Pro (consumer), Windows 10/11 Home |
| **Notes** | Was previously the default for Windows Server 2022 Datacenter: Azure Edition until December 13, 2022. Removed from Windows 10 starting with version 1903 for consumer editions. Microsoft recommends at minimum "Required" if relying on Windows Update. |

### Level 1 - Required Diagnostic Data (Basic)

| Attribute | Details |
|-----------|---------|
| **Current Name** | "Required diagnostic data" |
| **Previous Name** | "Basic" |
| **Registry Value** | `AllowTelemetry = 1` |
| **What is collected** | Minimum data required to keep device secure, up to date, and performing as expected |
| **Crash Metadata** | Yes |
| **Crash Dumps** | No |
| **Diagnostic Logs** | No |
| **Data Collection Rate** | 100% of devices (all devices send this) |
| **Editions Available** | All editions (Home, Pro, Enterprise, Education) |
| **Default Since** | Windows 10 version 1903 (for consumer editions) |
| **Specific Data Collected** | |
| | - Device attributes: camera resolution, display type |
| | - Battery attributes: capacity, type |
| | - Networking: number of adapters, speed, mobile operator, IMEI |
| | - Processor/memory: cores, architecture, speed, size, firmware |
| | - Virtualization: SLAT support, guest OS |
| | - OS attributes: edition, virtualization state |
| | - Storage: number of drives, type, size |
| | - Quality metrics: upload rates, dropped/blocked events |
| | - Device performance: crashes, hangs, app state changes |
| | - Compatibility data: installed apps, compatibility issues |
| | - System upgrade readiness data |
| | - Connected peripheral devices (printers, external storage) |
| | - Driver data for upgrade compatibility |
| | - Microsoft Store data: downloads, installs, updates |

### Level 2 - Enhanced (Legacy)

| Attribute | Details |
|-----------|---------|
| **Current Name** | "Enhanced" |
| **Registry Value** | `AllowTelemetry = 2` |
| **What is collected** | All Required data PLUS additional data about websites browsed, how Windows/apps are used and perform, and device activity |
| **Crash Metadata** | Yes |
| **Crash Dumps** | Triage dumps only (not full dumps) |
| **Diagnostic Logs** | No |
| **Data Collection Rate** | Sampling applies (not 100%) |
| **Editions Available** | Windows 10 version 1809 and earlier, Windows Server 2016, Windows Server 2019 |
| **NOT Available On** | Windows 11, Windows Server 2022, Windows 10 1903+ |
| **Notes** | This level has been deprecated on Windows 11 and replaced with policies that control optional diagnostic data. Only exists as a legacy option on older OS versions. |
| **Additional Data Collected** | |
| | - OS events: networking, Hyper-V, Cortana, storage, file system |
| | - OS app events: Microsoft apps, management tools, Store apps |
| | - Device-specific events (Surface Hub, HoloLens HPU events) |
| | - All crash dump types except heap and full dumps |

### Level 3 - Optional Diagnostic Data (Full)

| Attribute | Details |
|-----------|---------|
| **Current Name** | "Optional diagnostic data" |
| **Previous Name** | "Full" / "Optional" |
| **Registry Value** | `AllowTelemetry = 3` |
| **What is collected** | All Required data PLUS additional device/connectivity/config data, status/logging, app activity, browser activity, enhanced error reporting |
| **Crash Metadata** | Yes |
| **Crash Dumps** | Full and triage memory dumps |
| **Diagnostic Logs** | Yes |
| **Data Collection Rate** | Sampling applies (not 100%) |
| **Editions Available** | All editions |
| **Default On** | Windows 10 Home/Pro (consumer editions that cannot set to 0) |
| **Additional Data Collected** | |
| | - Additional device, connectivity, and configuration data |
| | - Status/logging for OS and system component health |
| | - App activity: programs launched, run time, response speed |
| | - Browser activity: browsing history and search terms (Edge/IE) |
| | - Enhanced error reporting including memory state at crash time |
| **Warning** | Crash dumps may unintentionally contain personal data (portions of memory from documents/web pages). Crash data is never used for Tailored experiences. |

### Level 4 (Historical - Deprecated)

| Attribute | Details |
|-----------|---------|
| **Status** | No longer exists as a separate level |
| **History** | In early Windows 10 builds (pre-1511), there was a Level 4 that was more permissive than current Level 3 |
| **Current Equivalent** | Level 3 (Optional/Full) is now the maximum available level |

### Telemetry Levels Summary Table

| Level | Name (Current) | Name (Legacy) | Value | Data Sent | Crash Dumps | Logs | Collection |
|-------|----------------|---------------|-------|-----------|-------------|------|------------|
| 0 | Off (Security) | Security | 0 | None | None | None | N/A |
| 1 | Required | Basic | 1 | Device/config/perf basics | Metadata only | No | 100% |
| 2 | Enhanced | Enhanced | 2 | + App/website usage | Triage only | No | Sampled |
| 3 | Optional | Full | 3 | + Browser/history/crashes | Full + triage | Yes | Sampled |

### Edition Support Matrix

| Edition | Level 0 | Level 1 | Level 2 | Level 3 | Default |
|---------|---------|---------|---------|---------|---------|
| Windows 10/11 Home | No | Yes | Legacy only | Yes | Level 3 |
| Windows 10/11 Pro | No | Yes | Legacy only | Yes | Level 3 |
| Windows 10/11 Enterprise | Yes | Yes | Legacy only | Yes | Level 1 |
| Windows 10/11 Education | Yes | Yes | Legacy only | Yes | Level 1 |
| Windows Server 2022 | Yes | Yes | N/A | Yes | Level 1 |
| Windows Server 2019 | Yes | Yes | Yes | Yes | Level 1 |

---

## 2. Telemetry Registry Settings

### AllowTelemetry (Primary Control)

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection
Name:   AllowTelemetry
Type:   REG_DWORD
Values:
  0 = Security/Off (Enterprise/Education only)
  1 = Required/Basic
  2 = Enhanced (deprecated on Win11)
  3 = Optional/Full
```

**Group Policy Equivalent:**
`Computer Configuration > Administrative Templates > Windows Components > Data Collection and Preview Builds > Allow Telemetry`
(On Win10 1809 and earlier: named "Allow telemetry")

**Notes:**
- On Windows 10 Home, the minimum enforceable level is 1 (Basic)
- Value 0 is only honored on Enterprise/Education editions
- Windows 11 simplified naming to "Required" and "Optional" (dropped "Basic"/"Full")
- If both Computer Configuration and User Configuration policies are set, the more restrictive policy is used
- The user can still use Settings to set a more restrictive value unless "Configure diagnostic data opt-in settings user interface" policy is also set

### DiagnosticDataViewer (Diagnostic Data Viewer)

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection
Name:   DisableDiagnosticDataViewer
Type:   REG_DWORD
Values:
  1 = Disable the Diagnostic Data Viewer
  0 = Enable (default)
```

**Or to explicitly enable:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection
Name:   EnableDiagnosticDataViewer
Type:   REG_DWORD
Values:
  1 = Enable
  0 = Disable
```

**Settings UI Path:** Settings > Privacy & security > Diagnostics & feedback > View diagnostic data

**What it does:**
- When enabled, stores diagnostic data locally (up to 1 GB / 30 days)
- Users can view JSON-formatted diagnostic events via the Diagnostic Data Viewer app
- PowerShell alternative: `Install-Module -Name Microsoft.DiagnosticDataViewer` then `Enable-DiagnosticDataViewing`
- Average device generates approximately 6 MB of diagnostic data per day
- Enabling data viewing can use up to 1 GB of disk space on system drive

### DiagTrack Service Registry Keys

```
Path:   HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Diagnostics\DiagTrack
```
This path contains configuration for the Connected User Experiences and Telemetry service (DiagTrack).

```
Path:   HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack
Name:   Start
Type:   REG_DWORD
Values:
  2 = Automatic (default)
  3 = Manual
  4 = Disabled
```

**Note:** Disabling DiagTrack service entirely can break Windows Update diagnostics, app telemetry, and some Windows features. Microsoft recommends against fully disabling this service.

### DataCollection Policy Keys

```
Path:   HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection
```

Key values under this path:
```
Name:   AllowTelemetry
Type:   REG_DWORD
Values: Same as above (0-3)

Name:   MaxTelemetryAllowed
Type:   REG_DWORD
Values:
  1 = Required only
  3 = Optional (full)
  (Limits maximum telemetry level the user can select)

Name:   DoNotShowFeedbackNotifications
Type:   REG_DWORD
Values:
  1 = Suppress feedback notification prompts
  0 = Allow feedback prompts (default)
```

### LimitDiagnosticLogCollection

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection
Name:   LimitDiagnosticLogCollection
Type:   REG_DWORD
Values:
  1 = Limit log collection (diagnostic logs not sent to Microsoft)
  0 = Allow full log collection (default)
```

**Note:** Only available on Windows 11 and Windows Server 2022.

### LimitDumpCollection

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection
Name:   LimitDumpCollection
Type:   REG_DWORD
Values:
  1 = Limit to kernel mini dumps and user mode triage dumps only
  0 = Allow all dump types (default)
```

**Note:** Only available on Windows 11 and Windows Server 2022.

### AllowDeviceMetadataConnectedNetwork

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\Device Metadata
Name:   PreventDeviceMetadataFromNetwork
Type:   REG_DWORD
Values:
  1 = Prevent device metadata retrieval from network
  0 = Allow retrieval (default)
```

**Group Policy:** Computer Configuration > Administrative Templates > Windows Components > Device Metadata

### Telemetry Endpoints (for firewall/proxy configuration)

| Service | Endpoints |
|---------|-----------|
| Connected User Experiences & Telemetry | v10.events.data.microsoft.com, v10c.events.data.microsoft.com, v10.vortex-win.data.microsoft.com |
| Windows Error Reporting | watson.telemetry.microsoft.com, umwatsonc.events.data.microsoft.com, *.blob.core.windows.net |
| Authentication | login.live.com (do NOT block - device authentication) |
| Online Crash Analysis | oca.telemetry.microsoft.com, oca.microsoft.com |
| Settings | settings-win.data.microsoft.com (do NOT block - config endpoint) |

---

## 3. Advertising ID

### Overview
The Advertising ID is a unique identifier assigned to each user/device for app developers to deliver personalized advertisements. Disabling it does not remove ads but prevents them from being targeted based on your activity.

### Registry Keys

**Per-User Disable (HKCU):**
```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo
Name:   Enabled
Type:   REG_DWORD
Values:
  0 = Advertising ID disabled
  1 = Advertising ID enabled (default)
```

**Machine-Wide Disable (HKLM - requires restart):**
```
Path:   HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo
Name:   Enabled
Type:   REG_DWORD
Values:
  0 = Advertising ID disabled for all users
  1 = Advertising ID enabled (default)
```

**Group Policy Override:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo
Name:   DisabledByGroupPolicy
Type:   REG_DWORD
Values:
  1 = Advertising ID disabled by Group Policy (overrides user setting)
  0 = Not disabled by policy
```

**Group Policy Path:** Computer Configuration > Administrative Templates > System > OS Policies > "Turn off advertising ID"

### What Happens When Disabled
- Apps can no longer use the Advertising ID to deliver personalized ads
- Generic (non-personalized) ads may still appear in some apps
- Some Store apps may show more generic/repetitive ads instead of relevant ones
- No core Windows functionality is affected
- No performance impact
- The Advertising ID is reset if the user signs out and signs back in (when enabled)

### Group Policy Setting Name
- **Turn off advertising ID** (Computer Configuration > Administrative Templates > System > OS Policies)
- Also available as: "Allow or Disallow use of Advertising ID for app tracking" in some versions

---

## 4. Activity History / Timeline

### Overview
Activity History tracks your activities across devices - which apps you use, files you open, and websites you visit. This data powers the Timeline feature (Windows 10) and is used for Microsoft account sync and "Pick up where you left off" features.

### Registry Keys

**Enable Activity Feed:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\System
Name:   EnableActivityFeed
Type:   REG_DWORD
Values:
  1 = Enable activity history / feed
  0 = Disable activity history / feed
```

**Publish User Activities:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\System
Name:   PublishUserActivities
Type:   REG_DWORD
Values:
  1 = Allow publishing of user activities
  0 = Block publishing of user activities
```

**Upload User Activities (to cloud/sync):**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\System
Name:   UploadUserActivities
Type:   REG_DWORD
Values:
  1 = Allow uploading activities to cloud
  0 = Block uploading activities to cloud
```

**User-Level Activity History:**
```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced
Name:   Start_TrackProgs
Type:   REG_DWORD
Values:
  1 = Track program launches
  0 = Do not track
```

### Group Policy Path
`Computer Configuration > Administrative Templates > System > OS Policies > Allow Activity History`

### Windows 10 vs Windows 11 Differences

| Feature | Windows 10 | Windows 11 |
|---------|------------|------------|
| Timeline | Full Timeline view in Task View | Timeline removed from Task View |
| Activity History | Supports full activity timeline | Still collects data but no Timeline UI |
| Cloud sync | Can sync activities across devices | "Pick up where you left off" in Start |
| Settings page | "Activity history" page exists | Renamed to "Activity history" under Privacy |

### What Breaks When Disabled
- Timeline feature is removed (Windows 10)
- "Pick up where you left off" suggestions stop appearing
- Cross-device activity sync stops
- Microsoft account activity history is no longer populated
- Start menu suggestions based on recent activity stop
- No impact on app functionality or core OS performance
- Slight reduction in disk usage (activity history database is not populated)
- Some enterprise compliance/audit features may be affected

---

## 5. Cortana and Cloud Search

### Overview
Cortana has undergone significant changes between Windows 10 and Windows 11. In Windows 10, Cortana was deeply integrated as a voice assistant. In Windows 11 22H2+, Cortana was decoupled and is now a standalone app (not built into the OS shell). As of Windows 11 24H2, Microsoft has officially ended support for the Cortana app.

### Registry Keys - Cortana

**Disable Cortana (Windows 10):**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search
Name:   AllowCortana
Type:   REG_DWORD
Values:
  0 = Disable Cortana
  1 = Enable Cortana (default)
```

**Allow Cloud Search:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search
Name:   AllowCloudSearch
Type:   REG_DWORD
Values:
  0 = Disable cloud search results
  1 = Enable cloud search (default)
```

**Disable Web Search in Start Menu:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search
Name:   DisableWebSearch
Type:   REG_DWORD
Values:
  1 = Disable web search results in Start
  0 = Allow web search (default)
```

**Connected Search (Web):**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search
Name:   ConnectedSearchUseWeb
Type:   REG_DWORD
Values:
  0 = Do not use web results in connected search
  1 = Use web results (default)
```

**Search Location Usage:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search
Name:   AllowSearchToUseLocation
Type:   REG_DWORD
Values:
  0 = Do not use location for search
  1 = Allow location use (default)
```

### Group Policy Path for Cortana
`Computer Configuration > Administrative Templates > Windows Components > Search`

### Cortana Firewall Block (Advanced)
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\FirewallRules
Name:   {0DE40C8E-C126-4A27-9371-A27DAB1039F7}
Type:   REG_SZ
Value:  v2.25|Action=Block|Active=TRUE|Dir=Out|Protocol=6|
        App=%windir%\SystemApps\Microsoft.Windows.Cortana_cw5n1h2txyewy\searchUI.exe|
        Name=Block outbound Cortana|
```

### Cortana Status in Windows 11

| Version | Status |
|---------|--------|
| Windows 11 21H1 | Cortana built into OS shell, voice assistant active |
| Windows 11 22H2 | Cortana decoupled into standalone app, removed from taskbar by default |
| Windows 11 23H2 | Cortana app still available but not pinned |
| Windows 11 24H2 | Cortana app officially end-of-life; Copilot replaces AI assistant role |

**What Breaks When Cortana is Disabled:**
- Voice-activated "Hey Cortana" no longer works
- Cortana search suggestions/recommendations stop
- Some Start menu search features may be reduced
- Windows 10: Search bar may show only local results
- No impact on local file search functionality
- Windows 11: Minimal impact since Cortana is already a standalone app
- In Windows 10 versions, Cortana performance impact was notable (background resource usage)

---

## 6. Windows Spotlight / Lock Screen Content

### Overview
Windows Spotlight delivers rotating wallpapers on the lock screen and background, along with tips, suggestions, fun facts, and (in enterprise) organizational messages. Starting with Windows 11 22H2 KB5046633 and Windows 10 KB5048652, Windows Spotlight became the default wallpaper.

**Important:** Windows Spotlight is only available on Windows Enterprise and Education editions for managed deployment. Consumer editions get Spotlight features automatically.

### Registry Keys

**Disable Windows Spotlight Features (Global):**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent
Name:   DisableWindowsSpotlightFeatures
Type:   REG_DWORD
Values:
  1 = Disable all Windows Spotlight features
  0 = Allow Windows Spotlight (default)
```

**Disable Consumer Features:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent
Name:   DisableWindowsConsumerFeatures
Type:   REG_DWORD
Values:
  1 = Disable consumer features (suggestions, Spotlight content, app promotions)
  0 = Allow consumer features (default)
```

**Disable Tailored Experiences:**
```
Path:   HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent
Name:   DisableTailoredExperiencesWithDiagnosticData
Type:   REG_DWORD
Values:
  1 = Disable tailored/recommended experiences based on diagnostic data
  0 = Allow tailored experiences (default)
```

### Windows Spotlight Policy Settings (Windows 11)

| Policy Name | Description |
|-------------|-------------|
| AllowSpotlightCollection | Allow collection of Spotlight content data (CSP only, no GPO) |
| AllowThirdPartySuggestionsInWindowsSpotlight | Allow third-party suggestions in Spotlight |
| AllowWindowsSpotlight | Enable/disable Windows Spotlight entirely |
| AllowWindowsSpotlightOnActionCenter | Show Spotlight content in Action Center |
| AllowWindowsSpotlightOnSettings | Show Spotlight content in Settings |
| AllowWindowsSpotlightWindowsWelcomeExperience | Show Spotlight in Windows welcome/out-of-box experience |
| ConfigureWindowsSpotlightOnLockScreen | Configure Spotlight behavior on lock screen |

**GPO Path:** Computer Configuration > Administrative Templates > Windows Components > Cloud Content

**CSP:** Experience Policy CSP (for MDM-managed devices)

### What Happens When Disabled
- Lock screen reverts to user-selected or default static wallpaper
- Background no longer shows rotating Spotlight images
- No more "Learn more about this picture" links on lock screen
- No suggestions, fun facts, or tips appear
- No organizational messages (enterprise)
- No performance impact (slight reduction in network usage)
- Some personalization features are lost

---

## 7. Other Privacy-Related Registry Keys

### App Privacy - Universal Control Keys

All App Privacy settings use the same registry path and value scheme:

**Registry Path:** `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy`

**Value meanings:**
| Value | Meaning |
|-------|---------|
| 0 | User can control (default) |
| 1 | Force Allow (always allow app access) |
| 2 | Force Deny (always deny app access) |

#### Location Services

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessLocation
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

**Additional location keys:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors
Name:   DisableLocation
Type:   REG_DWORD
Values:
  1 = Disable location services entirely (all apps and OS)
  0 = Allow location services (default)
```

**User-level location:**
```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location
Name:   Value
Type:   REG_SZ
Values:
  "Deny" = Location denied
  "Allow" = Location allowed
```

#### Camera Access (Global Kill Switch)
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessCamera
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

**User-level:**
```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam
Name:   Value
Type:   REG_SZ
Values: "Deny" or "Allow"
```

#### Microphone Access (Global Kill Switch)
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessMicrophone
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

**User-level:**
```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone
Name:   Value
Type:   REG_SZ
Values: "Deny" or "Allow"
```

#### Contacts Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessContacts
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Calendar Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessCalendar
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Account Info Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessAccountInfo
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Email Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessEmail
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Messaging Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessMessaging
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

**Additional messaging sync:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\Messaging
Name:   AllowMessageSync
Type:   REG_DWORD
Values:
  1 = Allow message synchronization
  0 = Block message synchronization
```

#### Call History Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessCallHistory
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Phone Calls Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessPhone
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Radio Access (Bluetooth, etc.)
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessRadios
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

**Note:** Denying radio access can affect Bluetooth functionality for apps that need it.

#### App Diagnostics Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsGetDiagnosticInfo
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Documents Library Access
```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\documentsLibrary
Name:   Value
Type:   REG_SZ
Values: "Deny" or "Allow"
```

#### Pictures Library Access
```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\picturesLibrary
Name:   Value
Type:   REG_SZ
Values: "Deny" or "Allow"
```

#### Videos Library Access
```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\videosLibrary
Name:   Value
Type:   REG_SZ
Values: "Deny" or "Allow"
```

#### Music Library Access
```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\musicLibrary
Name:   Value
Type:   REG_SZ
Values: "Deny" or "Allow"
```

#### Notifications Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessNotifications
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Other Devices (Sync with Devices)
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsSyncWithDevices
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

**Additional trusted devices key:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessTrustedDevices
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Motion Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessMotion
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Tasks Access
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsAccessTasks
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Voice Activation
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsActivateWithVoice
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)

Name:   LetAppsActivateWithVoiceAboveLock
Type:   REG_DWORD
Values: 0 (User control), 1 (Force Allow), 2 (Force Deny)
```

#### Background Apps (Global)
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
Name:   LetAppsRunInBackground
Type:   REG_DWORD
Values:
  0 = User decides per app
  1 = Force Allow all apps to run in background
  2 = Force Deny - prevent all apps from running in background
```

**Note:** This is one of the few privacy settings that CAN directly affect system performance. See Section 8.

### Ink and Typing Personalization

```
Path:   HKCU\SOFTWARE\Microsoft\InputPersonalization
Name:   RestrictImplicitTextCollection
Type:   REG_DWORD
Values:
  1 = Restrict text data collection for inking/typing improvement
  0 = Allow collection (default)

Name:   RestrictImplicitInkCollection
Type:   REG_DWORD
Values:
  1 = Restrict ink data collection
  0 = Allow collection (default)
```

**Additional inking data:**
```
Path:   HKCU\SOFTWARE\Microsoft\InputPersonalization\TrainedDataStore
Name:   (Various keys related to handwriting recognition data)
```

### Handwriting Data Sharing / Ink Analysis

```
Path:   HKCU\SOFTWARE\Microsoft\Input\TIPC\Enabled
Name:   Enabled
Type:   REG_DWORD
Values:
  1 = Enable inking and handwriting analysis/sharing
  0 = Disable
```

### Find My Device

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\FindMyDevice
Name:   AllowFindMyDevice
Type:   REG_DWORD
Values:
  1 = Allow Find My Device
  0 = Disable Find My Device
```

**Group Policy Path:** Computer Configuration > Administrative Templates > Windows Components > Find My Device

**What breaks:** Location-based device recovery stops working. Cannot remotely locate, lock, or erase device.

### Online Speech Privacy

```
Path:   HKCU\SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy
Name:   HasAccepted
Type:   REG_DWORD
Values:
  1 = Online speech recognition accepted/enabled
  0 = Online speech recognition declined/disabled
```

**Speech Model Update:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Speech
Name:   AllowSpeechModelUpdate
Type:   REG_DWORD
Values:
  0 = Prevent speech model updates from being downloaded
  1 = Allow updates (default)
```

### Tailored Experiences

```
Path:   HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent
Name:   DisableTailoredExperiencesWithDiagnosticData
Type:   REG_DWORD
Values:
  1 = Disable personalized tips/recommendations based on diagnostic data
  0 = Allow (default)

Name:   DisableSoftLanding
Type:   REG_DWORD
Values:
  1 = Disable soft landing tips/content
  0 = Allow (default)
```

### Feedback Frequency

```
Path:   HKCU\SOFTWARE\Microsoft\Siuf\Rules
Name:   NumberOfSIUFInPeriod
Type:   REG_DWORD
Values:
  0 = Never ask for feedback
  1 = Once per period (use with PeriodInNanoSeconds)

Name:   PeriodInNanoSeconds
Type:   REG_DWORD
Values:
  0 = Never
  864000000000 = Once a day
  6048000000000 = Once a week
  (Delete both keys for "Automatically" / "Always" behavior)
```

**Suppress feedback notifications:**
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection
Name:   DoNotShowFeedbackNotifications
Type:   REG_DWORD
Values:
  1 = Suppress all feedback notifications
  0 = Allow notifications (default)
```

### Suggested / Autocomplete Settings (IE/Edge Legacy)

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Internet Explorer\Suggested Sites
Name:   Enabled
Type:   REG_DWORD
Values:
  0 = Disable suggested sites
  1 = Enable (default)

Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Explorer\AutoComplete
Name:   AutoSuggest
Type:   REG_SZ
Values:
  "no" = Disable autocomplete suggestions
  "yes" = Enable (default)
```

### Cross-Device Experiences

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\System
Name:   EnableCdp
Type:   REG_DWORD
Values:
  0 = Disable Cross Device Platform (prevents phone-PC sync)
  1 = Allow (default)
```

### News and Interests / Windows Feeds

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds
Name:   EnableFeeds
Type:   REG_DWORD
Values:
  0 = Disable news and interests feeds
  1 = Enable (default)
```

### Live Tiles

```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\PushNotifications
Name:   NoCloudApplicationNotification
Type:   REG_DWORD
Values:
  1 = Disable cloud-based push notifications / live tiles
  0 = Allow (default)
```

### Language List Sharing

```
Path:   HKCU\Control Panel\International\User Profile
Name:   HttpAcceptLanguageOptOut
Type:   REG_DWORD
Values:
  1 = Opt out of sending language list to websites
  0 = Send language list (default)
```

### SmartScreen for Store Apps

```
Path:   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost
Name:   EnableWebContentEvaluation
Type:   REG_DWORD
Values:
  0 = Disable SmartScreen for Store apps
  1 = Enable (default)
```

---

## 8. Privacy Impact vs Performance Impact

### Settings with NO Measurable Performance Impact

These settings are purely about data collection/privacy and have no effect on system speed, responsiveness, or resource usage:

| Setting | Performance Impact | Privacy Impact |
|---------|-------------------|----------------|
| AllowTelemetry (all levels) | None - data collection runs in background with negligible overhead | High - controls what diagnostic data is sent |
| Advertising ID | None - only affects ad targeting | Medium - prevents behavioral ad tracking |
| Tailored Experiences | None | Medium - stops personalized recommendations |
| Feedback Frequency | None - only controls prompt frequency | Low - reduces notification prompts |
| Online Speech Privacy | None | Medium - prevents voice data from being processed in cloud |
| Inking/Typing Personalization | Negligible | Medium - prevents handwriting/typing data collection |
| Language List Sharing | None | Low - prevents language list disclosure |
| Diagnostic Data Viewer | Negligible (uses ~1GB disk when enabled) | High - enables visibility into collected data |
| Find My Device | None | Medium - location tracking for device recovery |
| Activity History | Minimal - slightly reduces disk I/O | Medium - stops activity tracking/collection |

### Settings with POTENTIAL Performance Impact

| Setting | Performance Impact | Details |
|---------|-------------------|---------|
| **Background Apps (LetAppsRunInBackground)** | **MODERATE TO HIGH** | If many UWP apps are allowed to run in background, they consume CPU, RAM, and battery. Setting to "Force Deny" can significantly improve battery life and reduce idle resource usage on systems with many Store apps. |
| **Cortana** (Windows 10) | Low to Moderate | Cortana ran background processes, used CPU for voice wake-word detection ("Hey Cortana"), and maintained network connections. Disabling freed modest resources. |
| **Windows Spotlight** | Negligible | Minor network usage for downloading new wallpaper images. |
| **Location Services** | Negligible | GPS/radio polling is minimal unless actively used by many apps. |
| **OneDrive Sync** | Low to Moderate | Active file sync uses network, CPU, and disk I/O. |
| **Sync Your Settings** | Negligible | Background sync of preferences uses minimal resources. |
| **Live Tiles/Push Notifications** | Negligible | Minor network polling for tile updates. |

### Key Insight for Optimization Tool Design

**Most Windows privacy settings are about DATA COLLECTION, not processing overhead.** The telemetry pipeline (DiagTrack service) runs regardless of level setting - it just sends less data at lower levels. Users who disable telemetry for "performance gains" will see virtually no improvement in speed or responsiveness.

The settings that actually affect performance are:
1. **Background apps** - the most impactful privacy-adjacent setting
2. **Cortana** (Windows 10 only) - moderate background resource usage
3. **OneDrive sync** - network and disk impact

A well-designed optimization tool should clearly communicate to users which settings affect performance vs. which are purely privacy choices.

---

## 9. What Telemetry/Privacy Changes Can Break

### High Risk - Significant Functionality Loss

| Setting Disabled | What Breaks | Severity |
|-----------------|-------------|----------|
| **Telemetry Level 0** | Windows Update diagnostics are severely limited. Microsoft cannot identify and fix update failures. May cause update loops or unresolved update issues. | HIGH |
| **DiagTrack Service (disabled entirely)** | Windows Update may fail or become unreliable. Windows Error Reporting stops. Some app telemetry breaks. Microsoft Store may have issues. Device compatibility data not collected. | HIGH |
| **Location Services** | GPS-dependent apps (Maps, Weather, navigation) break. Time zone auto-detection may fail. Find My Device stops working. Some enterprise geofencing features break. | HIGH |
| **Camera (Force Deny)** | All apps blocked from camera access. Video conferencing (Teams, Zoom, etc.) cannot use camera. Windows Hello face recognition fails. | HIGH |
| **Microphone (Force Deny)** | All apps blocked from microphone. Voice calls, dictation, voice commands all fail. Windows Hello voice features break. | HIGH |

### Medium Risk - Reduced Functionality

| Setting Disabled | What Breaks | Severity |
|-----------------|-------------|----------|
| **Required Diagnostic Data (Level 1)** | Not recommended. Microsoft uses this data for security updates. May cause delayed or missing security patches. | MEDIUM |
| **Advertising ID** | Some Store apps may show more repetitive/generic ads. No core functionality breaks. Some ad-supported free apps may have slightly different ad experiences. | LOW |
| **Activity History** | Timeline feature removed (Win10). "Pick up where you left off" stops. Cross-device sync of activities ceases. No app breaks. | LOW |
| **Cloud Content/Spotlight** | Personalization features lost. No content suggestions. Lock screen becomes static. No functional breakage. | LOW |
| **Background Apps** | Apps may not receive timely notifications. Background music players may stop. Messaging apps may not show new messages until opened. Download managers may pause. Calendar reminders may be delayed. | MEDIUM-HIGH |
| **Online Speech Recognition** | Cloud-based speech-to-text stops working. Local speech recognition still works but is less accurate. Dictation quality degrades. | MEDIUM |
| **Cortana** | Voice assistant features lost. In Windows 10, search bar may show only local results. In Windows 11, minimal impact. | LOW-MEDIUM |
| **Find My Device** | Cannot remotely locate, lock, or erase a lost/stolen device. | MEDIUM |

### Low Risk - Minor or No Impact

| Setting Disabled | What Breaks | Severity |
|-----------------|-------------|----------|
| **Diagnostic Data Viewer** | Not actually a problem - simply removes the viewer app. No data collection changes. Users just can't see what's being sent. | NONE |
| **Tailored Experiences** | No personalized tips/recommendations. Some users may prefer this. | NONE |
| **Feedback Notifications** | No prompts to send feedback. Many users prefer this. | NONE |
| **Language List Sharing** | Websites may not automatically offer localized content. User can manually select language. | VERY LOW |
| **News/Interests Feeds** | Taskbar news widget disappears. Many users prefer this. | NONE |
| **Live Tiles** | Cloud-based tile updates stop. Tiles show static content. | VERY LOW |
| **Cross-Device Platform** | Phone-PC sync features stop. Clipboard sharing across devices may break. | LOW |
| **Handwriting Data** | Handwriting recognition may become slightly less accurate over time. No immediate break. | VERY LOW |
| **Inking Analysis** | Ink-to-text conversion quality may degrade. No immediate break. | VERY LOW |

### Critical Warnings for Optimization Tool

1. **Never recommend disabling DiagTrack service** - it is essential for Windows Update functionality
2. **Never set telemetry to Level 0 on non-Enterprise editions** - the setting will be ignored or cause issues
3. **Warn users before denying camera/microphone globally** - many common apps will break
4. **Warn about background app restrictions** - notification-heavy apps (messaging, email) will be affected
5. **Location disable is safe** but breaks GPS-dependent features
6. **Most telemetry/privacy settings are safe to disable** with minimal functional impact

### Apps Known to Be Affected by Telemetry Restrictions

| App | Affected By | Impact |
|-----|-------------|--------|
| Microsoft Teams | Telemetry Level 0, DiagTrack disabled | May not function properly, telemetry-dependent diagnostics |
| Microsoft Edge | Telemetry settings, SmartScreen | Some security features reduced |
| Microsoft Store | Telemetry, Background Apps | Downloads/updates may be affected |
| Windows Update | Telemetry Level 0, DiagTrack | Update diagnostics impaired |
| Windows Security | Telemetry | Some threat intelligence features reduced |
| Copilot | Telemetry, Online Speech | Cloud AI features may degrade |
| Maps / Weather | Location Services | Core functionality breaks |
| Camera app | Camera Access Deny | Cannot take photos/video |
| Voice Recorder | Microphone Access Deny | Cannot record audio |
| Dictation | Online Speech, Microphone | Speech-to-text fails |

---

## 10. Windows 11 Specific Privacy Changes

### Major Changes from Windows 10

#### 1. Telemetry Level Simplification
- **Windows 10:** Four visible levels (Security, Basic, Enhanced, Full)
- **Windows 11:** Two user-facing levels (Required, Optional) with "Enhanced" removed
- Level 0 (Security) only available on Enterprise/Education
- Level 2 (Enhanced) removed - was only on Win10 1809 and earlier / Server 2019

#### 2. Cortana Decoupling
- **Windows 10:** Cortana deeply integrated into OS shell, taskbar, search
- **Windows 11 21H2:** Cortana moved to standalone app, removed from taskbar
- **Windows 11 22H2+:** Cortana available as downloadable app only
- **Windows 11 24H2:** Cortana officially end-of-life, replaced by Copilot
- The `AllowCortana` registry key still exists but has reduced effect in Windows 11

#### 3. Online Speech Privacy (New in Windows 11)
```
Path:   HKCU\SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy
Name:   HasAccepted
Type:   REG_DWORD
Values:
  1 = Online speech recognition enabled
  0 = Disabled (voice processing stays local)
```
- Windows 11 separates online (cloud) vs. offline (local) speech processing
- Users get explicit control over whether voice data is sent to Microsoft
- Local speech recognition continues to work even when online is disabled

#### 4. Inking and Typing Personalization
```
Path:   HKCU\SOFTWARE\Microsoft\InputPersonalization
Name:   RestrictImplicitTextCollection
Type:   REG_DWORD
Values: 1 = Restrict, 0 = Allow

Name:   RestrictImplicitInkCollection
Type:   REG_DWORD
Values: 1 = Restrict, 0 = Allow
```
- More granular control than Windows 10
- Separate controls for text and ink data collection
- Data is used to improve handwriting recognition and word prediction

#### 5. Tailored Experiences
```
Path:   HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent
Name:   DisableTailoredExperiencesWithDiagnosticData
Type:   REG_DWORD
Values:
  1 = Disable personalized tips/recommendations
  0 = Allow (default)
```
- New in Windows 11: uses diagnostic data to provide "relevant" tips
- Controlled by this specific registry key
- Can be disabled without affecting diagnostic data collection itself

#### 6. Diagnostic Data Viewer Improvements
- Still available via Microsoft Store
- Now includes "View problem reports" for Windows Error Reporting data
- Added "About your data" analytics view showing data summary over time
- PowerShell module available: `Microsoft.DiagnosticDataViewer`
- Default storage: 1 GB or 30 days (whichever first)
- Average data generation: ~6 MB per day per device

#### 7. New Privacy Settings Page Layout
Windows 11 reorganized the Settings > Privacy page:
- Renamed "Privacy" to "Privacy & security"
- Added new top-level sections for app permissions
- Added "App permissions" section with granular per-app controls
- Added "Windows permissions" section for OS-level privacy
- Activity History renamed but Timeline UI removed

#### 8. Copilot Privacy Considerations (New in Windows 11 23H2+)
```
Path:   HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot
Name:   TurnOffWindowsCopilot
Type:   REG_DWORD
Values:
  1 = Disable Copilot
  0 = Allow Copilot (default)
```
**Group Policy:** Computer Configuration > Administrative Templates > Windows Components > Windows Copilot

- Copilot is a new privacy consideration in Windows 11
- Sends queries to cloud AI services
- Can be disabled via Group Policy or registry
- Enterprise editions have full management control
- Home/Pro editions: Copilot can be uninstalled or disabled via registry

#### 9. Windows Spotlight as Default (22H2+)
- Starting with KB5046633 (Win11) / KB5048652 (Win10), Spotlight became the default wallpaper
- New `AllowSpotlightCollection` CSP policy added for Win11
- Organization can configure via `ConfigureWindowsSpotlightOnLockScreen` policy

#### 10. Smart App Control (Windows 11 22H2+)
- New security feature that may affect privacy
- Uses cloud-based reputation checking
- Can be configured via WDAC policies
- Blocks untrusted/malicious apps

### Windows 11 Privacy Settings Comparison

| Setting | Windows 10 | Windows 11 |
|---------|------------|------------|
| Telemetry levels | 4 (Security, Basic, Enhanced, Full) | 2 user-facing (Required, Optional) |
| Cortana | Built-in voice assistant | Standalone app (end-of-life in 24H2) |
| Activity History | Full Timeline | Data collected but no Timeline UI |
| Online Speech | Single toggle | Separate online vs. offline controls |
| Tailored Experiences | Part of feedback settings | Dedicated registry key |
| Privacy settings page | "Privacy" | "Privacy & security" |
| App permissions | Basic | Granular per-category controls |
| Copilot | Not available | AI assistant with privacy implications |
| Windows Spotlight | Lock screen only | Lock screen + background + tips |
| Diagnostic Data Viewer | Basic | Enhanced with analytics and PowerShell |

### Windows 11 24H2 Additional Changes

- **Cortana app officially discontinued** - no longer supported or updated
- **Copilot becomes primary AI assistant** - new privacy considerations
- **Enhanced Passkey support** - reduces reliance on passwords
- **Personal Data Encryption** - file-level encryption beyond BitLocker
- **WebAuthn ECC support** - improved authentication security
- **Additional location permissions** - more granular location controls
- **Improved camera/microphone indicators** - clearer visibility when sensors are in use

---

## Appendix A: Quick Reference - All AppPrivacy Registry Keys

| Setting | Registry Name | Path |
|---------|--------------|------|
| Location | LetAppsAccessLocation | HKLM\...\AppPrivacy |
| Camera | LetAppsAccessCamera | HKLM\...\AppPrivacy |
| Microphone | LetAppsAccessMicrophone | HKLM\...\AppPrivacy |
| Notifications | LetAppsAccessNotifications | HKLM\...\AppPrivacy |
| Account Info | LetAppsAccessAccountInfo | HKLM\...\AppPrivacy |
| Contacts | LetAppsAccessContacts | HKLM\...\AppPrivacy |
| Calendar | LetAppsAccessCalendar | HKLM\...\AppPrivacy |
| Call History | LetAppsAccessCallHistory | HKLM\...\AppPrivacy |
| Email | LetAppsAccessEmail | HKLM\...\AppPrivacy |
| Messaging | LetAppsAccessMessaging | HKLM\...\AppPrivacy |
| Phone | LetAppsAccessPhone | HKLM\...\AppPrivacy |
| Radios | LetAppsAccessRadios | HKLM\...\AppPrivacy |
| Other Devices | LetAppsSyncWithDevices | HKLM\...\AppPrivacy |
| Trusted Devices | LetAppsAccessTrustedDevices | HKLM\...\AppPrivacy |
| Background | LetAppsRunInBackground | HKLM\...\AppPrivacy |
| Motion | LetAppsAccessMotion | HKLM\...\AppPrivacy |
| Tasks | LetAppsAccessTasks | HKLM\...\AppPrivacy |
| Diagnostics | LetAppsGetDiagnosticInfo | HKLM\...\AppPrivacy |
| Voice (normal) | LetAppsActivateWithVoice | HKLM\...\AppPrivacy |
| Voice (lock) | LetAppsActivateWithVoiceAboveLock | HKLM\...\AppPrivacy |

**Base Path:** `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy`
**Values:** 0 = User control, 1 = Force Allow, 2 = Force Deny

---

## Appendix B: Recommended Privacy Tool Configuration Tiers

### Tier 1 - Basic Privacy (Safe for All Users)
- Disable Advertising ID
- Set telemetry to Required/Basic (Level 1)
- Disable Tailored Experiences
- Disable feedback notifications
- Set feedback frequency to Never
- Disable activity history
- Disable online speech recognition

### Tier 2 - Enhanced Privacy (Moderate Restrictions)
All of Tier 1, plus:
- Disable Windows Spotlight
- Disable consumer features
- Disable news/interests feeds
- Block web search in Start menu
- Disable Cross Device Platform
- Disable Cortana (Windows 10)
- Deny app diagnostics access

### Tier 3 - Maximum Privacy (May Affect Functionality)
All of Tier 2, plus:
- Deny all app permissions globally (location, camera, microphone, etc.)
- Disable background apps
- Disable Find My Device
- Disable location services entirely
- Block all library access (documents, pictures, videos, music)
- Disable inking and typing data collection

### NOT Recommended
- Disabling DiagTrack service (breaks Windows Update)
- Setting telemetry to Level 0 on non-Enterprise editions
- Blocking authentication endpoints (login.live.com)
- Blocking settings-win.data.microsoft.com

---

## Appendix C: Sources

- Microsoft Learn: [Configure Windows diagnostic data in your organization](https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization)
- Microsoft Learn: [Diagnostic Data Viewer](https://learn.microsoft.com/en-us/windows/privacy/diagnostic-data-viewer-overview)
- Microsoft Learn: [Manage connections from Windows operating system components to Microsoft services](https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services)
- Microsoft Learn: [Configure Windows spotlight](https://learn.microsoft.com/en-us/windows/configuration/windows-spotlight/)
- Microsoft Learn: [What's new in Windows 11, version 22H2](https://learn.microsoft.com/en-us/windows/whats-new/whats-new-windows-11-version-22h2)
- Microsoft Learn: [What's new in Windows 11, version 23H2](https://learn.microsoft.com/en-us/windows/whats-new/whats-new-windows-11-version-23h2)
- Microsoft Privacy Statement: https://www.microsoft.com/privacy/privacystatement
