# Catalogue expansion — triage of the 78-item backlog

**Date:** 2026-10-02
**Scope:** Sub-project 3 of the Novimize rework — "add 78 validated new tweaks", triaged
before a single definition is written.

The brief says *"Do NOT force all 78 into the application if research proves some are
obsolete, incompatible, placebo, unsafe, or meaningless. But target 78 validated
additions."* This document is where that judgement is recorded, item by item, so a
later reader can see what was declined and why rather than inferring it from absence.

## How the targets were verified

Three sources, in descending order of authority, all available offline on the machine
the catalogue is developed against (Windows 11 Pro, build 26200):

1. **ADMX policy definitions** — `C:\Windows\PolicyDefinitions\*.admx` (224 files,
   3,486 policies). Microsoft's own machine-readable statement of *this policy writes
   this value under this key, with these enable/disable values*. The default XML
   namespace differs per file, so it is read from each root rather than assumed.
   Where a policy is element-based (`<elements>` carrying the `valueName`), the
   element is walked rather than the `<policy>` attribute.
2. **Live registry state** — `reg query` on the candidate keys. A value that is
   present on a real install proves the name and the hive are right. An *absent*
   value is not a negative: Windows writes defaults only after they are changed, and
   `RegistryProvider.DetectAsync` already treats "absent" as `NotApplied` when the
   spec declares an `expectedDefault`. Rollback deletes the value rather than
   restoring a value that never existed.
3. **PE string tables** — the value name read as ASCII or UTF-16LE out of
   `explorer.exe`, `Shell32.dll`, `sysdm.cpl` and the Shell apps. This is what
   settles contested names: `ShowSyncProviderNotifications` and `SyncProviderNotificationDisable`
   are both in circulation, and only the first appears in `Shell32.dll`.

Web search and the browser were unavailable this session; nothing below rests on a
recollection that could not be checked against one of these three. Items that could
not be checked are declined rather than included on confidence.

## Verdicts

**27 new** · **23 already exist** · **14 belong to a later sub-project** · **14 declined**.

### Already in the catalogue (23)

Deduplication is against `tweaks/*.json` — 58 definitions, read directly, not from
memory of what was written.

| # | Backlog item | Existing definition |
|--:|--------------|---------------------|
| 2 | Disable unnecessary animation effects | `visual animations` (`MinAnimate=0`) |
| 4 | Reduce menu show delay | `visual.menuDelay` (`MenuShowDelay=50`) |
| 7 | Minimize/maximize animation | `visual animations` (same value as 2) |
| 16 | Remove unnecessary Explorer suggestions | same mechanism as 21 below |
| 25 | NTFS behaviour with evidence | `storage.ntfs.lastAccess.disabled` |
| 31 | Search suggestions | same policy as 26 below |
| 38 | Desktop system icons | same CLSID set as 37 |
| 41 | Advertising ID | `privacy.advertisingId.disabled` |
| 42 | App launch tracking | `privacy.appLaunchTracking.disabled` |
| 43 | Activity history | `privacy.activityHistory.disabled` |
| 44 | Diagnostic data level | `privacy.telemetry.level` |
| 48 | Cloud content suggestions | `privacy.cloudSuggestions.disabled` |
| 51 | Handwriting personalisation data | `privacy.handwritingData.disabled` |
| 52 | Diagnostic prompt features | same policy as 45 below |
| 54 | Windows consumer experiences | `privacy.cloudSuggestions.disabled` (`DisableWindowsConsumerFeatures`) |
| 58 | Error reporting | `services.wersvc.config` |
| 59 | Usage-data collection | same knob as 44 |
| 63 | Game Mode | `gpu.gaming.gameMode` |
| 65 | Game DVR | `gpu.gaming.gameDvr` **and** `gpu.gaming.dvrDisablePolicy` |
| 66 | HAGS | `gpu.gaming.hags` |
| 68 | Fullscreen behaviour | `gpu.gaming.fullscreenOpt` |
| 69 | Gaming power plan | `cpu-power.plan.*` |
| 71 | Game Mode related policies | `gpu.gaming.gameMode` is literally `HKCU\Software\Microsoft\GameBar\AllowAutoGameMode` |

Six of these were the reason this triage exists: adding them again would have
produced two definitions writing the same value, which the batch planner would then
report as a conflict against itself.

### Added (27)

Each row is the mechanism, where it was confirmed, and the score it was given.

**visual-effects**

| Backlog | New id | Target | Confirmed by | Risk / evidence |
|--------:|--------|--------|--------------|-----------------|
| 3 | `visual.transparency.disabled` | `HKCU\…\Themes\Personalize\EnableTransparency=0` | ADMX-adjacent, live `=1`, string in `explorer.exe` | Safe / 5 |
| 5 | `visual.tooltipDelay` | `HKCU\Control Panel\Desktop\MouseHoverTimeout=50` (SZ) | SPI-settable desktop value; absent = default 400 | Safe / 3 |
| 8 | `visual.taskbarAnimations.disabled` | `HKCU\…\Explorer\Advanced\TaskbarAnimations=0` | live `=1` | Safe / 5 |
| 10 | `visual.fontSmoothing.disabled` | `HKCU\Control Panel\Desktop\FontSmoothingType=0` | live `=2`, written by Performance Options | Optional / 5 |

**explorer** — a new category. Backlog items 13–25 and 32–40 are shell/Folder-Options
settings with no home among the existing nine; `Category` is a free-form string and
`GetCategories()` derives from the data, so the only place that needs to know is
`categoryColors` in `Profiles.tsx` and the README table.

| Backlog | New id | Target | Confirmed by | Risk / evidence |
|--------:|--------|--------|--------------|-----------------|
| 13 | `explorer.fileExtensions.show` | `…\Explorer\Advanced\HideFileExt=0` | live `=0` | Recommended / 5 |
| 14 | `explorer.hiddenFiles.show` | `…\Explorer\Advanced\Hidden=1` | live `=1`, string in `Shell32.dll` | Optional / 5 |
| 17 | `explorer.start.recentDocs` | `…\Explorer\Advanced\Start_TrackDocs=0` | string in `Shell32.dll` | Recommended / 4 |
| 18 | `explorer.launchTo.thisPc` | `…\Explorer\Advanced\LaunchTo=2` | string in `Shell32.dll` | Optional / 4 |
| 20 | `explorer.compactMode` | `…\Explorer\Advanced\UseCompactMode=1` | string in `Shell32.dll` | Optional / 5 |
| 21 | `explorer.syncProviderNotifications.disabled` | `…\Explorer\Advanced\ShowSyncProviderNotifications=0` | string in `Shell32.dll`; the rival name is absent from every binary | Safe / 5 |
| 22 | `explorer.thumbnails.iconsOnly` | `…\Explorer\Advanced\IconsOnly=1` | live `=0`, Folder Options checkbox | Optional / 5 |
| 32 | `explorer.taskbar.widgets.disabled` | `…\Explorer\Advanced\TaskbarDa=0` | live `=0` | Optional / 5 |
| 33 | `explorer.newsAndInterests.disabled` | `HKLM\…\Policies\Microsoft\Dsh\AllowNewsAndInterests=0` | `NewsAndInterests.admx` | Recommended / 5 |
| 35 | `explorer.taskbar.alignLeft` | `…\Explorer\Advanced\TaskbarAl=0` | absent = default centre; Windows 11 taskbar setting | Optional / 4 |
| 40 | `explorer.classicContextMenu` | `HKCU\Software\Classes\CLSID\{86ca1aa0-…}\InprocServer32` default value `""`, `minBuild 22000` | build-gated, reversible, description says it is undocumented | Optional / 3 |

**privacy**

| Backlog | New id | Target | Confirmed by | Risk / evidence |
|--------:|--------|--------|--------------|-----------------|
| 26 | `privacy.searchWebSuggestions.disabled` | `HKCU\…\Policies\Windows\Explorer\DisableSearchBoxSuggestions=1` | `WindowsExplorer.admx` | Recommended / 5 |
| 34 | `privacy.startSuggestions.disabled` | `…\ContentDeliveryManager\SystemPaneSuggestionsEnabled=0` | live `=1` | Recommended / 4 |
| 45 | `privacy.feedbackPrompts.disabled` | `HKLM\…\Policies\Windows\DataCollection\DoNotShowFeedbackNotifications=1` | `FeedbackNotifications.admx` | Safe / 5 |
| 46 | `privacy.tips.disabled` | `HKLM\…\Policies\Windows\CloudContent\DisableSoftLanding=1` | `CloudContent.admx` | Safe / 5 |
| 47 | `privacy.tailoredExperiences.disabled` | `HKCU\…\Policies\Windows\CloudContent\DisableTailoredExperiencesWithDiagnosticData=1` | `CloudContent.admx` | Safe / 5 |
| 49 | `privacy.location.disabled` | `…\Policies\Windows\LocationAndSensors\DisableLocation=1` | `Sensors.admx`, both hives | Optional / 5 |
| 50 | `privacy.clipboardSync.disabled` | `HKLM\…\Policies\Windows\System\AllowCrossDeviceClipboard=0` | `OSPolicy.admx` | Safe / 5 |
| 60 | `privacy.backgroundApps.denied` | `HKLM\…\Policies\Windows\AppPrivacy\LetAppsRunInBackground=2` | `AppPrivacy.admx` (element-based `valueName`) | Optional / 5 |
| 61 | `privacy.silentAppInstalls.disabled` | `…\ContentDeliveryManager\SilentInstalledAppsEnabled=0` | live `=1` | Recommended / 4 |
| 62 | `privacy.thirdPartySuggestions.disabled` | `HKCU\…\Policies\Windows\CloudContent\DisableThirdPartySuggestions=1` | `CloudContent.admx` | Recommended / 5 |

**storage / gpu-gaming**

| Backlog | New id | Target | Confirmed by | Risk / evidence |
|--------:|--------|--------|--------------|-----------------|
| 24 | `storage.longPaths.enabled` | `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled=1` | `FileSys.admx`, live `=1` | Safe / 5 |
| 70 | `gpu.gaming.usbSelectiveSuspend.disabled` | powercfg subgroup `2a737441-…` setting `48e6b7a6-…`, index 0 | `powercfg /q` reports the subgroup, the friendly name, and both indices | Optional / 5 |

Risk assignments follow one rule: a change nobody can perceive keeps `Safe`; a change
that alters what the user sees or loses a function gets `Recommended`; a genuine
trade-off — power draw, Find My Device, background notifications — gets `Optional`
and therefore reaches opt-in rather than any profile's default set.

### Belongs to a later sub-project (14)

Not catalogue material — they are features with their own UI, state and failure modes.

| # | Item | Goes to |
|--:|------|---------|
| 27 | Search indexing locations | Phase 9 — maintenance |
| 28 | Exclude folders from indexing | Phase 9 |
| 29 | Rebuild the search index | Phase 9 |
| 30 | Restart/recover the Search service | Phase 9 |
| 53 | Privacy permissions matrix | Phase 9 — UI over per-app permissions |
| 55 | Diagnostic scheduled tasks | Phase 9 — needs upgrade-impact review |
| 56 | Telemetry scheduled tasks | Phase 9 |
| 72 | Reduce background load before games | Phase 4 — Gaming Center |
| 73 | Per-game performance profiles | Phase 4 |
| 74 | Detect installed game launchers | Phase 4 |
| 75 | Detect game folders automatically | Phase 4 |
| 76 | Game-specific presets | Phase 4 |
| 77 | Restore previous gaming state | Phase 4 |
| 78 | Session-temporary optimization mode | Phase 4 |

55 and 56 are deferred rather than declined: the `TaskScheduler` provider is already
wired, but disabling `Microsoft\Windows\Application Experience\*` tasks changes
upgrade-readiness reporting, and that deserves its own review instead of being
shipped inside a batch of registry tweaks.

### Declined (14)

Each of these was looked at and found to fail one of the brief's bars.

| # | Item | Why not |
|--:|------|---------|
| 1 | Balanced visual effects preset | `VisualFXSetting` is confirmed in `sysdm.cpl`, but Performance Options writes it *and* each component value together. Writing only the selector would report `Applied` while nothing changed — a placebo the brief forbids. |
| 6 | Window animation policy | No documented knob distinct from the `MinAnimate` already shipped (items 2 and 7). |
| 9 | Accent transparency | The same value as item 3. |
| 11 | Desktop composition | Composition cannot be turned off on Windows 10/11; DWM is always on. |
| 12 | Foreground/background scheduling | `Win32PrioritySeparation` — a hex bitmask whose effect is disputed and whose misconfiguration degrades the machine. Registry magic by the brief's definition. |
| 15 | Shortcut-arrow overlay | Needs `HKLM\…\Explorer\Shell Icons\29`, which does not exist on this machine and is undocumented in ADMX; it also requires an icon-cache rebuild and an Explorer restart before anything is visible, so verify-after-apply could not read the result back. |
| 19 | Folder view behaviour | No single mechanism; overlaps 18 and 20. |
| 23 | Thumbnail generation behaviour | No knob beyond item 22. |
| 36 | Taskbar item grouping | Not configurable on Windows 11. |
| 37 | Desktop icon visibility | Per-CLSID values under `HideDesktopIcons\NewStartPanel` — a set, not one value. Fitting it into a single-value tweak would apply part of it silently. Appearance tab instead. |
| 39 | Context-menu behaviour | Vague; resolves to item 40. |
| 57 | Optional telemetry services | `DiagTrack` is already `services.diagtrack.config`; the other candidate, the Diagnostic Policy Service, must not be disabled. |
| 64 | Game Bar | No ADMX policy exists (`grep` over 224 files finds no `GameBar` policy), and `HKCU\Software\Microsoft\Windows\CurrentVersion\AppCapture` is absent. Nothing to detect against. |
| 67 | Windowed-game optimization | No confirmed mechanism: `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` on this machine carries `HwSchMode` and `ShaderCacheEnabled` but nothing matching the windowed-game toggle. Deferred pending a source that names the value. |

## A defect found during triage

`visual.transparentEffects` is named *"Transparency Effects — Windows
transparency/acrylic effects. Disabling improves GPU performance on low-end
hardware"*, sets `targetValue: 1`, and writes
`HKLM\…\Explorer\Advanced\UseOLEDTaskbarTransparency`.

Three things are wrong with that at once:

1. It writes the **opposite polarity** to what its description claims (the tweak
   *enables* rather than disables).
2. `UseOLEDTaskbarTransparency` is the OLED taskbar-opacity knob, not the transparency
   switch. The transparency switch is `Themes\Personalize\EnableTransparency`, which
   item 3 now covers properly.
3. The value does **not exist** on this machine and does not appear as a string in
   `explorer.exe`, `Shell32.dll`, or any Shell app — so on this build the tweak
   creates a value nothing reads, and would report `Applied` for a change that did
   nothing.

The id is left alone (snapshots key on it, and renaming would orphan every rollback
already recorded), but the name, description, evidence and risk are rewritten to
describe what the value actually does, and the evidence drops to 2 — which, under the
profile policy, moves it out of every default set and into opt-in where a
questionable-value tweak belongs.

## Wiring

- **New category `explorer`**, added to the `office` and `potato-pc` profiles'
  `IncludeCategories` (and picked up automatically by `daily`, whose include list is
  empty and therefore means *all*). Gaming, streaming, developer, workstation and
  battery-saver do not reach it — deliberately, since most of it is view preference.
- **`categoryColors`** in `Profiles.tsx` gains an entry; the fallback is `#888`.
- **README** table gains the category and the counts move 58 → 85.

## Not in scope

Gaming Center (Phase 4), App Installer/WinGet (5), Appearance (6), Blocker (7),
Network/Power/Startup/Services/Tasks (8), Debloat/Maintenance/Windows Update/
Diagnostics/Health (9), activation/updater/polish/testing/docs (10).
