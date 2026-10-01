namespace WinOpt.Core.Models;

/// <summary>
/// Where a tweak writes, and what it writes there.
///
/// Shared by the planner (to spot two tweaks fighting over one target) and by
/// the apply result (to show the user what is about to change), so a target is
/// described the same way in a warning and in a preview.
/// </summary>
public static class TweakTarget
{
    /// <summary>
    /// The unit of change a tweak owns. Two tweaks sharing a resource key
    /// cannot both hold if they leave different values behind.
    /// </summary>
    public static string Resource(TweakDefinition t)
    {
        var p = t.Params;

        var regKey = t.Apply.RegistryKey ?? p.GetValueOrDefault("registryKey");
        var regValue = t.Apply.RegistryValue ?? p.GetValueOrDefault("registryValue")
                       ?? t.Detection.RegistryValue;
        if (!string.IsNullOrEmpty(regKey) && !string.IsNullOrEmpty(regValue))
            return $"registry:{regKey}::{regValue}";

        var svc = t.Apply.ServiceName ?? p.GetValueOrDefault("serviceName") ?? t.Detection.ServiceName;
        if (!string.IsNullOrEmpty(svc))
            return $"service:{svc}";

        // Switching the active power scheme is a single global slot.
        if (IsPowerPlanSwitch(t))
            return "powerplan:active";

        // Free-form scripts cannot be reasoned about, so they are left out
        // rather than guessed at.
        return string.Empty;
    }

    /// <summary>
    /// The value a tweak leaves behind, used to decide whether two writes to the
    /// same target agree. PowerShell tweaks carry theirs in the command text, so
    /// the command stands in as the identity.
    /// </summary>
    public static string Write(TweakDefinition t)
    {
        var p = t.Params;

        var regKey = t.Apply.RegistryKey ?? p.GetValueOrDefault("registryKey");
        var regValue = t.Apply.RegistryValue ?? p.GetValueOrDefault("registryValue");
        if (!string.IsNullOrEmpty(regKey) && !string.IsNullOrEmpty(regValue))
        {
            var data = t.Apply.RegistryData ?? p.GetValueOrDefault("registryData")
                       ?? (t.TargetValue.Length > 0 ? t.TargetValue : string.Empty);
            var type = t.Apply.RegistryType ?? p.GetValueOrDefault("registryType") ?? "DWORD";
            return $"registry {type} '{data}'";
        }

        var svc = t.Apply.ServiceName ?? p.GetValueOrDefault("serviceName");
        if (!string.IsNullOrEmpty(svc))
        {
            var start = t.Apply.ServiceStartType ?? p.GetValueOrDefault("serviceStartType")
                        ?? t.TargetValue;
            return $"service start-type '{start}'";
        }

        if (IsPowerPlanSwitch(t))
            return $"active plan {t.TargetValue}";

        var cmd = t.Apply.Command ?? p.GetValueOrDefault("applyCommand") ?? string.Empty;
        return cmd.Trim().Length > 0 ? $"script {cmd.Trim()}" : string.Empty;
    }

    /// <summary>
    /// True when the tweak changes which power scheme is active — a single
    /// global slot, so only one such tweak can hold at a time.
    ///
    /// <c>/setactive SCHEME_CURRENT</c> does not qualify: powercfg requires that
    /// call after changing a value so the change takes effect, and it leaves the
    /// active scheme exactly where it was. Treating it as a switch made every
    /// powercfg tweak look like it was fighting over the same slot.
    /// </summary>
    public static bool IsPowerPlanSwitch(TweakDefinition t)
    {
        var cmd = t.Apply.Command ?? t.Params.GetValueOrDefault("applyCommand") ?? string.Empty;
        var idx = cmd.IndexOf("/setactive", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        var arg = cmd[(idx + "/setactive".Length)..];
        var end = arg.IndexOfAny(new[] { ';', '\n', '\r', '&', '|' });
        if (end >= 0) arg = arg[..end];
        arg = arg.Trim().Trim('"');

        if (arg.Length == 0) return false;
        if (arg.StartsWith("SCHEME_", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// A human-readable target for a preview or a log line: the resource when
    /// the tweak owns one, otherwise its method, so the row is never blank.
    /// </summary>
    public static string Describe(TweakDefinition t)
    {
        var resource = Resource(t);
        return resource.Length > 0 ? resource : t.Method.ToString();
    }
}
