# WinOpt Preset & Recommendation System

## Preset Definitions

Each preset is a named collection of tweak IDs with weighting and priority.

### PRESET: Default / Safe
```
description: "Baseline optimizations that are safe for any system"
qualifyingCriteria:
  risk: SAFE
  evidence: ≥ 4
  hardware: any
  powerState: any
  reboot: not required
  securityImpact: none
```

### PRESET: Balanced
```
description: "Optimized settings for general use — performance without sacrifice"
qualifyingCriteria:
  risk: SAFE or RECOMMENDED
  evidence: ≥ 3
  hardware: any
  reboot: acceptable
  securityImpact: none
  mayAffectBattery: documented
```

### PRESET: Max Performance
```
description: "Maximum performance — may increase power consumption and heat"
qualifyingCriteria:
  risk: SAFE, RECOMMENDED, or OPTIONAL
  evidence: ≥ 3
  hardware: any desktop preferred
  securityImpact: none
  batteryImpact: acknowledged (power plan changes)
```

### PRESET: Gaming
```
description: "Optimized for gaming — prioritizes latency and frame stability"
qualifyingCriteria:
  risk: SAFE or RECOMMENDED
  evidence: ≥ 3
  category: includes cpu, gpu, network, gaming, visual
  securityImpact: none
```

### PRESET: Low Latency
```
description: "Minimized input and network latency for competitive gaming and real-time applications"
qualifyingCriteria:
  risk: SAFE or RECOMMENDED
  evidence: ≥ 3
  latencyImpact: positive
  throughputMayDecrease: documented
```

### PRESET: Max Throughput
```
description: "Maximized data transfer rates for large file operations and high-bandwidth networks"
qualifyingCriteria:
  risk: SAFE or RECOMMENDED
  evidence: ≥ 3
  throughputImpact: positive
  latencyMayIncrease: documented
```

### PRESET: Laptop / Battery
```
description: "Optimized for battery life — reduces power consumption without major performance loss"
qualifyingCriteria:
  risk: SAFE
  evidence: ≥ 3
  hardware: laptop only
  batteryImpact: positive (extends battery)
  performanceImpact: minor reduction acceptable
```

### PRESET: Workstation
```
description: "Optimized for professional workloads — CAD, video editing, 3D rendering, development"
qualifyingCriteria:
  risk: SAFE or RECOMMENDED
  evidence: ≥ 3
  workloadSpecific: true
  mayInclude: cpu, memory, storage, gpu optimizations
```

### PRESET: Developer
```
description: "Optimized for development — fast builds, responsive IDE, good networking"
qualifyingCriteria:
  risk: SAFE or RECOMMENDED
  evidence: ≥ 3
  developerRelevant: true
  mayInclude: memory, storage, network, startup optimizations
```

### PRESET: Streaming
```
description: "Optimized for streaming — stable frame encoding, good network, background task management"
qualifyingCriteria:
  risk: SAFE or RECOMMENDED
  evidence: ≥ 3
  streamingRelevant: true
  mayInclude: network, gpu, scheduling, background task management
```

### PRESET: Privacy
```
description: "Enhanced privacy settings — reduces telemetry and data collection"
qualifyingCriteria:
  risk: SAFE or RECOMMENDED
  evidence: ≥ 3
  category: privacy
  functionalImpact: documented (e.g., some personalization lost)
  neverDisables: security features
```

### PRESET: Cleanup
```
description: "Non-destructive cleanup — reclaim disk space"
qualifyingCriteria:
  risk: SAFE
  evidence: ≥ 4
  category: cleanup
  destructive: false
  canFreeSpace: documented
```

## Preset Configuration File

```json
{
  "presets": [
    {
      "id": "gaming",
      "name": "Gaming",
      "description": "Optimized for gaming — prioritizes latency and frame stability",
      "icon": "gamepad",
      "color": "#FF6B35",
      "applicableTo": ["desktop", "laptop"],
      "minEvidence": 3,
      "maxRisk": "RECOMMENDED",
      "includeCategories": ["cpu", "gpu", "network", "gaming", "visual", "power"],
      "excludeCategories": ["privacy", "cleanup", "debloat"],
      "alwaysInclude": [
        "gaming.gameMode.enable",
        "gaming.gameDvr.configure",
        "cpu.powerPlan.highPerformance",
        "gpu.hags.enable"
      ],
      "neverInclude": [
        "security.defender.disable",
        "services.windowsUpdate.disable",
        "features.hyperV.disable"
      ],
      "overrides": {
        "cpu.powerPlan.minimum": { "value": 50, "reason": "Prevent CPU downclocking during games" },
        "cpu.powerPlan.boost": { "value": "aggressive", "reason": "Maximize single-thread performance" }
      }
    }
  ]
}
```

## Recommendation Engine

### Input
- SystemProfile (hardware, OS, current state)
- UserContext (use case, preferences, history)
- TweakDatabase (all tweak definitions)

### Scoring Algorithm

For each tweak, compute a recommendation score:

```
score = baseScore × hardwareMultiplier × stateMultiplier × riskMultiplier × evidenceMultiplier

where:
  baseScore = 50 (neutral)
  
  hardwareMultiplier:
    hardwareMatches    → 1.5
    hardwarePartial    → 1.0
    hardwareMismatch   → 0.0 (exclude)
  
  stateMultiplier:
    notApplied         → 1.0
    partiallyApplied   → 1.2 (prioritize completing)
    applied            → 0.0 (skip — already done)
    notApplicable      → 0.0 (exclude)
  
  riskMultiplier:
    SAFE               → 1.5
    RECOMMENDED        → 1.2
    OPTIONAL           → 0.8
    EXPERIMENTAL       → 0.5
    RISKY              → 0.2
    DANGEROUS          → 0.0 (exclude from recommendations)
  
  evidenceMultiplier:
    evidence / 5.0     (linear scaling)
```

### Preset Alignment Score
If user selected a preset, multiply score by preset relevance:
- Tweak is in preset's alwaysInclude → × 2.0
- Tweak matches preset categories → × 1.5
- Tweak is in preset's neverInclude → × 0.0

### Conflict Penalty
If tweak conflicts with a higher-scored tweak:
- severity=warning → × 0.5
- severity=error → × 0.0 (exclude)

### Output
Ranked list of tweaks with:
- Recommendation score
- Reason for score
- Expected impact
- Risk level
- Evidence score
- Dependencies (what must come first)
- Conflicts (what contradicts this)

## Conflict Engine

### Conflict Types

1. **Direct Contradiction**
   - Tweak A: Set TCP auto-tuning to normal
   - Tweak B: Set TCP auto-tuning to disabled
   - Resolution: Higher-scored tweak wins; warn user

2. **Resource Conflict**
   - Tweak A: Enable high-performance power plan
   - Tweak B: Enable aggressive battery saving
   - Resolution: Conflict warning; user must choose

3. **Security vs Performance**
   - Tweak A: Disable Windows Defender real-time
   - Tweak B: Maximize gaming performance
   - Resolution: ALWAYS block; security wins

4. **Dependency Not Met**
   - Tweak A: Requires service X to be running
   - Tweak B: Disables service X
   - Resolution: Block Tweak B if Tweak A is selected

5. **Windows Override**
   - Tweak A: Set registry value
   - Tweak B: Windows Update periodically resets this value
   - Resolution: Document; don't block but warn

### Conflict Resolution Priority
1. Security always wins
2. Stability beats performance
3. Higher evidence beats lower evidence
4. User choice overrides defaults

### Conflict Definition Format
```json
{
  "conflicts": [
    {
      "id": "conflict-network-throughput-latency",
      "description": "Throughput-optimized settings may increase latency",
      "tweaks": ["network.tcpip.autoTuning.normal", "network.tcpip.congestion.bbr"],
      "type": "mutual-exclusion",
      "severity": "warning",
      "resolution": "Choose one optimization path"
    }
  ]
}
```

## Desktop vs Laptop Detection

### Detection Method
```csharp
// Check if system is a laptop
bool IsLaptop()
{
    // Method 1: Battery present
    var battery = WMI.Query("Win32_Battery");
    if (battery != null) return true;
    
    // Method 2: Chassis type
    var chassis = WMI.Query("Win32_SystemEnclosure").ChassisTypes;
    // ChassisTypes: 8=Portable, 9=Laptop, 10=Notebook, 14=SubNotebook, 30=Tablet
    if (chassis.Any(t => new[] {8,9,10,14,30}.Contains(t))) return true;
    
    // Method 3: Platform role
    var role = Registry.Get(@"HKLM\SYSTEM\CurrentControlSet\Control\Power");
    // PlatformAcDcLine != 0 suggests desktop
    
    return false;
}
```

### Laptop-Safe Overrides
When detecting a laptop:
- Never default to Ultimate Performance power plan
- Always consider battery impact
- Warn about power tweaks that increase heat
- Default to Balanced or Battery preset
- Disable aggressive core parking changes
- Be cautious with NIC power saving (need it for battery)
