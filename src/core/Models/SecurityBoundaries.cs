namespace WinOpt.Core.Models;

/// <summary>
/// Tier classification for security protections.
/// </summary>
public enum ProtectionTier
{
    /// <summary>Never touch. Hardcoded block.</summary>
    NeverTouch = 1,

    /// <summary>Never silently disable. Requires explicit user consent.</summary>
    NeverSilent = 2,

    /// <summary>Warn before disabling. User can override with confirmation.</summary>
    WarnBeforeDisable = 3,

    /// <summary>Check dependency chain before disabling.</summary>
    CheckDependencies = 4
}

/// <summary>
/// A protected service or setting that must not be blindly disabled.
/// </summary>
public sealed class ProtectionEntry
{
    public string Name { get; init; } = string.Empty;
    public ProtectionTier Tier { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;

    /// <summary>If true, auto-optimize never touches this regardless of mode.</summary>
    public bool BlockAutoOptimize => Tier == ProtectionTier.NeverTouch || Tier == ProtectionTier.NeverSilent;
}

/// <summary>
/// Hardcoded security boundaries — never bypass.
/// </summary>
public static class SecurityBoundaries
{
    /// <summary>Services that must NEVER be disabled by WinOpt.</summary>
    public static readonly HashSet<string> NeverDisableServices = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tier 1: Protection — NEVER
        "WinDefend", "WdNisSvc", "MpsSvc", "BFE", "wscsvc",
        "CryptSvc", "TrustedInstaller",

        // Tier 1: Critical System — NEVER
        "RpcSs", "DcomLaunch", "PlugPlay", "LSM", "BrokerInfrastructure",
        "SamSs", "EventLog", "Netlogon", "ProfSvc", "KeyIso",
        "Winmgmt", "Schedule", "nsi",

        // Tier 1: Update — NEVER
        "wuauserv", "UsoSvc", "WaaSMedicSvc",
    };

    /// <summary>Services that must not be disabled without explicit user consent.</summary>
    public static readonly HashSet<string> WarnBeforeDisableServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dnscache",       // Breaking DNS breaks Wi-Fi
        "CryptSvc",       // Breaks update, Store, driver signing
        "wuauserv",       // No security patches
        "UsoSvc",         // No automatic update scheduling
        "WSearch",        // Breaks file search, Start menu
        "SysMain",        // MYTH that it hurts SSDs
        "AppReadiness",   // Causes login delay but needed
    };

    /// <summary>Registry paths that must never be modified by auto-optimize.</summary>
    public static readonly HashSet<string> ProtectedRegistryPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\EnableLUA",
        @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\EnableVirtualizationBasedSecurity",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\DisableAntiSpyware",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection\DisableRealtimeMonitoring",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection\DisableBehaviorMonitoring",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard",
        @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallPolicy",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DisableSmartScreen",
    };

    /// <summary>Feature names that must never be removed via DISM.</summary>
    public static readonly HashSet<string> NeverRemoveFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "NetFx3",        // .NET Framework 3.5 — many apps depend on it
        "NetFx4",        // .NET Framework 4.x
        "Fonts",         // Font support
        "Printing",      // Print subsystem
        "MediaPlayback", // Media Foundation
        "WindowsFirewall",
        "Defender",
    };

    /// <summary>
    /// Check if a service is safe to disable.
    /// </summary>
    public static (bool Safe, ProtectionTier Tier, string Reason) CheckServiceProtection(string serviceName)
    {
        if (NeverDisableServices.Contains(serviceName))
            return (false, ProtectionTier.NeverTouch, $"Service '{serviceName}' is in the never-disable list.");

        if (WarnBeforeDisableServices.Contains(serviceName))
            return (false, ProtectionTier.WarnBeforeDisable, $"Service '{serviceName}' has important dependencies.");

        return (true, ProtectionTier.CheckDependencies, "No known protection boundary.");
    }

    /// <summary>
    /// Check if a registry path is protected.
    /// </summary>
    public static bool IsRegistryProtected(string registryPath)
    {
        return ProtectedRegistryPaths.Any(p =>
            string.Equals(p, registryPath, StringComparison.OrdinalIgnoreCase));
    }
}
