# WinOpt Auto-Optimize System

## Overview

Auto-Optimize automatically applies optimizations that meet strict safety criteria. It is NOT "apply everything." It is a carefully curated set of changes that are universally safe, easily reversible, and broadly beneficial.

## Safety Criteria for Auto-Apply

A tweak qualifies for automatic application ONLY if it meets ALL of these criteria:

### Hard Requirements (all must pass)
1. **Risk = SAFE or RECOMMENDED** — Never RISKY, DANGEROUS, or EXPERIMENTAL
2. **Evidence ≥ 3** — At least plausible with some supporting evidence
3. **Easily Reversible** — Rollback method is reliable and tested
4. **State Detectable** — Can reliably detect current state before changing
5. **No Security Reduction** — Does not weaken Defender, Firewall, UAC, SmartScreen, or other protections
6. **No Functionality Loss** — Does not remove a feature the user likely needs
7. **Deterministic Outcome** — Same input → same output, no hardware-dependent surprises
8. **Windows-Approved** — Setting is documented or at least not discouraged by Microsoft

### Soft Requirements (weighted scoring)
- Benefit is broadly applicable across hardware configurations
- Low reboot requirement (prefer no reboot needed)
- Fast apply/rollback cycle
- No dependency on specific drivers
- Works on both Desktop and Laptop
- Works on both Windows 10 and 11

## Auto-Optimize Modes

### AUTO SAFE
The most conservative mode. Only universally safe changes.

**Qualification criteria:**
- Risk = SAFE only
- Evidence ≥ 4
- No hardware dependency
- No reboot required
- Applies to both Desktop and Laptop
- No battery life impact
- No security impact

**Example tweaks:**
- Menu animation delay reduction
- Visual effects optimization (basic)
- Explorer behavior improvements (show file extensions, etc.)
- Non-destructive cleanup (temp files, thumbnail cache)
- DNS cache flush
- Windows Search index optimization (not removal)
- Taskbar settings (Windows 11)

### AUTO BALANCED
Safe + recommended changes.

**Qualification criteria:**
- Risk = SAFE or RECOMMENDED
- Evidence ≥ 3
- May be hardware-dependent but with safe defaults
- Reboot acceptable if needed
- No security reduction
- Battery life impact must be documented

**Additional tweaks beyond AUTO SAFE:**
- Power plan optimization (balanced/high-performance based on device type)
- TCP auto-tuning normalization
- NIC basic optimization
- Wi-Fi power management optimization
- Service optimization (safe-only services)
- Startup optimization (clearly unnecessary items)
- Storage optimization (TRIM, write caching)
- Windows Update configuration
- Delivery Optimization limits

### AUTO GAMING
Gaming-focused optimizations.

**Qualification criteria:**
- All AUTO SAFE criteria
- Gaming-specific evidence ≥ 3
- User has confirmed gaming use case
- Desktop preferred (gaming laptops need extra care)

**Additional gaming tweaks:**
- Game Mode ON
- Game DVR configuration
- GPU scheduling optimization (HAGS where supported)
- MMSCSS SystemResponsiveness tuning
- Power plan gaming optimization
- Network latency tweaks (gaming-relevant)
- Input latency optimization (mouse, keyboard repeat rate)

### AUTO NETWORK
Network-specific optimization.

**Qualification criteria:**
- Risk = SAFE or RECOMMENDED
- Evidence ≥ 3
- Network-related only
- No security impact on firewall
- No impact on other system functions

**Network tweaks:**
- TCP auto-tuning normalization
- Congestion control provider selection
- NIC advanced property optimization (safe settings)
- Wi-Fi power management
- DNS cache optimization
- MTU verification
- Delivery Optimization bandwidth
- Network profile verification

### AUTO CLEANUP
Non-destructive cleanup only.

**Qualification criteria:**
- Only removes files that Windows can recreate
- Never removes user data
- Never removes driver files
- Never removes system files
- Only removes caches, temp files, thumbnails, crash dumps

**Cleanup targets:**
- Windows Temp folder
- User Temp folder
- Thumbnail cache
- DNS cache
- Windows Update download cache
- Delivery Optimization cache
- Crash dump files (old)
- Windows Error Reports (old)
- Recycle Bin (if user confirms)

## Safety Guards

### Pre-Execution Checks
Before auto-optimize runs:

1. **System State Validation**
   - Verify Windows is in a healthy state
   - Check disk space (need minimum free space for snapshots)
   - Check if any system update is in progress
   - Check if any critical service is in a bad state
   - Check if battery is low (laptop — don't apply power tweaks on low battery)

2. **Snapshot Creation**
   - ALWAYS create a full snapshot before any auto-optimize run
   - Store snapshot with trigger type for audit trail

3. **Conflict Check**
   - Run conflict engine before applying
   - Block if any unsafe conflict is detected

4. **User Confirmation (when required)**
   - Show dry-run results before applying
   - Allow user to deselect individual tweaks
   - Show risk level and evidence score for each

### Execution Guards
During auto-optimize:

1. **Order of Operations**
   - Apply in provider order (Registry → Power → Services → etc.)
   - Wait for each apply to complete before next
   - If any tweak fails, stop and ask user whether to continue

2. **Reboot Management**
   - Batch reboot-required changes together
   - Ask user to reboot rather than forcing
   - Track pending reboot changes in snapshot

3. **Rollback on Failure**
   - If verify fails for any tweak, automatic rollback of that tweak
   - Continue with remaining tweaks unless the failure indicates a systemic problem

### Post-Execution Checks
After auto-optimize:

1. **Verification Sweep**
   - Re-verify all applied tweaks
   - Update snapshot with verification results
   - Show summary to user

2. **System Health Check**
   - Quick system health check after changes
   - Verify critical services are running
   - Verify network connectivity
   - Verify no BSOD or crash (check event log)

## Dry Run System

Before any auto-optimize execution, the user can preview exactly what will change:

```json
{
  "dryRun": {
    "mode": "auto-balanced",
    "totalCandidates": 47,
    "filteredOut": {
      "alreadyApplied": 12,
      "notApplicable": 8,
      "conflictBlocked": 2,
      "belowThreshold": 5
    },
    "willApply": [
      {
        "tweakId": "network.tcpip.autoTuning",
        "name": "TCP Auto-Tuning",
        "currentState": "disabled",
        "targetState": "normal",
        "risk": "SAFE",
        "evidence": 4,
        "rebootRequired": false,
        "estimatedImpact": "Improved TCP throughput"
      }
    ],
    "estimatedDuration": "30 seconds",
    "estimatedReboots": 0,
    "totalRisk": "LOW"
  }
}
```

## Logging

Every auto-optimize run produces a log entry:

```json
{
  "runId": "uuid",
  "mode": "auto-balanced",
  "startedAt": "2026-08-31T12:00:00Z",
  "completedAt": "2026-08-31T12:00:30Z",
  "snapshotId": "snapshot-uuid",
  "totalTweaks": 25,
  "applied": 23,
  "skipped": 2,
  "failed": 0,
  "rolledBack": 0,
  "results": [
    {
      "tweakId": "...",
      "status": "applied | skipped | failed",
      "reason": "..."
    }
  ]
}
```
