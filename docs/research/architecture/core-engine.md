# WinOpt Core Engine Architecture

## Technology Stack

- **Primary Engine:** C# / .NET 8+ (class library, runs as local helper process)
- **UI Shell:** Rust + Tauri (thin binary, web-based UI, communicates with .NET engine via IPC)
- **IPC Layer:** Tauri command bridge → .NET stdin/stdout or named pipe
- **CLI:** Rust (thin wrapper calling the same .NET engine)

## Process Architecture

```
┌─────────────────────────────────┐
│         Tauri UI (Rust)         │
│   ┌───────────────────────────┐ │
│   │   Web UI (HTML/CSS/JS)    │ │
│   └───────────┬───────────────┘ │
│               │ Tauri Commands   │
│   ┌───────────▼───────────────┐ │
│   │    Rust IPC Bridge        │ │
│   └───────────┬───────────────┘ │
└───────────────┼─────────────────┘
                │ Named Pipe / stdio
┌───────────────▼─────────────────┐
│     .NET Engine (Helper)        │
│  ┌──────────────────────────┐   │
│  │    Core Engine            │   │
│  │  ┌──────────────────┐    │   │
│  │  │ System Detection │    │   │
│  │  │ Hardware Detect  │    │   │
│  │  │ State Engine     │    │   │
│  │  │ Snapshot Engine  │    │   │
│  │  │ Rollback Engine  │    │   │
│  │  │ Verify Engine    │    │   │
│  │  │ Recommend Engine │    │   │
│  │  │ Conflict Engine  │    │   │
│  │  │ Logging Engine   │    │   │
│  │  └──────────────────┘    │   │
│  │  ┌──────────────────┐    │   │
│  │  │ Provider Registry │    │   │
│  │  │  ├─ Registry      │    │   │
│  │  │  ├─ Power         │    │   │
│  │  │  ├─ Network       │    │   │
│  │  │  ├─ Services      │    │   │
│  │  │  ├─ Startup       │    │   │
│  │  │  ├─ Storage       │    │   │
│  │  │  ├─ Privacy       │    │   │
│  │  │  ├─ Visual        │    │   │
│  │  │  ├─ Features      │    │   │
│  │  │  ├─ AppX          │    │   │
│  │  │  ├─ Cleanup       │    │   │
│  │  │  ├─ Graphics      │    │   │
│  │  │  ├─ Gaming        │    │   │
│  │  │  ├─ Bluetooth     │    │   │
│  │  │  ├─ USB           │    │   │
│  │  │  └─ ScheduledTasks│    │   │
│  │  └──────────────────┘    │   │
│  └──────────────────────────┘   │
└─────────────────────────────────┘
```

## Engine Lifecycle

1. **Startup** → Detect system state, hardware, OS version
2. **Load Tweak Definitions** → Parse JSON tweak database
3. **Filter by Compatibility** → Remove tweaks not applicable to current system
4. **Detect Current State** → For each applicable tweak, detect if already applied
5. **Recommend** → Score and rank tweaks based on hardware/OS/use-case
6. **User Interaction** → User selects tweaks or runs auto-optimize
7. **Dry Run** → Show what will change, current state, target state
8. **Snapshot** → Save current state before any changes
9. **Apply** → Execute changes in dependency order
10. **Verify** → Post-check each change
11. **Record** → Log all changes to snapshot

## Core Engine Classes

### SystemDetector
Responsibilities:
- Detect Windows version, build, edition, architecture
- Detect CPU, GPU, RAM, disks, network adapters
- Detect active power plan, network profile
- Detect installed software
- Detect current system state for all tweak categories
- Build SystemProfile object

### StateEngine
Responsibilities:
- For each tweak, call its detect() method
- Compare detected state with tweak's expected states
- Return StateReport: NOT_APPLIED, APPLIED, PARTIALLY_APPLIED, UNKNOWN, NOT_APPLICABLE
- Cache state for UI refresh without re-detection

### SnapshotEngine
Responsibilities:
- Create full system state snapshot before changes
- Serialize snapshot to JSON
- Store snapshot with unique ID, timestamp, metadata
- Load previous snapshots for rollback
- Manage snapshot retention policy

### RollbackEngine
Responsibilities:
- Execute per-tweak rollback using stored oldState
- Execute category rollback (all tweaks in a category)
- Execute session rollback (all changes from a snapshot)
- Execute full restore (return to pre-optimization state)
- Create System Restore point when configured
- Verify rollback succeeded

### VerifyEngine
Responsibilities:
- After apply, re-run detect() for each tweak
- Compare new state with expected state
- Return VerifyResult: SUCCESS, FAILED, PARTIAL
- If failed, trigger automatic rollback for that tweak

### RecommendationEngine
Responsibilities:
- Take SystemProfile as input
- Score each tweak based on:
  - Hardware compatibility
  - OS compatibility
  - Current state (skip if already optimized)
  - Evidence score
  - Risk level
  - User preference/preset
- Return ranked recommendation list

### ConflictEngine
Responsibilities:
- Check tweak dependencies (Tweak A requires Tweak B)
- Check tweak conflicts (Tweak A contradicts Tweak B)
- Check user-defined conflict groups
- Return ConflictReport with warnings and resolution suggestions

### LoggingEngine
Responsibilities:
- Structured logging for every operation
- Log levels: DEBUG, INFO, WARN, ERROR
- Persist logs to file with rotation
- Export capability for diagnostics
- Human-readable format with technical detail

## Provider System

### Provider Interface
Each provider implements:
```csharp
interface ITweakProvider
{
    string CategoryId { get; }
    string Name { get; }
    
    Task<List<TweakDefinition>> GetTweaksAsync();
    Task<TweakState> DetectStateAsync(TweakDefinition tweak);
    Task<ApplyResult> ApplyAsync(TweakDefinition tweak, TweakState currentState);
    Task<RollbackResult> RollbackAsync(TweakDefinition tweak, TweakState previousState);
    Task<bool> VerifyAsync(TweakDefinition tweak, TweakState expectedState);
}
```

### Provider Registry
- Providers register themselves at engine startup
- Engine iterates providers for detection and application
- New providers can be added without modifying core engine
- Providers can be loaded from DLL plugins (future)

### Provider Execution Order
1. Registry (foundational — many other providers depend on registry state)
2. Power (power plan affects all other subsystems)
3. Services (services affect feature availability)
4. Features (Windows features affect other tweaks)
5. AppX (app removal depends on features)
6. Startup (startup items depend on services)
7. Network (network tweaks are mostly independent)
8. Storage (storage tweaks are mostly independent)
9. Privacy (privacy tweaks are mostly independent)
10. Visual (visual tweaks are mostly independent)
11. Graphics (GPU tweaks depend on power plan)
12. Gaming (gaming tweaks depend on power, graphics, services)
13. Cleanup (cleanup should run last)
14. Bluetooth (independent)
15. USB (independent)
16. ScheduledTasks (depends on services)

## IPC Protocol

### Message Format (Rust ↔ .NET)
```json
{
  "id": "uuid",
  "method": "detectSystem | getTweaks | detectState | applyTweak | rollbackTweak | getSnapshot | runAutoOptimize | etc.",
  "params": { ... },
  "result": { ... },
  "error": null | { "code": "...", "message": "..." }
}
```

### Key IPC Methods
- `system.detect` → Full system profile
- `tweaks.list` → All applicable tweaks with current state
- `tweaks.detect` → Detect state for specific tweak or all
- `tweaks.apply` → Apply a single tweak
- `tweaks.applyBatch` → Apply multiple tweaks in order
- `tweaks.dryRun` → Preview what changes would be made
- `tweaks.rollback` → Rollback specific tweak
- `snapshot.create` → Create full snapshot
- `snapshot.restore` → Restore from snapshot
- `snapshot.list` → List available snapshots
- `auto.run` → Execute auto-optimize with specified mode
- `preset.apply` → Apply a named preset
- `diagnostics.run` → Run diagnostic checks
- `install.list` → List available packages
- `install.execute` → Install/uninstall package

## Error Handling Strategy

### Fail-Safe Principles
1. Never leave a system in a partially-modified state
2. If apply() fails halfway, rollback what was applied
3. If verify() fails after apply(), automatically rollback
4. Always preserve original state in snapshot before modifying
5. Log every operation, especially failures
6. If the engine crashes mid-operation, the snapshot contains enough data to recover

### Recovery Scenarios
- Engine crash mid-apply → Snapshot contains old states → On restart, offer to complete or rollback
- Reboot required mid-batch → Mark pending changes → On restart, detect and offer to continue
- User force-kills the app → Snapshot on disk → Next launch detects incomplete operation
- .NET helper not responding → Tauri shows error → Retry or escalate
- Insufficient privileges → Detect before apply → Prompt for elevation
