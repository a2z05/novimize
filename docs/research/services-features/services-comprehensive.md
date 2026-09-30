# WinOpt Comprehensive Services Reference

## Service Architecture

### Service Types
| Constant | Value | Description |
|----------|-------|-------------|
| SERVICE_KERNEL_DRIVER | 0x1 | Kernel-mode device driver |
| SERVICE_FILE_SYSTEM_DRIVER | 0x2 | File system driver |
| SERVICE_WIN32_OWN_PROCESS | 0x10 | Dedicated process |
| SERVICE_WIN32_SHARE_PROCESS | 0x20 | Shared svchost.exe |
| SERVICE_INTERACTIVE_PROCESS | 0x100 | Desktop interaction (deprecated) |
| SERVICE_USER_OWN_PROCESS | 0x50 | Per-user process (Vista+) |
| SERVICE_USER_SHARE_PROCESS | 0x60 | Per-user shared (Vista+) |

### Start Types
| Value | Constant | Description |
|-------|----------|-------------|
| 0 | SERVICE_BOOT_START | Boot driver (loader) |
| 1 | SERVICE_SYSTEM_START | Kernel initialization |
| 2 | SERVICE_AUTO_START | SCM automatic |
| 3 | SERVICE_DEMAND_START | Manual / on-demand |
| 4 | SERVICE_DISABLED | Cannot start |

### Trigger-Start Services (Windows 7+)
Trigger types: Device Interface (1), IP Address (2), Domain Join (3), Firewall Port (4), Group Policy (5), Network Endpoint (6), Custom ETW (20).
- Trigger-start services use demand-start (3) with trigger configuration
- Zero resources consumed until trigger fires
- Microsoft recommends: "Prefer trigger-start over auto-start"

---

## Service Modification Safety Rules

### HARD RULES
1. **NEVER** disable Protected tier services
2. **NEVER** disable Critical System tier services
3. **Prefer Manual over Disabled** for Optional services (Manual allows on-demand; Disabled blocks all)
4. **Always check forward dependencies** before disabling (what does this service need?)
5. **Always check reverse dependencies** before disabling (what depends on this service?)
6. **Store original StartType and Status** for rollback before any modification
7. **Never set Boot/System start type services to Disabled**

### Detection Commands
```powershell
# Forward dependencies (what this service needs)
Get-Service -Name Spooler -RequiredServices
sc qc Spooler

# Reverse dependencies (what depends on this service)
Get-Service -Name RpcSs -DependentServices
sc enumdepend RpcSs

# Full service info
Get-CimInstance Win32_Service -Filter "Name='SysMain'" | Select-Object Name, StartMode, State, Dependencies
sc qc SysMain
sc query SysMain
sc qfailure SysMain
```

---

## Service Classification Tiers

### Tier 1: PROTECTED — NEVER Modify
These services are critical to security and system integrity.

| Service | Display Name | Why Protected | Dependencies |
|---------|-------------|---------------|-------------|
| WinDefend | Windows Defender Antivirus | Real-time malware protection | FsDepends, FltMgr |
| WdNisSvc | Windows Defender Antivirus Network Inspection | Network threat detection | WinDefend |
| MpsSvc | Windows Firewall | Network security | BFE, RpcSs |
| BFE | Base Filtering Engine | All WFP networking/firewall | RpcSs, nsi |
| wscsvc | Security Center | Security monitoring | RpcSs |
| wuauserv | Windows Update | Security patches | RpcSs, cryptSvc, bits, DcomLaunch |
| UsoSvc | Update Orchestrator | Update pipeline | RpcSs |
| WaaSMedicSvc | Windows Update Medic | Self-repair of WU | RpcSs |
| CryptSvc | Cryptographic Services | Certs, SSL, signing | RpcSs |
| TrustedInstaller | Windows Modules Installer | System servicing | RpcSs |
| bits | Background Intelligent Transfer | BITS downloads | RpcSs, EventLog |

### Tier 2: CRITICAL SYSTEM — NEVER Modify
Core OS infrastructure. Disabling causes boot failure, login failure, or system collapse.

| Service | Display Name | What Happens If Disabled |
|---------|-------------|-------------------------|
| RpcSs | Remote Procedure Call | IPC collapse — nearly ALL services fail |
| DcomLaunch | DCOM Server Process Launcher | COM infrastructure destroyed — most apps crash |
| PlugPlay | Plug and Play | Hardware detection stops, devices unrecognized |
| LSM | Local Session Manager | Session management fails, login breaks |
| BrokerInfrastructure | Background Tasks Infrastructure | Background task scheduling collapses |
| SamSs | Security Accounts Manager | Authentication database inaccessible |
| EventLog | Windows Event Log | Auditing lost, many services depend on it |
| Netlogon | Network Logon | Domain authentication fails |
| ProfSvc | User Profile Service | User profiles can't load |
| KeyIso | CNG Key Isolation | Cryptographic keys exposed |
| Winmgmt | Windows Management Instrumentation | WMI infrastructure — many system components use it |
| Schedule | Task Scheduler | System scheduled tasks can't run |
| nsi | Network Store Interface | Network interface enumeration fails |

### Tier 3: IMPORTANT — Modify With Caution

| Service | Display Name | Safe to Disable? | Evidence | Notes |
|---------|-------------|-----------------|----------|-------|
| SysMain | SysMain (Superfetch) | **NO** — it's a MYTH that it hurts SSDs | 9/5 | Microsoft optimized for SSDs; provides prefetching |
| WSearch | Windows Search | Yes, if not using search | 4/5 | Lose file search, Outlook search, Start menu search |
| DiagTrack | Connected User Experiences and Telemetry | Yes | 5/5 | Reduces telemetry; may affect some diagnostics |
| dmwappushservice | WAP Push Message Routing | Yes | 5/5 | WAP push telemetry routing |
| WerSvc | Windows Error Reporting | Yes | 5/5 | Crash reports to Microsoft |
| PcaSvc | Program Compatibility Assistant | Yes | 3/5 | Compatibility warnings stop |
| DsSvc | Data Sharing Service | Yes | 3/5 | Some data sharing features stop |

### Tier 4: OPTIONAL — Safe to Modify

| Service | Display Name | Default | Recommended | Notes |
|---------|-------------|---------|-------------|-------|
| XboxGipSvc | Xbox Accessories Management | Auto | Disabled (if no Xbox) | Xbox controller management |
| XblAuthManager | Xbox Live Auth Manager | Manual | Disabled (if no Xbox) | Xbox authentication |
| XblGameSave | Xbox Live Game Save | Manual | Disabled (if no Xbox) | Xbox cloud saves |
| XboxNetApiSvc | Xbox Live Networking | Manual | Disabled (if no Xbox) | Xbox networking |
| MapsBroker | Downloaded Maps Manager | Manual | Manual | Offline maps |
| RemoteRegistry | Remote Registry | Disabled | Disabled | Remote registry access |
| Fax | Fax Service | Disabled | Disabled | Fax sending/receiving |
| RetailDemo | Retail Demo Service | Manual | Manual | Store demo mode |
| lfsvc | Geolocation Service | Manual | Manual | Location tracking |
| Spooler | Print Spooler | Auto | Manual (if no printer) | Print queue management |
| WMPNetworkSvc | Windows Media Player Network Sharing | Manual | Manual | Media sharing |
| SSDPDiscovery | SSDP Discovery | Manual | Manual | UPnP device discovery |
| upnphost | UPnP Device Host | Manual | Manual | UPnP hosting |
| WpnService | Windows Push Notifications | Auto | Manual | Notification service |
| AppReadiness | App Readiness | Auto | Manual | Login app preparation delay |
| PushToInstall | Push to Install | Manual | Manual | Remote app install |
| UnistoreSvc | User Data Storage | Manual | Manual | User data access |
| wisvc | Windows Insider Service | Manual | Manual | Insider builds |

### Tier 5: USER-DEPENDENT

| Service | Display Name | Depends On |
|---------|-------------|------------|
| TabletInputService | Touch Keyboard and Handwriting | Touch/pen hardware |
| WbioSrvc | Windows Biometric | Windows Hello hardware |
| ScDeviceSvc | Smart Card Device | Smart card reader |
| SCardSvr | Smart Card | Smart card reader |
| acmaindexer | Accessibility Indexer | Accessibility needs |
| WpcMonSvc | Parental Controls | Family Safety configured |

---

## Services Often Mistakenly Disabled

### DcomLaunch (DCOM Server Process Launcher)
**Why people disable it:** "I don't use DCOM" / "It's just for legacy apps"
**Reality:** DCOM is the foundation for inter-process communication across Windows. Disabling breaks: Windows Update, Windows Installer, parts of the shell, many third-party apps, Office applications, Visual Studio, and more. **NEVER disable.**

### RpcSs (Remote Procedure Call)
**Why people disable it:** "I don't use remote procedures"
**Reality:** RPC is used for LOCAL inter-process communication too. Nearly every Windows service communicates via RPC. Disabling causes a cascade of 50+ service failures. **NEVER disable.**

### EventLog (Windows Event Log)
**Why people disable it:** "It just logs stuff" / "Saves disk space"
**Reality:** Many services depend on EventLog to function. Windows Update, Security auditing, diagnostics, and numerous system components require it. Disabling breaks security auditing. **NEVER disable.**

### PlugPlay (Plug and Play)
**Why people disable it:** "I don't plug in new devices"
**Reality:** PlugPlay manages ALL hardware devices, not just new ones. It handles device startup, driver loading, and hardware configuration. Disabling can cause storage controllers, network adapters, and display drivers to malfunction. **NEVER disable.**

### LSM (Local Session Manager)
**Why people disable it:** "I don't know what it is"
**Reality:** Manages user sessions — login, logout, lock, unlock, RDP connections. Disabling can prevent login entirely. **NEVER disable.**

### ProfSvc (User Profile Service)
**Why people disable it:** "Sounds optional"
**Reality:** Loads user profiles at login. Without it, users cannot log in — Windows will show "The User Profile Service failed to logon." **NEVER disable.**

### Schedule (Task Scheduler)
**Why people disable it:** "Scheduling is bloat"
**Reality:** Windows Update, Disk Cleanup, TRIM, security scanning, and dozens of system maintenance tasks run via Task Scheduler. Disabling causes system degradation over time. **NEVER disable.**

### Winmgmt (Windows Management Instrumentation)
**Why people disable it:** "WMI is bloatware"
**Reality:** WMI is used by: Event Viewer, Performance Monitor, System Information, Device Manager, many Group Policy settings, third-party monitoring tools, antivirus software, and WinOpt itself. **NEVER disable.**

---

## Service Management Commands

### Query Commands
```powershell
# Service configuration
sc.exe qc SysMain
sc.exe qc SysMain 8192  # larger buffer

# Service state
sc.exe query SysMain
sc.exe query SysMain type= service state= all

# Failure configuration
sc.exe qfailure SysMain

# Reverse dependencies
sc.exe enumdepend RpcSs

# Trigger info
sc.exe qtriggerinfo w32time
```

### Modification Commands
```powershell
# Set start type
sc.exe config SysMain start= demand       # Manual
sc.exe config SysMain start= auto         # Automatic
sc.exe config SysMain start= delayed-auto # Delayed Auto
sc.exe config SysMain start= disabled     # Disabled

# Start/Stop
sc.exe stop SysMain
sc.exe start SysMain

# Set recovery actions
sc.exe failure Spooler reset= 86400 actions= restart/5000/run/10000/reboot/60000

# Clear recovery
sc.exe failure Spooler actions= ""

# Set failure flag (trigger on ANY exit)
sc.exe failureflag Spooler flag=1
```

### PowerShell Commands
```powershell
# Query
Get-Service -Name SysMain
Get-Service -Name SysMain -RequiredServices   # forward deps
Get-Service -Name SysMain -DependentServices  # reverse deps

# Modify
Set-Service -Name SysMain -StartupType Manual
Stop-Service -Name SysMain -Force
Start-Service -Name SysMain

# CIM/WMI (richer data)
Get-CimInstance Win32_Service -Filter "Name='SysMain'" |
    Select-Object Name, StartMode, State, PathName, Dependencies

# Enumerate all services with optimization info
Get-Service | Select-Object Name, DisplayName, Status, StartType |
    Sort-Object StartType | Format-Table -AutoSize
```

### Registry Commands
```powershell
# Read service start type
Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\SysMain" -Name Start

# Set service start type via registry
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\SysMain" -Name Start -Value 3

# Read service SID type
Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\SysMain" -Name ServiceSidType
```

---

## Service Dependencies

### How Dependency Resolution Works
1. SCM reads all service records from registry
2. Builds dependency graph for all auto-start services
3. Services started by group ordering → tag ordering → dependency ordering
4. Circular dependencies detected and broken by SCM
5. Service START_PENDING counts as "started" for dependents
6. Delayed auto-start services start ~120s after regular auto-start

### Key Dependency Chains
```
wuauserv (Windows Update)
  ├── RpcSs (RPC)
  ├── CryptSvc (Cryptographic Services)
  │     └── RpcSs
  ├── bits (Background Intelligent Transfer)
  │     ├── RpcSs
  │     └── EventLog
  ├── DcomLaunch
  └── EventLog

Dnscache (DNS Client) — NOT_STOPPABLE
  ├── nsi (Network Store Interface)
  └── Afd
  Dependents:
  ├── WinHttpAutoProxySvc
  │     └── Wcmsvc → WlanSvc (ALL WI-FI BREAKS)
  └── NcaSvc
```

---

## Service SIDs

### Types
| Value | Type | Description |
|-------|------|-------------|
| 0 | SERVICE_SID_TYPE_NONE | No SID, no isolation |
| 1 | SERVICE_SID_TYPE_UNRESTRICTED | Full NT SERVICE\Name SID |
| 2 | SERVICE_SID_TYPE_RESTRICTED | Maximum isolation |

### Built-in Accounts
| Account | SID | Use Case |
|---------|-----|----------|
| SYSTEM (LocalSystem) | S-1-5-18 | Full local privileges, network as computer |
| NetworkService | S-1-5-20 | Minimal local, network as computer |
| LocalService | S-1-5-19 | Minimal local, anonymous network |

### Commands
```bash
sc.exe sidtype MyService unrestricted   # Set SID type
sc.exe sidtype MyService restricted     # Maximum isolation
sc.exe sdset MyService "D:(A;;GA;;;NS)" # Set service DACL
```

---

## Service Recovery Options

### Action Types
| Action | Value | Description |
|--------|-------|-------------|
| None | 0 | No action |
| Restart Computer | 1 | Reboot machine |
| Restart Service | 2 | Restart the service |
| Run Program | 3 | Execute command |

### Configuration
```bash
# Three-tier recovery: restart → run program → reboot
sc.exe failure Spooler reset= 86400 reboot= "Rebooting" command= "C:\recovery.exe" actions= restart/5000/run/10000/reboot/60000

# Clear all recovery
sc.exe failure Spooler actions= ""

# Trigger recovery on ANY exit (not just errors)
sc.exe failureflag Spooler flag=1
```

### Service Failure Event IDs
| Event ID | Meaning |
|----------|---------|
| 7000 | Service failed to start |
| 7001 | Dependency failed to start |
| 7009 | Service timed out during startup (30s) |
| 7022 | Service hung on starting |
| 7023 | Service terminated with error |
| 7024 | Service terminated with service-specific error |
| 7031 | Service terminated unexpectedly (with recovery) |
| 7034 | Service terminated unexpectedly (no recovery) |
| 7040 | Start type changed |
| 7041 | Logon failure |

---

## Complete Service Optimization Matrix

### Always Keep Running (Auto)
| Service | Evidence | Reason |
|---------|----------|--------|
| WinDefend | 5/5 | Malware protection |
| MpsSvc | 5/5 | Network security |
| BFE | 5/5 | Firewall infrastructure |
| wuauserv | 5/5 | Security patches |
| RpcSs | 5/5 | Core IPC |
| DcomLaunch | 5/5 | Core COM |
| EventLog | 5/5 | Auditing + dependencies |
| PlugPlay | 5/5 | Hardware management |
| CryptSvc | 5/5 | Certificate validation |
| LSM | 5/5 | Session management |
| ProfSvc | 5/5 | User profiles |

### Safe to Disable (if not used)
| Service | Evidence | Save |
|---------|----------|------|
| Fax | 5/5 | ~5MB RAM |
| RemoteRegistry | 5/5 | Security improvement |
| RetailDemo | 5/5 | ~2MB RAM |
| XboxGipSvc | 5/5 | ~3MB RAM (no Xbox) |
| XblAuthManager | 5/5 | ~3MB RAM (no Xbox) |
| XblGameSave | 5/5 | ~2MB RAM (no Xbox) |
| XboxNetApiSvc | 5/5 | ~3MB RAM (no Xbox) |
| MapsBroker | 4/5 | ~3MB RAM (no offline maps) |
| WMPNetworkSvc | 4/5 | ~5MB RAM (no media sharing) |
| dmwappushservice | 5/5 | Telemetry reduction |
| WerSvc | 5/5 | ~3MB RAM |
| lfsvc | 4/5 | ~2MB RAM (no location) |
| Spooler | 5/5 | ~10MB RAM (no printer) |

### Do NOT Disable (commonly mistakenly targeted)
| Service | Why NOT |
|---------|---------|
| SysMain | MYTH that it hurts SSDs; provides prefetch optimization |
| Dnscache | NOT_STOPPABLE; disabling breaks Wi-Fi (dependency chain) |
| Winmgmt | WMI used by system components, monitoring tools, WinOpt itself |
| Schedule | System maintenance relies on it |
| WSearch | Search, Start menu, Outlook depend on it |
| AppReadiness | Causes login delay but needed for app preparation |
