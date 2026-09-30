# WinOpt Tweak Definition Schema

## Overview

Every tweak in WinOpt is defined as a structured data object. This document defines the complete schema that every tweak must follow. Tweaks are stored as JSON files organized by category.

## Schema Definition

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "id": "string — unique identifier (format: category.subcategory.name, e.g. network.wifi.powerSaving)",
  "name": "string — human-readable name",
  "category": "string — top-level category (see Category Enum)",
  "subcategory": "string — sub-category within the domain",
  "description": "string — short user-facing description (1-2 sentences)",
  "technicalDescription": "string — detailed technical explanation of what the tweak does",
  "expectedBenefit": "string — what improvement the user should expect",
  "limitations": "string — when this tweak may not help or may not apply",

  "risk": "string — risk classification (see Risk Enum)",
  "evidence": "integer — 0-5 evidence score",
  "evidenceJustification": "string — explanation of why this score was assigned",

  "provider": "string — which provider handles this tweak (e.g. 'RegistryProvider', 'PowerProvider')",

  "platformSupport": {
    "windows10": ["21H2", "22H2"],
    "windows11": ["21H2", "22H2", "23H2", "24H2"],
    "editions": ["Home", "Pro", "Enterprise", "Education"],
    "architectures": ["x64", "ARM64"]
  },

  "hardwareRequirements": {
    "type": "any | desktop | laptop",
    "cpuVendor": ["Intel", "AMD", "Qualcomm"],
    "gpuVendor": ["NVIDIA", "AMD", "Intel"],
    "nicVendor": ["Intel", "Realtek", "Qualcomm", "MediaTek", "Killer"],
    "storageType": ["SATA-SSD", "NVMe", "HDD"],
    "wifiStandard": ["Wi-Fi5", "Wi-Fi6", "Wi-Fi6E", "Wi-Fi7"],
    "minRamMB": 0,
    "notes": "string — additional hardware requirements or notes"
  },

  "privileges": {
    "required": true,
    "elevationType": "none | uac | full"
  },

  "restartRequired": {
    "reboot": false,
    "signOut": false,
    "serviceRestart": [],
    "processRestart": []
  },

  "detection": {
    "method": "string — 'registry' | 'powercfg' | 'netsh' | 'wmi' | 'powershell' | 'service' | 'file' | 'custom'",
    "command": "string — command or registry path to read current state",
    "parseExpression": "string — regex or path to extract current value from command output",
    "states": {
      "notApplied": "string — value that means tweak is NOT applied",
      "applied": "string — value that means tweak IS applied",
      "partiallyApplied": "string | null — value for partial state"
    },
    "customDetectScript": "string | null — PowerShell script for complex detection"
  },

  "apply": {
    "method": "string — 'registry' | 'powercfg' | 'netsh' | 'powershell' | 'sc' | 'dism' | 'appx' | 'custom'",
    "commands": [
      {
        "command": "string — the command to execute",
        "description": "string — what this specific command does",
        "elevationRequired": true,
        "timeoutMs": 30000
      }
    ],
    "customApplyScript": "string | null — PowerShell script for complex apply logic"
  },

  "rollback": {
    "method": "string — 'registry' | 'command' | 'snapshot' | 'custom'",
    "restoreOriginalValue": true,
    "commands": [
      {
        "command": "string — the command to restore previous state",
        "description": "string — what this rollback command does"
      }
    ],
    "customRollbackScript": "string | null — PowerShell script for complex rollback"
  },

  "verify": {
    "method": "string — same as detection method",
    "command": "string — command to verify post-apply",
    "parseExpression": "string — how to parse the result",
    "expectedValue": "string — what value confirms success"
  },

  "dependencies": [
    "string — IDs of tweaks that must be applied first"
  ],

  "conflicts": [
    {
      "tweakId": "string — ID of conflicting tweak",
      "reason": "string — why they conflict",
      "severity": "warning | error"
    }
  ],

  "presets": ["string — preset names this tweak belongs to: 'gaming', 'balanced', 'maxPerformance', etc."],

  "autoOptimize": {
    "qualifiesFor": ["safe", "balanced", "gaming", "network", "cleanup"],
    "alwaysAutoApply": false,
    "requiresUserConfirmation": false,
    "confirmationMessage": "string | null — shown when user confirmation needed"
  },

  "tags": ["string — searchable tags"],

  "metadata": {
    "author": "string — who defined this tweak",
    "createdDate": "2026-01-01T00:00:00Z",
    "lastVerified": "2026-01-01T00:00:00Z",
    "lastVerifiedWindowsBuild": "22631",
    "sources": [
      {
        "type": "microsoft-docs | vendor-docs | technical-research | community | anecdotal",
        "url": "string",
        "description": "string"
      }
    ],
    "deprecated": false,
    "deprecationReason": "string | null",
    "obsolete": false,
    "obsoleteReason": "string | null"
  }
}
```

## Category Enum

```
cpu          - Processor power management, scheduling, boost
gpu          - GPU scheduling, HAGS, graphics settings
network      - All networking: NIC, TCP/IP, DNS, Wi-Fi, Ethernet
storage      - Disk optimization, TRIM, write caching, cleanup
memory       - Virtual memory, pagefile, compression, SysMain
services     - Windows service configuration
features     - Windows optional features
privacy      - Telemetry, advertising ID, activity history
security     - Security-related settings (HIGH RISK section)
gaming       - Game Mode, Game Bar, gaming-specific settings
visual       - Visual effects, animations, Explorer
cleanup      - Non-destructive cleanup operations
debloat      - App removal, feature removal
power        - Power plans, sleep, hibernate
startup      - Startup apps, boot optimization
apps         - Application installation/management
usb          - USB power and configuration
bluetooth    - Bluetooth power and configuration
pcie         - PCIe power management
updates      - Windows Update configuration
maintenance  - System repair, diagnostics
```

## Risk Enum

```
SAFE          - Low risk, broadly useful
RECOMMENDED   - Useful for specific systems/users
OPTIONAL      - Potentially beneficial depending on context
EXPERIMENTAL  - Mixed evidence, workload-dependent
RISKY         - Can affect stability, security, or battery
DANGEROUS     - Can damage functionality or security
DEPRECATED    - Obsolete, do not expose
MYTH          - Placebo, do not implement
```

## Example Tweak Definitions

### Example 1: TCP Auto-Tuning (Network)

```json
{
  "id": "network.tcpip.autoTuning",
  "name": "TCP Receive Window Auto-Tuning",
  "category": "network",
  "subcategory": "tcpip",
  "description": "Configure TCP auto-tuning to optimize receive window sizing for your network connection.",
  "technicalDescription": "TCP auto-tuning dynamically adjusts the receive window size based on network conditions. The 'normal' setting allows Windows to optimize for most network environments. Disabling it can cause issues on modern networks.",
  "expectedBenefit": "Optimized TCP throughput for your network conditions. May improve download speeds on high-bandwidth connections.",
  "limitations": "Most networks benefit from 'normal'. Only modify if you have a specific networking issue.",

  "risk": "SAFE",
  "evidence": 4,
  "evidenceJustification": "Microsoft-documented feature with clear technical rationale. Normal mode is recommended by Microsoft. Specific optimal level depends on network conditions.",

  "provider": "NetworkProvider",

  "platformSupport": {
    "windows10": ["21H2", "22H2"],
    "windows11": ["21H2", "22H2", "23H2", "24H2"],
    "editions": ["Home", "Pro", "Enterprise", "Education"],
    "architectures": ["x64", "ARM64"]
  },

  "hardwareRequirements": {
    "type": "any",
    "notes": "Requires active network adapter"
  },

  "privileges": {
    "required": true,
    "elevationType": "uac"
  },

  "restartRequired": {
    "reboot": false,
    "signOut": false,
    "serviceRestart": [],
    "processRestart": []
  },

  "detection": {
    "method": "netsh",
    "command": "netsh int tcp show global",
    "parseExpression": "autotuninglevel\\s*:\\s*(\\w+)",
    "states": {
      "notApplied": "disabled",
      "applied": "normal",
      "partiallyApplied": "highlyrestricted"
    }
  },

  "apply": {
    "method": "powershell",
    "commands": [
      {
        "command": "netsh int tcp set global autotuninglevel=normal",
        "description": "Set TCP auto-tuning to normal mode",
        "elevationRequired": true,
        "timeoutMs": 5000
      }
    ]
  },

  "rollback": {
    "method": "command",
    "restoreOriginalValue": true,
    "commands": [
      {
        "command": "netsh int tcp set global autotuninglevel={originalValue}",
        "description": "Restore original auto-tuning level"
      }
    ]
  },

  "verify": {
    "method": "netsh",
    "command": "netsh int tcp show global",
    "parseExpression": "autotuninglevel\\s*:\\s*(\\w+)",
    "expectedValue": "normal"
  },

  "dependencies": [],
  "conflicts": [],
  "presets": ["balanced", "gaming", "maxPerformance"],
  "autoOptimize": {
    "qualifiesFor": ["safe", "balanced", "gaming", "network"],
    "alwaysAutoApply": false,
    "requiresUserConfirmation": true,
    "confirmationMessage": "This will set TCP auto-tuning to 'normal' mode. Only change if your current setting is non-standard."
  },

  "tags": ["tcp", "network", "throughput", "auto-tuning", "netsh"],
  "metadata": {
    "author": "WinOpt",
    "createdDate": "2026-08-31T00:00:00Z",
    "lastVerified": "2026-08-31T00:00:00Z",
    "lastVerifiedWindowsBuild": "22631",
    "sources": [
      {
        "type": "microsoft-docs",
        "url": "https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-tcp-autotuning-levels",
        "description": "Microsoft documentation on TCP auto-tuning levels"
      }
    ],
    "deprecated": false,
    "obsolete": false
  }
}
```

### Example 2: Disable Pagefile (DEPRECATED — MYTH)

```json
{
  "id": "memory.pagefile.disable",
  "name": "Disable Pagefile",
  "category": "memory",
  "subcategory": "virtualMemory",
  "description": "DISABLED — This tweak is a known myth. Do not implement.",
  "technicalDescription": "Disabling the pagefile does NOT increase FPS or performance. Modern Windows uses the pagefile for efficient memory management, crash dumps, and memory commit. Disabling it causes application crashes, prevents crash dump generation, and can cause out-of-memory errors.",
  "expectedBenefit": "None. This is a myth.",

  "risk": "DANGEROUS",
  "evidence": 0,
  "evidenceJustification": "MYTH — Disabling pagefile is counterproductive. Windows memory management depends on it for commit charge, crash dumps, and efficient memory allocation. Multiple technical analyses confirm this provides zero performance benefit.",

  "metadata": {
    "deprecated": true,
    "deprecationReason": "Known myth. Disabling pagefile causes stability issues with zero performance benefit.",
    "obsolete": false,
    "sources": [
      {
        "type": "technical-research",
        "url": "",
        "description": "Extensive community testing shows no FPS improvement from disabling pagefile, with significant stability risks"
      }
    ]
  }
}
```

## Tweak File Organization

```
src/engine/providers/tweaks/
├── cpu/
│   ├── power-plan.json
│   ├── processor-states.json
│   ├── boost-modes.json
│   └── core-parking.json
├── gpu/
│   ├── hags.json
│   ├── fullscreen-optimizations.json
│   └── shader-cache.json
├── network/
│   ├── nic-advanced.json
│   ├── tcp-ip.json
│   ├── dns.json
│   ├── wifi.json
│   └── delivery-optimization.json
├── memory/
│   ├── pagefile.json
│   ├── memory-compression.json
│   └── sysmain.json
├── services/
│   ├── common-services.json
│   └── gaming-services.json
├── ... (one folder per category)
└── _meta/
    ├── categories.json
    ├── risk-definitions.json
    └── evidence-rubric.json
```

## State Detection Contract

Every tweak MUST return one of these states:

```
NOT_APPLICABLE   — Tweak cannot be applied on this system (wrong OS, hardware, etc.)
NOT_APPLIED      — Tweak is available but not currently applied
APPLIED          — Tweak is fully applied and active
PARTIALLY_APPLIED — Some aspects applied, others not
UNKNOWN          — Unable to determine current state
ERROR            — Detection failed (log error, show warning)
```

## Apply/Verify/Rollback Contract

### Apply
1. Read current state (pre-check)
2. Store current state in snapshot as oldState
3. Execute apply command(s)
4. Verify post-apply state matches expected
5. If verify fails → automatic rollback → record failure
6. If verify succeeds → record success
7. Return ApplyResult

### Rollback
1. Read current state (may differ from both old and new)
2. Execute rollback command(s) using stored oldState
3. Verify state matches oldState
4. If verify fails → log warning, manual intervention needed
5. Record rollback result

### Verify
1. Execute detect command
2. Parse output
3. Compare with expected state
4. Return boolean + details
