# WinOpt Snapshot & Rollback System

## Snapshot Format

Every optimization operation creates a snapshot BEFORE any changes are made. Snapshots are the foundation of the rollback system.

### Snapshot Schema

```json
{
  "snapshot": {
    "id": "uuid-v4",
    "timestamp": "2026-08-31T12:00:00Z",
    "appVersion": "1.0.0",
    "sessionType": "manual | auto | preset",
    "windowsVersion": {
      "major": 10,
      "minor": 0,
      "build": 22631,
      "revision": 0,
      "displayVersion": "23H2",
      "edition": "Pro",
      "architecture": "x64"
    },
    "machineInfo": {
      "computerName": "DESKTOP-XXXX",
      "cpu": "AMD Ryzen 9 7950X",
      "gpu": "NVIDIA GeForce RTX 4090",
      "ramMB": 32768,
      "primaryDisk": "NVMe Samsung 990 Pro 2TB",
      "activePowerPlan": "High Performance",
      "activeNetworkProfile": "Private"
    },
    "trigger": "user | auto-safe | auto-balanced | auto-gaming | auto-network | auto-cleanup | preset",
    "changes": [
      {
        "tweakId": "network.tcpip.autoTuning",
        "stateBefore": "disabled",
        "stateAfter": "normal",
        "target": "netsh global autotuning",
        "oldState": {
          "method": "netsh",
          "rawOutput": "TCP Auto-Tuning Level          : disabled",
          "parsedValue": "disabled",
          "originalExists": true
        },
        "newState": {
          "method": "netsh",
          "rawOutput": "TCP Auto-Tuning Level          : normal",
          "parsedValue": "normal"
        },
        "appliedAt": "2026-08-31T12:00:05Z",
        "verificationState": "SUCCESS | FAILED | PENDING",
        "verificationAt": "2026-08-31T12:00:06Z",
        "rollbackData": {
          "type": "command",
          "command": "netsh int tcp set global autotuninglevel=disabled",
          "description": "Restore original auto-tuning level"
        },
        "rebootRequired": false,
        "rebootPending": false
      },
      {
        "tweakId": "services.sysmain.config",
        "stateBefore": "running",
        "stateAfter": "disabled",
        "target": "Service: SysMain",
        "oldState": {
          "method": "service",
          "rawOutput": "START_TYPE : 2 AUTO_START",
          "parsedValue": "2",
          "originalExists": true
        },
        "newState": {
          "method": "service",
          "rawOutput": "START_TYPE : 4 DISABLED",
          "parsedValue": "4"
        },
        "appliedAt": "2026-08-31T12:00:07Z",
        "verificationState": "SUCCESS",
        "verificationAt": "2026-08-31T12:00:08Z",
        "rollbackData": {
          "type": "service",
          "service": "SysMain",
          "previousStartType": 2,
          "previousStatus": "Running",
          "command": "sc.exe config SysMain start= auto && sc.exe start SysMain"
        },
        "rebootRequired": false,
        "rebootPending": false
      },
      {
        "tweakId": "registry.visual.menuDelay",
        "stateBefore": "400",
        "stateAfter": "50",
        "target": "HKCU\\Control Panel\\Desktop\\MenuShowDelay",
        "oldState": {
          "method": "registry",
          "hive": "HKEY_CURRENT_USER",
          "path": "Control Panel\\Desktop",
          "name": "MenuShowDelay",
          "type": "REG_SZ",
          "value": "400",
          "originalExists": true
        },
        "newState": {
          "method": "registry",
          "hive": "HKEY_CURRENT_USER",
          "path": "Control Panel\\Desktop",
          "name": "MenuShowDelay",
          "type": "REG_SZ",
          "value": "50"
        },
        "appliedAt": "2026-08-31T12:00:09Z",
        "verificationState": "SUCCESS",
        "verificationAt": "2026-08-31T12:00:09Z",
        "rollbackData": {
          "type": "registry",
          "hive": "HKEY_CURRENT_USER",
          "path": "Control Panel\\Desktop",
          "name": "MenuShowDelay",
          "type": "REG_SZ",
          "value": "400",
          "originalExisted": true
        },
        "rebootRequired": false,
        "rebootPending": false
      }
    ],
    "summary": {
      "totalChanges": 3,
      "successful": 2,
      "failed": 0,
      "pendingReboot": 0,
      "rolledBack": 0
    }
  }
}
```

## Snapshot Storage

### Location
```
%LOCALAPPDATA%\WinOpt\snapshots\
├── {snapshot-id-1}.json
├── {snapshot-id-2}.json
├── ...
└── index.json
```

### Index File
```json
{
  "snapshots": [
    {
      "id": "uuid-1",
      "timestamp": "2026-08-31T12:00:00Z",
      "trigger": "user",
      "summary": { "totalChanges": 3, "successful": 3 },
      "canRollback": true
    }
  ],
  "retentionPolicy": {
    "maxSnapshots": 100,
    "maxAgeDays": 90,
    "autoDeleteOnRollback": false
  }
}
```

## Rollback Strategies

### Strategy 1: Registry Rollback

**Scenario: Value existed before**
```
Old: Registry key "MenuShowDelay" = "400" (REG_SZ)
New: Registry key "MenuShowDelay" = "50" (REG_SZ)
Rollback: Set "MenuShowDelay" back to "400" (REG_SZ)
```

**Scenario: Value did NOT exist before**
```
Old: Registry key "MenuShowDelay" did not exist
New: Registry key "MenuShowDelay" = "50" (REG_SZ)
Rollback: REMOVE "MenuShowDelay" entirely
```

**Implementation:**
```csharp
// Rollback logic for registry
if (rollbackData.originalExisted)
{
    // Restore original value and type
    RegistryKey.SetValue(rollbackData.path, rollbackData.name, rollbackData.value, rollbackData.type);
}
else
{
    // Value was created by us — remove it
    RegistryKey.DeleteValue(rollbackData.name, throwOnMissingValue: false);
}
```

### Strategy 2: Service Rollback

**Scenario: Service was running, we disabled it**
```
Old: SysMain start=2 (AUTO), status=Running
New: SysMain start=4 (DISABLED), status=Stopped
Rollback: sc.exe config SysMain start=auto && sc.exe start SysMain
```

**Scenario: Service was manual, we disabled it**
```
Old: SysMain start=3 (MANUAL)
New: SysMain start=4 (DISABLED)
Rollback: sc.exe config SysMain start=demand
```

**Key rule:** Store the exact previous StartType AND running status. Restore both.

### Strategy 3: Power Plan Rollback

**Scenario: Changed active power plan**
```
Old: Active plan = "High Performance" (GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c)
New: Active plan = "Ultimate Performance" (GUID: e9a42b02-d5df-448d-aa00-03f14749eb61)
Rollback: powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
```

### Strategy 4: Service Configuration Rollback

**Scenario: Modified powercfg sub-indices**
```
Old: powercfg /getacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN → 5
New: powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN → 100
Rollback: powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN → 5
         powercfg /setactive SCHEME_CURRENT  (apply changes)
```

### Strategy 5: netsh Rollback

**Scenario: Changed TCP global parameter**
```
Old: netsh int tcp show global → autotuninglevel: normal
New: netsh int tcp set global autotuninglevel=disabled
Rollback: netsh int tcp set global autotuninglevel=normal
```

### Strategy 6: Windows Feature Rollback

**Scenario: Removed a Windows feature**
```
Old: DISM /Online /Get-Features → WindowsMediaPlayer: Enabled
New: DISM /Online /Disable-Feature /FeatureName:WindowsMediaPlayer
Rollback: DISM /Online /Enable-Feature /FeatureName:WindowsMediaPlayer /NoRestart
```

### Strategy 7: Firewall Rule Rollback

**Scenario: Created a firewall rule**
```
Old: Rule did not exist
New: Rule "WinOpt_BlockPortX" created
Rollback: Remove-NetFirewallRule -Name "WinOpt_BlockPortX"
```
**Key rule:** Only remove rules that WinOpt created. Use a naming convention (WinOpt_ prefix) to identify our rules.

### Strategy 8: AppX Removal Rollback

**Scenario: Removed a provisioned/installed app**
```
Old: Get-AppxPackage *Calculator* → Found
New: Remove-AppxPackage -Package *Calculator*
Rollback: Add-AppxPackage -Register "C:\Program Files\WindowsApps\*\AppxManifest.xml" -DisableDevelopmentMode
```
**Note:** Reinstalling AppX apps after removal can be complex. Store the package full name and use:
```powershell
# For provisioned packages (re-provision)
Add-AppxProvisionedPackage -Online -PackagePath "{path}" -LicensePath "{path}"

# For user packages (reinstall from Store or provisioned)
Get-AppxPackage -AllUsers | Where-Object {$_.Name -like "*Calculator*"}
```

## Rollback Levels

### Per-Tweak Rollback
Rollback a single tweak to its previous state.
- Uses the specific change entry in the snapshot
- Safest operation
- Can be done at any time

### Category Rollback
Rollback all tweaks within a category (e.g., all network tweaks).
- Iterates all changes matching the category
- Applies rollback for each
- Order: reverse of apply order

### Session Rollback
Rollback ALL changes from a single snapshot.
- Reverses every change in the snapshot
- Order: reverse of original apply order
- Most comprehensive rollback

### Full Restore
Return the system to its state before ANY WinOpt changes.
- Uses the earliest snapshot
- Chains rollback through all snapshots
- Creates a new snapshot of the rollback state

## Pending Reboot Handling

Some changes require a reboot. The snapshot tracks these:

```json
{
  "rebootRequired": true,
  "rebootPending": true,
  "pendingRebootAction": {
    "type": "registryPendingRename",
    "source": "C:\\path\\old.dll",
    "destination": "C:\\path\\new.dll"
  }
}
```

### On Next Launch After Reboot:
1. Check for incomplete snapshots
2. Verify pending reboot operations completed
3. Mark changes as verified
4. If any failed, offer to retry or rollback

## Snapshot Integrity

Each snapshot includes a checksum to detect corruption:
```json
{
  "checksum": "sha256:...",
  "checksumValid": true
}
```

## System Restore Integration

Before applying risky changes, optionally create a Windows System Restore point:
```powershell
Enable-ComputerRestore -Drive "C:\"
Checkpoint-Computer -Description "WinOpt Pre-Apply" -RestorePointType "MODIFY_SETTINGS"
```

This provides a nuclear option if all else fails.

## Retention Policy

- Keep last 100 snapshots by default
- Auto-delete snapshots older than 90 days
- Never delete snapshots that contain un-reverted changes
- User can manually delete snapshots
- Export snapshots for archival
