# WinOpt CLI Design

## Overview

The CLI provides the same backend engine as the GUI, enabling automation, scripting, and power-user workflows. Built in Rust (Tauri's sidecar model), it communicates with the same .NET engine.

## Command Structure

```
winopt <command> [subcommand] [options]
```

## Commands

### System Information
```bash
# Full system inventory
winopt system

# System summary (one-line)
winopt system --summary

# Specific component info
winopt system cpu
winopt system gpu
winopt system network
winopt system storage
winopt system memory
```

### Tweak Management
```bash
# List all tweaks with current state
winopt list

# List tweaks by category
winopt list --category network
winopt list --category cpu
winopt list --category gpu
winopt list --category services

# List with filtering
winopt list --risk safe
winopt list --evidence 4+
winopt list --state not-applied
winopt list --preset gaming

# Show details for a specific tweak
winopt info network.tcpip.autoTuning

# Detect state of all tweaks
winopt scan

# Detect state of specific tweak
winopt scan network.tcpip.autoTuning
```

### Apply Tweaks
```bash
# Dry run (preview what would change)
winopt apply --dry-run
winopt apply --dry-run --preset balanced
winopt apply --dry-run network.tcpip.autoTuning

# Apply single tweak
winopt apply network.tcpip.autoTuning

# Apply by category
winopt apply --category network
winopt apply --category cpu --category gpu

# Apply preset
winopt apply --preset gaming
winopt apply --preset safe
winopt apply --preset balanced

# Force apply (skip confirmation)
winopt apply --preset gaming --force

# Apply with verbose output
winopt apply --preset balanced --verbose
```

### Auto-Optimize
```bash
# Auto-optimize with specific mode
winopt auto --mode safe
winopt auto --mode balanced
winopt auto --mode gaming
winopt auto --mode network
winopt auto --mode cleanup

# Auto-optimize with dry run first
winopt auto --mode balanced --dry-run

# Auto-optimize and show results
winopt auto --mode balanced --verbose
```

### Rollback
```bash
# Rollback single tweak
winopt rollback network.tcpip.autoTuning

# Rollback by category
winopt rollback --category network

# Rollback entire session (from snapshot)
winopt rollback --session <snapshot-id>

# Rollback all changes
winopt rollback --all

# List available rollback points
winopt rollback --list
```

### Snapshots
```bash
# List snapshots
winopt snapshot list

# Create manual snapshot
winopt snapshot create --description "Before manual changes"

# View snapshot details
winopt snapshot show <snapshot-id>

# Export snapshot
winopt snapshot export <snapshot-id> --output snapshot.json

# Delete old snapshots
winopt snapshot cleanup --older-than 30d
```

### Presets
```bash
# List available presets
winopt preset list

# Show what a preset would change
winopt preset show gaming

# Apply a preset
winopt preset apply gaming

# Create custom preset from current state
winopt preset create my-custom --from-current

# Export preset
winopt preset export gaming --output gaming.json
```

### Cleanup
```bash
# Preview cleanup targets
winopt cleanup --dry-run

# Run cleanup
winopt cleanup

# Selective cleanup
winopt cleanup --temp-files --dns-cache --thumbnails

# Cleanup specific cache
winopt cleanup --shader-cache
winopt cleanup --windows-update-cache
```

### Diagnostics
```bash
# Full system diagnostics
winopt doctor

# Network diagnostics
winopt doctor network

# Startup diagnostics
winopt doctor startup

# Service diagnostics
winopt doctor services

# Storage health
winopt doctor storage

# Quick health check
winopt doctor --quick
```

### App Management
```bash
# List available apps
winopt install list

# Search for an app
winopt install search "vs code"

# Install an app
winopt install "Visual Studio Code"

# Install multiple apps
winopt install "Visual Studio Code" "7-Zip" "VLC"

# Uninstall an app
winopt uninstall "VLC"

# List installed apps
winopt install installed

# Upgrade all apps
winopt install upgrade --all
```

### Services
```bash
# List services with optimization status
winopt services list

# Show service details
winopt services info SysMain

# Show service dependencies
winopt services deps DiagTrack

# Show what disabling a service would affect
winopt services check SysMain
```

### Configuration
```bash
# Show current configuration
winopt config show

# Set configuration value
winopt config set autoMode balanced
winopt config set snapshotRetention 90

# Reset configuration
winopt config reset
```

## Output Formats

### Default (Human-Readable)
```
✓ network.tcpip.autoTuning    TCP Auto-Tuning     NOT_APPLIED  SAFE  4/5
  Current: disabled
  Target:  normal
  Risk:    Safe
  Evidence: 4/5 - Microsoft documented feature

✓ services.sysmain.config     SysMain Service     APPLIED      SAFE  2/5
  Current: Disabled
  Note:    Already optimized
```

### JSON (Machine-Readable)
```json
{
  "tweaks": [
    {
      "id": "network.tcpip.autoTuning",
      "name": "TCP Auto-Tuning",
      "state": "not_applied",
      "risk": "safe",
      "evidence": 4,
      "currentValue": "disabled",
      "targetValue": "normal"
    }
  ]
}
```

### Table (for scripts)
```bash
winopt list --format table
winopt list --format csv
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | General error |
| 2 | Permission denied (elevation required) |
| 3 | Tweak not found |
| 4 | Tweak not applicable |
| 5 | Apply failed |
| 6 | Rollback failed |
| 7 | Verification failed |
| 8 | Snapshot error |
| 9 | Configuration error |
| 10 | Network error (online feature) |

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `WINOPT_VERBOSE` | Enable verbose output |
| `WINOPT_JSON` | Force JSON output |
| `WINOPT_NO_COLOR` | Disable colored output |
| `WINOPT_CONFIG` | Custom config path |
| `WINOPT_LOG_LEVEL` | Log level (debug, info, warn, error) |
| `WINOPT_OFFLINE` | Disable all network features |

## Scripting Examples

### Automated Optimization Script
```bash
#!/bin/bash
# Optimized gaming setup script

# Scan first
winopt scan --format json > scan-results.json

# Dry run
winopt auto --mode gaming --dry-run --format json > dry-run.json

# Apply with confirmation
winopt auto --mode gaming --force

# Verify
winopt doctor --quick

# Save snapshot
winopt snapshot create --description "Post-gaming-optimize"
```

### CI/CD Integration
```bash
# In a Windows CI/CD pipeline
winopt system --format json > system-info.json
winopt doctor --quick --format json > health-check.json
```

### Maintenance Script
```bash
#!/bin/bash
# Weekly maintenance

# Cleanup
winopt cleanup --temp-files --dns-cache

# Check health
winopt doctor --quick

# Upgrade apps
winopt install upgrade --all --force

# Create snapshot
winopt snapshot create --description "Weekly maintenance"
```
