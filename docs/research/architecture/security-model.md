# WinOpt Security Model & Privacy Architecture

## Core Security Principles

### 1. Hard Boundaries — NEVER Silently Disable

These protections are NEVER touched by auto-optimize and require explicit high-risk confirmation in manual mode:

| Protection | Service/Component | Risk if Disabled |
|---|---|---|
| Microsoft Defender | WinDefend, WdNisSvc, Sense | System left without real-time malware protection |
| Windows Firewall | mpssvc, MpsSvc | Network exposure to attacks |
| SmartScreen | SmartScreen (Edge + Explorer) | Phishing and malware download protection |
| UAC | Application Information service | Privilege escalation attacks become trivial |
| Exploit Protection | System-wide mitigation policies | Exploitation of software vulnerabilities |
| Core Isolation / VBS | hvhost, VirtualSecureMachine | Credential theft, kernel exploits |
| Credential Guard | LSA Isolation | Pass-the-hash attacks |
| Secure Boot | Firmware-level | Bootkit/rootkit installation |
| Windows Update core | wuauserv, bits | Missing critical security patches |
| Data Execution Prevention | System-wide | Buffer overflow exploitation |

**Policy:** If any tweak in the database would weaken these protections, it is classified as DANGEROUS, never included in any auto-optimize mode, and requires:
1. Explicit "HIGH RISK" section in the UI
2. Warning message explaining consequences
3. Confirmation checkbox ("I understand this reduces my security")
4. Reason field (user must type why they want this)
5. Snapshot creation before application

### 2. Principle of Least Privilege

- **Normal operations:** Run as standard user (no elevation)
- **System modifications:** Request UAC elevation
- **Never persist admin credentials**
- **Never modify system files directly** (use official APIs)
- **Never create scheduled tasks that run as SYSTEM** without user awareness

### 3. Local-First Architecture

- **No telemetry by default** — WinOpt does not phone home
- **No account required** — fully offline-capable
- **No cloud service** for core operation
- **All data stored locally** in %LOCALAPPDATA%\WinOpt\
- **Snapshots stored locally** with user-controlled retention
- **Logs stored locally** with rotation

### 4. Optional Online Features

When the user explicitly enables:
- Package metadata (winget package lists)
- Tweak database updates
- DNS provider data
- App installation via winget

**Rules:**
- HTTPS only
- All external requests disclosed to user
- Can be completely disabled
- No data leaves the system without explicit consent
- Package integrity verification (hash checking)

## Elevation Model

### Operation Classification

| Level | Operations | UI Treatment |
|---|---|---|
| **None** | System detection, state reading, diagnostics | Run without elevation |
| **UAC** | Registry writes, service changes, powercfg, netsh, DISM | Standard UAC prompt |
| **Full** | System Restore point creation, driver changes | Explicit warning + UAC |

### Elevation Strategy

```
1. Detect if running elevated
2. If not elevated and operation requires it:
   a. Perform dry-run without elevation
   b. Show user what will change
   c. Request UAC elevation only when user confirms
   d. Pass operation details to elevated process
3. Elevated process executes operations
4. Results returned to non-elevated UI
5. Elevated process exits
```

**Never:** Run the entire application elevated. Only elevate for specific operations.

## Registry Modification Safety

### Pre-Modification Checks
1. Read current value (including "does not exist")
2. Validate target path exists and is writable
3. Create backup in snapshot
4. Set registry value
5. Read back and verify

### Registry Paths to Never Touch
```
HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot     — Secure Boot state
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Setup  — System setup state
HKLM\SYSTEM\CurrentControlSet\Control\Lsa             — LSA secrets
HKLM\SAM                                                 — Security Account Manager
HKLM\SECURITY                                           — Security policies
```

### Registry Paths Requiring Extra Caution
```
HKLM\SYSTEM\CurrentControlSet\Control\Session Manager — Boot configuration
HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers — GPU stability
HKLM\SYSTEM\CurrentControlSet\Services\*               — Service configurations
HKLM\SOFTWARE\Policies\*                               — Group Policy (may conflict with domain policy)
```

## Service Modification Safety

### Service Classification

| Tier | Services | Action |
|---|---|---|
| **Protected** | WinDefend, mpssvc, wuauserv, bits, CryptSvc, TrustedInstaller | NEVER modify without extreme warning |
| **Critical System** | RpcSs, DcomLaunch, PlugPlay, LSM, BrokerInfrastructure | NEVER modify |
| **Important** | WSearch, SysMain, DiagTrack | Modify with caution, document impact |
| **Optional** | Xbox services, Fax, MapsBroker, RemoteRegistry | Safe to modify |
| **User-Dependent** | Spooler (if no printer), WMPNetworkSvc (if no media sharing) | Check hardware/use-case first |

### Service Modification Rules
1. **Always check dependencies** before disabling (sc.exe depshow)
2. **Always check reverse dependencies** (what depends on this service?)
3. **Prefer Manual over Disabled** — Manual still allows on-demand startup
4. **Never set Boot or System services to Disabled**
5. **Store exact previous StartType and Status**
6. **Verify service state after modification**

## Firewall Modification Rules

### WinOpt Firewall Policy
- **Default:** Do NOT modify firewall rules
- **Exception:** Only for user-requested network diagnostics or specific port configurations
- **Naming:** All WinOpt-created rules prefixed with `WinOpt_` for identification
- **Logging:** Every created rule logged with full details
- **Rollback:** Delete only rules with WinOpt_ prefix

## AppX Removal Safety

### Before Removing Any App
1. Check if it's on the protected list
2. Check if other apps depend on it
3. Check if it's a system component (NonRemovable flag)
4. Check if removal could break Store, Settings, or other core functionality
5. Warn user of consequences

### Protected AppX Packages (NEVER Remove)
```
Microsoft.WindowsStore          — Cannot be easily reinstalled
Microsoft.WindowsCalculator    — Core utility
Microsoft.Windows.Photos       — Core utility
Microsoft.WindowsNotepad       — Core utility (modern)
Microsoft.WindowsTerminal      — May be current shell
Microsoft.ScreenSketch         — Screenshot tool
Microsoft.Paint                — Core utility
```

### AppX Reinstallation Capability
Every removable app must have a documented reinstallation path:
- Microsoft Store reinstallation
- WinGet reinstallation
- PowerShell re-provisioning
- If no path exists → mark as "IRREVERSIBLE — requires Windows reinstall"

## Data Protection

### Snapshot Data
- Stored in %LOCALAPPDATA%\WinOpt\snapshots\
- Contains registry values and system configuration (NOT user files)
- Encrypted at rest (optional, configurable)
- User-controlled retention policy

### Log Data
- Stored in %LOCALAPPDATA%\WinOpt\logs\
- Contains operation records (what was changed, when, result)
- Rotated with size limits
- No personally identifiable information collected

### What WinOpt NEVER Collects
- Browsing history
- File contents
- Passwords or credentials
- Personal documents
- User names or account information
- Hardware serial numbers (except for internal compatibility checks)
- Network traffic
- Keystrokes or input data

## Supply Chain Security

### For Winget Package Installation
1. Only use official winget source (microsoft/winget-pkgs)
2. Verify package hashes when available
3. Show user the exact package being installed before proceeding
4. Never install from untrusted sources
5. Log all package installations

### For Tweak Database Updates (Future)
1. HTTPS download only
2. Verify digital signature
3. Validate JSON schema before applying
4. Show diff of changes before updating
5. Allow user to review and approve changes

## Audit Trail

Every system modification produces an audit entry:
```json
{
  "timestamp": "ISO-8601",
  "operation": "apply | rollback | detect",
  "tweakId": "string",
  "target": "string",
  "oldValue": "string | null",
  "newValue": "string",
  "result": "success | failure | rollback",
  "elevationUsed": true,
  "userInitiated": true,
  "snapshotId": "uuid"
}
```

Audit logs are:
- Append-only (never modified after creation)
- Stored locally
- Retained per user-configured policy
- Exportable for diagnostics
- NOT transmitted externally
