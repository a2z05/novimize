# WinOpt Security Boundaries — Never Disable Protections

## Overview

WinOpt enforces hard boundaries around security-critical Windows protections. These protections are **never silently disabled, never included in auto-optimize, and never recommended to disable**. The security model exists to prevent the application from becoming a vector for system compromise.

---

## Tier 1: ABSOLUTE NEVER TOUCH

These protections must NEVER be disabled by WinOpt under any circumstances. No user override, no auto-optimize mode, no preset. They are hardcoded in the engine.

### 1. Windows Defender Antivirus (WinDefend)

| Property | Value |
|----------|-------|
| Service | WinDefend, WdNisSvc, WdNisDrv, WdBoot |
| Detection | `Get-Service WinDefend`, `Get-MpComputerStatus` |
| Registry | `HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\DisableAntiSpyware` |
| Why never | Real-time malware protection. Disabling exposes the system to ransomware, trojans, and rootkits. |
| Attack vector | Many "optimizer" tools disable Defender for perceived performance gains (1-3% CPU at most). This is the #1 way PCs get compromised. |
| Evidence | 5/5 — Microsoft, NIST, and every security vendor recommends never disabling AV. |

### 2. Windows Defender Real-Time Protection

| Property | Value |
|----------|-------|
| Detection | `Get-MpPreference | Select-Object DisableRealtimeMonitoring` |
| Registry | `HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection\DisableRealtimeMonitoring` |
| Why never | Even with Defender "enabled," disabling real-time monitoring means files are only scanned on access — malware can execute before scan triggers. |
| Evidence | 5/5 |

### 3. Windows Defender Tamper Protection

| Property | Value |
|----------|-------|
| Detection | `Get-MpPreference | Select-Object DisableTamperProtection` |
| Why never | Tamper Protection prevents malicious software (including scripts and registry changes) from disabling Defender. It is the protection-of-protections. |
| Evidence | 5/5 |

### 4. Windows Defender Cloud Protection

| Property | Value |
|----------|-------|
| Detection | `Get-MpPreference | Select-Object MAPSReporting` |
| Registry | `HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Spynet\DisableSamplingReporting` |
| Why never | Cloud-delivered protection provides real-time signatures against zero-day threats not yet in local definitions. |
| Evidence | 5/5 |

### 5. Windows Firewall (MpsSvc / BFE)

| Property | Value |
|----------|-------|
| Services | MpsSvc (Windows Firewall), BFE (Base Filtering Engine) |
| Detection | `Get-Service MpsSvc`, `netsh advfirewall show allprofiles state` |
| Registry | `HKLM:\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallPolicy` |
| Why never | First line of defense against network attacks. Disabling exposes every open port and service to the network. |
| Attack vector | Even on home networks, UPnP devices and compromised IoT can be attack vectors. |
| Evidence | 5/5 |

### 6. Windows Defender SmartScreen

| Property | Value |
|----------|-------|
| Detection | Registry: `HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DisableSmartScreen` |
| Files | `C:\Windows\System32\SmartScreenApps.exe` |
| Why never | Blocks malicious downloads and phishing sites. Disabling removes browser and OS-level web protection. |
| Evidence | 5/5 |

### 7. User Account Control (UAC)

| Property | Value |
|----------|-------|
| Registry | `HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\EnableLUA` |
| Detection | `Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System -Name EnableLUA` |
| Why never | UAC is the privilege escalation barrier. Disabling means every application runs with full admin rights — one malicious download can take complete control. |
| Value types | 0=Disabled (NEVER), 1=Enabled (always notify), 2=Default (notify on changes) |
| Evidence | 5/5 |

### 8. Virtualization-Based Security (VBS)

| Property | Value |
|----------|-------|
| Registry | `HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\EnableVirtualizationBasedSecurity` |
| Detection | `Get-CimInstance Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard` |
| Why never | VBS enables Credential Guard, HVCI, and memory integrity. Protects credentials from theft and kernel exploits. |
| Performance cost | 1-5% CPU in most workloads — justifiable for security. |
| Evidence | 5/5 |

### 9. Secure Boot

| Property | Value |
|----------|-------|
| Detection | `Confirm-SecureBootUEFI` (UEFI) or `Get-SecureBootState` |
| Why never | Prevents bootkits and rootkits from loading before the OS. Compromising Secure Boot = compromise at the firmware level. |
| Evidence | 5/5 |

### 10. BitLocker Drive Encryption (BLBUI)

| Property | Value |
|----------|-------|
| Service | BDESVC |
| Detection | `Get-BitLockerVolume` |
| Why never | Full-disk encryption protects data at rest. Disabling exposes all data to physical theft. Only offer management (backup key, status) — never disable. |
| Evidence | 5/5 |

---

## Tier 2: NEVER SILENTLY DISABLE

These protections should not be disabled without explicit, informed user consent. WinOpt should warn heavily and require confirmation. They are never included in auto-optimize.

### 11. Windows Update Service (wuauserv)

| Property | Value |
|----------|-------|
| Service | wuauserv, UsoSvc, WaaSMedicSvc |
| Detection | `Get-Service wuauserv` |
| Why cautious | Disabling stops security patches. However, power users may legitimately want manual control over update timing. |
| Allowed actions | Set to Manual (not Disabled). User can re-enable on demand. |
| Never allowed | Set to Disabled in any auto-optimize mode. |
| Evidence | 5/5 |

### 12. Windows Defender Antivirus Service (Service Mode)

| Property | Value |
|----------|-------|
| Detection | `Get-MpPreference | Select-Object DisableRealtimeMonitoring` |
| Why cautious | Defender's scheduled scans, definition updates, and cloud protection continue even with real-time off. But real-time off = significant security reduction. |
| Never allowed | DisableRealtimeMonitoring = true in any auto-optimize. |

### 13. .NET Framework

| Property | Value |
|----------|-------|
| Detection | `Get-WindowsOptionalFeature -Online -FeatureName NetFx*` |
| Why cautious | Hundreds of applications depend on .NET Framework. Disabling breaks apps including many system tools. |
| Evidence | 5/5 |

### 14. Windows Script Host

| Property | Value |
|----------|-------|
| Registry | `HKLM:\SOFTWARE\Microsoft\Windows Script Host\Settings\Enabled` |
| Detection | `cscript //H` or registry check |
| Why cautious | Disabling WSH breaks legitimate automation scripts. However, it does prevent VBS/JS malware. Better approach: use AppLocker/WDAC to control script execution, not disable WSH entirely. |
| Evidence | 4/5 |

---

## Tier 3: WARN BEFORE DISABLING

These protections can be legitimately disabled by informed users, but WinOpt should always warn about consequences.

### 15. Windows Defender Attack Surface Reduction (ASR) Rules

| Property | Value |
|----------|-------|
| Registry | `HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR\Rules` |
| Detection | `Get-MpPreference | Select-Object AttackSurfaceReductionRules_Ids` |
| Why warn | ASR rules block specific attack vectors (Office macros, script engines, credential theft). Disabling weakens specific defense layers. |

### 16. Windows Defender PUA (Potentially Unwanted Application) Protection

| Property | Value |
|----------|-------|
| Detection | `Get-MpPreference | Select-Object PUAProtection` |
| Registry | `HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\PUAProtection` |
| Why warn | PUA protection blocks adware, toolbars, and potentially unwanted software. Some legitimate software triggers PUA detection. |

### 17. Controlled Folder Access (Ransomware Protection)

| Property | Value |
|----------|-------|
| Detection | `Get-MpPreference | Select-Object EnableControlledFolderAccess` |
| Registry | `HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access\EnableControlledFolderAccess` |
| Why warn | Blocks unauthorized apps from modifying protected folders. Can interfere with legitimate apps (games, development tools). |

### 18. Windows Filtering Platform (WFP)

| Property | Value |
|----------|-------|
| Service | BFE (Base Filtering Engine) |
| Detection | `Get-Service BFE` |
| Why warn | WFP is the kernel networking stack that powers the firewall, IPsec, and network inspection. Cannot be safely disabled. |

### 19. Group Policy Enforcement

| Property | Value |
|----------|-------|
| Detection | `gpresult /h gp.html` |
| Why warn | Group Policy may enforce security settings managed by IT administrators. Bypassing GPO can violate corporate security policies. |

### 20. Credential Guard

| Property | Value |
|----------|-------|
| Detection | `Get-CimInstance Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard | Select-Object -Property VirtualizationBasedSecurityStatus` |
| Why warn | Protects LSASS and domain credentials from pass-the-hash and credential dumping attacks. Enterprise environments depend on it. |

### 21. Device Guard / WDAC (Windows Defender Application Control)

| Property | Value |
|----------|-------|
| Detection | `Get-CimInstance Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard | Select-Object CodeIntegrityPolicyEnforcementStatus` |
| Why warn | Application whitelisting that prevents unauthorized executables from running. Enterprise security cornerstone. |

### 22. AppLocker

| Property | Value |
|----------|-------|
| Service | AppIDSvc |
| Detection | `Get-AppLockerPolicy -Effective` |
| Why warn | Application control policy. Disabling allows any executable to run. |

---

## Tier 4: CHECK DEPENDENCIES FIRST

These services/features can be safely disabled in some scenarios, but have dependency chains that must be validated.

### 23. DNS Client Service (Dnscache)

| Property | Value |
|----------|-------|
| Service | Dnscache |
| Detection | `Get-Service Dnscache` |
| Why check deps | Dnscache is marked NOT_STOPPABLE. It is a dependency of WinHttpAutoProxySvc → Wcmsvc → WlanSvc. Disabling DNS cache breaks ALL Wi-Fi connectivity on some systems. |
| Safe alternative | Reduce cache timeout rather than disable. |

### 24. Windows Management Instrumentation (Winmgmt)

| Property | Value |
|----------|-------|
| Service | Winmgmt |
| Detection | `Get-Service Winmgmt` |
| Why check deps | Used by: Event Viewer, Performance Monitor, Device Manager, Group Policy, third-party monitoring tools, WinOpt itself. |
| Note | WinOpt should never disable WMI since it relies on it for system detection. |

### 25. Task Scheduler (Schedule)

| Property | Value |
|----------|-------|
| Service | Schedule |
| Detection | `Get-Service Schedule` |
| Why check deps | TRIM, Disk Cleanup, security scanning, Windows Update maintenance, and dozens of system tasks depend on it. Disabling causes gradual system degradation. |

---

## Implementation Rules

### Hard-Coded Protection List

The engine must maintain a hardcoded list of protected service names and registry paths:

```csharp
public static class SecurityBoundaries
{
    // Tier 1: Absolute never-touch
    public static readonly HashSet<string> NeverDisableServices = new()
    {
        "WinDefend", "WdNisSvc", "MpsSvc", "BFE", "wscsvc",
        "wuauserv", "UsoSvc", "WaaSMedicSvc", "CryptSvc",
        "TrustedInstaller", "RpcSs", "DcomLaunch", "PlugPlay",
        "LSM", "EventLog", "SamSs", "Netlogon", "ProfSvc",
        "KeyIso", "Winmgmt", "Schedule", "nsi",
        "BrokerInfrastructure", "Dnscache"
    };

    // Registry paths that must never be modified by auto-optimize
    public static readonly HashSet<string> ProtectedRegistryPaths = new()
    {
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\EnableLUA",
        @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\EnableVirtualizationBasedSecurity",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\DisableAntiSpyware",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection\DisableRealtimeMonitoring",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard",
        @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallPolicy",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DisableSmartScreen",
    };
}
```

### Auto-Optimize Safety Criteria

For every tweak in auto-optimize mode:

1. **Check tier classification** — Tier 1/2 protections are excluded from auto-optimize entirely
2. **Check risk level** — Only SAFE and RECOMMIMENTED tweaks are auto-applied
3. **Check evidence score** — Minimum evidence score of 4/5 for auto-apply
4. **Check user consent** — Tier 3/4 tweaks require explicit user confirmation
5. **Check dependency chain** — Validate no protected services depend on the target
6. **Check rollback availability** — Must have verified rollback path before auto-apply

### Detection Flow

```
Tweak requested → Check SecurityBoundaries →
  ├── Tier 1 (Never Touch) → REJECT with explanation
  ├── Tier 2 (Never Silent) → BLOCK if auto-optimize, require confirmation
  ├── Tier 3 (Warn) → WARN with consequences, require confirmation
  ├── Tier 4 (Check Deps) → Validate dependency chain, warn if needed
  └── Safe → PROCEED with standard apply flow
```

### Rollback of Security Changes

If WinOpt detects that a security protection was disabled (by the user manually, by another tool, or by malware):

1. **Report** the disabled protection in diagnostics (`winopt doctor`)
2. **Recommend** re-enabling with evidence explanation
3. **Offer** to re-enable with one command
4. **Never** silently re-enable without user consent (user may have intentionally disabled for a specific reason)

---

## Security vs. Performance Trade-offs

| Protection | Performance Cost | Security Benefit | Recommendation |
|-----------|-----------------|-----------------|----------------|
| Defender Real-Time | 1-3% CPU, ~50MB RAM | Critical — malware detection | KEEP ENABLED |
| Defender Tamper Protection | ~0% | Critical — prevents AV disabling | KEEP ENABLED |
| Windows Firewall | <1% CPU | Critical — network security | KEEP ENABLED |
| UAC | ~0% | Critical — privilege escalation barrier | KEEP ENABLED |
| VBS/Credential Guard | 1-5% CPU | High — credential protection | KEEP ENABLED (warn user) |
| Secure Boot | ~0% | Critical — boot integrity | KEEP ENABLED |
| BitLocker | 1-3% I/O | High — data at rest protection | KEEP ENABLED |
| SmartScreen | ~0% | High — web/download protection | KEEP ENABLED |
| ASR Rules | 1-2% CPU | Medium-High — attack surface reduction | KEEP ENABLED |
| Controlled Folder Access | ~0% | Medium — ransomware protection | KEEP ENABLED |
| PUA Protection | ~0% | Medium — adware/pup blocking | KEEP ENABLED |

**Key insight:** The combined performance cost of all security protections is typically under 5% CPU. The security benefit is immeasurable. No optimization is worth disabling these.

---

## References

- Microsoft Security Baselines: https://learn.microsoft.com/en-us/windows/security/operating-system-security/device-management/windows-security-configuration-framework/windows-security-baselines
- NIST Windows Hardening Guide: https://csf.tools/reference/nist-sp-800-53/r4/cm/
- CIS Benchmarks for Windows: https://www.cisecurity.org/cis-benchmarks
- Microsoft Defender Antivirus documentation: https://learn.microsoft.com/en-us/microsoft-365/security/defender-endpoint/
