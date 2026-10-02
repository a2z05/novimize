# Gaming Center and Game Folder Exclusions — design

**Date:** 2026-10-02
**Scope:** Phase 4 of the Novimize rework — *Gaming Center, game folders, Defender
exclusions, temporary game mode* (brief §8 and §9, plus backlog items 72–78).

Phase 3 ended with a catalogue of 85 tweaks, all of them **permanent** changes that a
snapshot can undo. Phase 4 adds the two things that catalogue cannot express: a change
that lasts only as long as a game does, and a change that takes Windows Defender out of
the picture for one folder. Both are the kind of thing a wrong answer makes worse than
doing nothing, so both are built around capture-first, restore-never-guess.

## What was verified, and how

The same three offline sources Phase 3 used, applied to every mechanism below. Nothing
here is implemented on the strength of recall.

| Mechanism | Source | Result |
|---|---|---|
| Steam library path | live `reg query` | `HKCU\Software\Valve\Steam\SteamPath` = `c:/program files (x86)/steam` ✓ |
| Steam game list | live `libraryfolders.vdf` + `appmanifest_*.acf` | both present; ACF carries `name` / `installdir` ✓ |
| Ubisoft launcher | live `reg query` | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\InstallDir` ✓ |
| Epic / GOG / EA / Battle.net | — | **not installed here**, so their registry keys cannot be confirmed. They are probed by path *and* registry and reported with the evidence that matched; a launcher that matches nothing is reported as absent, never as found. |
| Toast suppression | `WPN.admx` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\PushNotifications` / `NoToastApplicationNotification`, enabled `1`, disabled `0` ✓ |
| Background apps | `AppPrivacy.admx` | `LetAppsRunInBackground`, values `0/1/2` where `2` = Force Deny ✓ (already shipped as `privacy.backgroundApps.denied`) |
| Power plan | `powercfg /getactivescheme`, `/setactive` | active scheme reported as `8c5e7fda-… (High performance)` ✓ |
| Defender exclusions | `Get/Add/Remove-MpPreference` | cmdlets present (module `ConfigDefender`) ✓ — and **reading exclusions needs administrator**, returning the literal string `N/A: Must be an administrator to view exclusions` rather than an error or an empty list |

That last row is the one that would have shipped a bug: the string parses as a list of
one path. `Get-MpPreference` output is checked for it explicitly and reported as
"elevation required".

## A. Gaming Center

### Detection

Three layers, each reporting the evidence that found it rather than a bare claim:

1. **Launchers** — probed by registry key *and* well-known path. A launcher is reported
   only when something actually matched, and the entry records which probe hit
   (`registry:…` or `filesystem:…`). Uninstalled launchers are simply absent from the
   result.
2. **Libraries** — Steam reads `libraryfolders.vdf` (every `path` entry, including
   libraries on other drives); other launchers contribute their install directory.
3. **Games** — where the launcher publishes an authoritative list (Steam's
   `appmanifest_*.acf` gives `name` and `installdir`), that list is used. Where it does
   not, a folder under a library that contains an executable is reported as a
   **candidate**, labelled as a candidate. The UI never says "installed games" for
   something it found by walking a directory.

Folders the user adds by hand are scanned the same way and marked `manual`.

### Temporary game mode

`game-mode start` records what it is about to change, changes it, and writes the
session to `%LOCALAPPDATA%\WinOpt\gamemode\current.json`. `game-mode stop` restores
from that file in reverse order and reports each line separately — a restore that fails
is reported as a failure, not folded into a success.

Controls, all of them capture-first:

| Control | What it writes | Restored by |
|---|---|---|
| power plan | `powercfg /setactive <GUID>` after recording `/getactivescheme` | setting the recorded scheme back |
| toast notifications | `PushNotifications\NoToastApplicationNotification` + `NoCloudApplicationNotification` = `1` (HKLM) | writing the recorded values, or deleting the value if it was absent |
| background apps | `LetAppsRunInBackground` = `2` (Force Deny, HKLM) | same |
| services | `Stop-Service` for the length of the session, only for services `SecurityBoundaries` already allows; the start type is not touched | `Start-Service`, only when the service was running when it was captured |
| process priority | `PriorityClass` on the running instances of a named process | no restore needed — priority is process-scoped and dies with the game |

Two rows are HKLM: `PushNotifications` and `AppPrivacy` are both `class="Machine"`
policies in their ADMX files, so the HKCU spelling does not exist. Those two rows and
services are therefore **refused before capture** when the process is not elevated —
they never enter the session, because a control whose `before` could not be read is a
control stop could never put back.

Services record start/stop, not start type. Writing `Start` would be a change that
outlives the session — a permanent edit in a feature whose whole claim is that it goes
away — and it needs the same elevation the stop already needs.

**Deliberately not implemented**, with reasons recorded rather than left as a gap:

- *Background process reduction* — suspending or killing arbitrary third-party
  processes is not cleanly reversible and there is no documented mechanism for it.
- *Focus Assist / quiet hours* — the live state is an opaque binary blob under
  `CloudStore\…\windows.data.donotdisturb.*`, which is exactly the "registry magic
  where the purpose cannot be explained" the brief rules out. `NoQuietHours` in
  `WPN.admx` exists but its polarity is inverted (enabling the policy turns quiet hours
  *off*), so it does not express "suppress notifications".
- *Notification suppression of a permanent kind* — a game-mode control that survives
  the game is a bug, so every control above has an explicit `before` value.

The session records `ownerProcess` when `--for-process` is given. `status` reports
whether that process is still running, so the caller can decide to stop; nothing
pretends to watch a process in the background, because a CLI that blocks is a CLI the
UI cannot talk to.

Never destructive: a session refuses to start over a live one rather than overwriting
the `before` values it would need to restore, and `stop` is idempotent.

### Presets

A preset is a named set of controls, saved against a game path (`game-mode preset
save|apply|list|delete`). It stores only the *intent* (which controls, with what
targets) — never a captured `before` value, because a `before` captured last month is
not this machine's state today.

## B. Game folder exclusions

`defender list|add|remove|export`, using the documented `Get-MpPreference`,
`Add-MpPreference`, `Remove-MpPreference`.

Gates, in the order they are applied:

1. **Elevation** — refused with the reason before anything else; the read path detects
   the `N/A: Must be an administrator…` string.
2. **Confirmation** — `add` requires `--confirm`, and prints the exact, fully resolved
   path first. The UI modal carries the same path plus the standing warning:
   *Defender will not scan excluded content.* The feature is never called "disable
   antivirus".
3. **Existence** — the folder must exist.
4. **Path validation** — refused for system locations: the Windows directory and its
   subtrees, Program Files and Program Files (x86), the user profile root, drive roots,
   and anything on a network or UNC path. Preferred roots are game libraries, but
   anything that is not a blocked system path is allowed — a user's own `D:\Games` has
   no reason to be second-class.
5. **Rollback** — the pre-change exclusion list is written to the session journal
   before the add, `remove` is its own inverse, and `export` writes the list to a file
   that can be re-applied.

Adding an exclusion that is already present is a no-op reported as such, not an error
and not a duplicate write.

## Wiring

- **Engine**: `src/engine/Gaming/{LauncherDetector,SteamLibrary,GameFolderScanner,GameModeManager}.cs`,
  `src/engine/Security/DefenderExclusions.cs`.
- **Models**: `src/core/Models/GameMode.cs`, `src/core/Models/DefenderExclusion.cs`.
- **CLI**: `game-mode` and `defender` commands alongside the existing ones, both with
  `--json` because the desktop app is a shell over the CLI.
- **Tauri**: eight commands registered in `main.rs`, following `run_cli_json`.
- **UI**: `Gaming.tsx` (detection, folders, session, presets) and `Exclusions.tsx`
  (Defender), both routed and linked from the sidebar.
- **Journal**: `WinOptLogger.AuditFeature` — a game-mode control or an exclusion is a
  change this machine made, so it lands in the same journal as a tweak apply rather
  than in a log only this feature can read.

## Testing

Parser tests for the VDF/ACF reader (round-trip, and the case where a value contains
escaped backslashes); session capture → restore against a throwaway HKCU key; path
validation tests for the blocked system locations; and a check that no control can be
recorded without a `before` value, which is the property the whole restore path rests
on.
