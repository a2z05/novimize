using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;
using WinOpt.Engine.Logging;

namespace WinOpt.Engine.Security;

/// <summary>
/// Enforces security boundaries. Every tweak must pass through
/// the security guard before detection, application, or rollback.
/// </summary>
public sealed class SecurityGuard
{
    private readonly WinOptLogger _logger;

    public SecurityGuard(WinOptLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Check if a tweak is safe to apply. Returns (allowed, reason).
    /// </summary>
    public (bool Allowed, string Reason) CheckCanApply(TweakDefinition tweak, bool isAutoMode)
    {
        // Hard block: Myth-risk tweaks are never applied
        if (tweak.Risk == RiskLevel.Myth)
            return (false, $"Tweak '{tweak.Id}' is classified as a myth — will never be applied.");

        // Hard block: Deprecated tweaks
        if (tweak.Risk == RiskLevel.Deprecated)
            return (false, $"Tweak '{tweak.Id}' is deprecated — will never be applied.");

        // Hard block: Dangerous tweaks
        if (tweak.Risk == RiskLevel.Dangerous)
            return (false, $"Tweak '{tweak.Id}' is classified as dangerous — will never be applied.");

        // Service protection check
        if (tweak.Method == TweakMethod.Service)
        {
            var serviceName = tweak.Params.GetValueOrDefault("serviceName")
                           ?? tweak.Apply.ServiceName
                           ?? tweak.Detection.ServiceName;

            if (!string.IsNullOrEmpty(serviceName))
            {
                var (safe, tier, reason) = SecurityBoundaries.CheckServiceProtection(serviceName);

                if (!safe && tier == ProtectionTier.NeverTouch)
                {
                    _logger.Warn($"SecurityGuard BLOCKED: {reason}", "security", tweak.Id);
                    return (false, reason);
                }

                if (!safe && tier == ProtectionTier.NeverSilent && isAutoMode)
                {
                    _logger.Warn($"SecurityGuard BLOCKED in auto-mode: {reason}", "security", tweak.Id);
                    return (false, $"Auto-optimize cannot modify protected service '{serviceName}'.");
                }

                if (!safe && tier == ProtectionTier.WarnBeforeDisable)
                {
                    _logger.Warn($"SecurityGuard WARNING: {reason}", "security", tweak.Id);
                    // Allow but log — caller should confirm with user
                }
            }
        }

        // Registry protection check
        if (tweak.Method == TweakMethod.Registry)
        {
            var regKey = tweak.Apply.RegistryKey ?? tweak.Params.GetValueOrDefault("registryKey");
            if (!string.IsNullOrEmpty(regKey))
            {
                // Check against protected paths
                if (SecurityBoundaries.IsRegistryProtected(regKey))
                {
                    if (isAutoMode)
                    {
                        _logger.Warn($"SecurityGuard BLOCKED: Registry path '{regKey}' is protected.", "security", tweak.Id);
                        return (false, $"Registry path '{regKey}' is protected from auto-optimize.");
                    }
                    _logger.Warn($"SecurityGuard WARNING: Modifying protected registry path '{regKey}'.", "security", tweak.Id);
                }
            }
        }

        // Auto-mode risk ceiling
        if (isAutoMode && tweak.Risk > RiskLevel.Safe)
        {
            return (false, $"Auto-optimize only allows Safe tweaks. '{tweak.Id}' is {tweak.Risk}.");
        }

        // Auto-mode evidence floor
        if (isAutoMode && tweak.Evidence < 4)
        {
            return (false, $"Auto-optimize requires evidence >= 4. '{tweak.Id}' has evidence {tweak.Evidence}.");
        }

        return (true, "OK");
    }

    /// <summary>
    /// Check if a set of tweaks has any security conflicts.
    /// </summary>
    public List<string> CheckBulkSecurity(IReadOnlyList<TweakDefinition> tweaks, bool isAutoMode)
    {
        var issues = new List<string>();

        foreach (var tweak in tweaks)
        {
            var (allowed, reason) = CheckCanApply(tweak, isAutoMode);
            if (!allowed)
            {
                issues.Add($"[{tweak.Id}] {reason}");
            }
        }

        return issues;
    }

    /// <summary>
    /// Check if a rollback is safe to perform.
    /// </summary>
    public bool CheckCanRollback(SnapshotEntry entry)
    {
        // Rollback is generally safe because it restores known-good state.
        // But warn if rolling back a security-related change.
        var serviceName = entry.Target;
        if (entry.Method == TweakMethod.Service && !string.IsNullOrEmpty(serviceName))
        {
            var (safe, tier, reason) = SecurityBoundaries.CheckServiceProtection(serviceName);
            if (!safe && tier == ProtectionTier.NeverTouch)
            {
                _logger.Warn($"Rollback of protected service '{serviceName}' — this should restore it, not disable it.", "security", entry.TweakId);
                // Allow rollback — restoring a protected service is good
            }
        }

        return true;
    }
}
