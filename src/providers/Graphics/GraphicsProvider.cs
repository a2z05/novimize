using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;
using RegKey = Microsoft.Win32.Registry;
using RegKeyKind = Microsoft.Win32.RegistryValueKind;

namespace WinOpt.Providers.Graphics;

/// <summary>
/// Provider for graphics and gaming tweaks. Handles HAGS (Hardware
/// Accelerated GPU Scheduling), fullscreen optimizations, GPU priority,
/// Game Mode, and Game DVR settings.
/// </summary>
public sealed class GraphicsProvider : ITweakProvider
{
    public string Name => "Graphics";
    // Registry handling is now consolidated in RegistryProvider; this provider is
    // retained for name-based lookup but does not claim a TweakMethod to avoid
    // ProviderRegistry overwrite (see ProviderRegistry.cs). Domain-specific
    // graphics tweaks are Registry method and routed via RegistryProvider.
    public IReadOnlyList<TweakMethod> SupportedMethods => Array.Empty<TweakMethod>();

    public Task<DetectionResult> DetectAsync(TweakDefinition tweak)
    {
        try
        {
            var keyPath = tweak.Detection.RegistryKey ?? tweak.Params.GetValueOrDefault("registryKey");
            var valueName = tweak.Detection.RegistryValue ?? tweak.Params.GetValueOrDefault("registryValue");

            if (string.IsNullOrEmpty(keyPath) || string.IsNullOrEmpty(valueName))
                return Task.FromResult(DetectionResult.Failed(tweak.Id, "Missing registry key or value in detection spec."));

            var fullPath = NormalizeRegistryPath(keyPath);

            // Determine hive from path
            var isHkcu = keyPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
                         keyPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);

            var hive = isHkcu ? RegKey.CurrentUser : RegKey.LocalMachine;
            using var key = hive.OpenSubKey(fullPath);
            if (key == null)
            {
                if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault))
                    return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.NotApplied, null));

                return Task.FromResult(DetectionResult.Failed(tweak.Id, $"Registry key not found: {keyPath}"));
            }

            var value = key.GetValue(valueName);
            var valueStr = value?.ToString();

            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                string.Equals(valueStr, tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.Applied, valueStr));

            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault) &&
                string.Equals(valueStr, tweak.Detection.ExpectedDefault, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.NotApplied, valueStr));

            return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.PartiallyApplied, valueStr));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DetectionResult.Failed(tweak.Id, ex.Message));
        }
    }

    public Task<ApplyResult> ApplyAsync(TweakDefinition tweak, SnapshotEntry? previousState = null)
    {
        try
        {
            var keyPath = tweak.Apply.RegistryKey ?? tweak.Params.GetValueOrDefault("registryKey");
            var valueName = tweak.Apply.RegistryValue ?? tweak.Params.GetValueOrDefault("registryValue");
            var valueData = tweak.Apply.RegistryData ?? tweak.TargetValue;
            var valueType = tweak.Apply.RegistryType ?? tweak.Params.GetValueOrDefault("registryType", "DWORD");

            if (string.IsNullOrEmpty(keyPath) || string.IsNullOrEmpty(valueName))
                return Task.FromResult(ApplyResult.Error(tweak.Id, "Missing registry key or value in apply spec."));

            var fullPath = NormalizeRegistryPath(keyPath);

            // Determine hive from path
            var isHkcu = keyPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
                         keyPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);

            var hive = isHkcu ? RegKey.CurrentUser : RegKey.LocalMachine;

            // Capture current value for snapshot
            string? oldValue = null;
            try
            {
                using var readKey = hive.OpenSubKey(fullPath);
                oldValue = readKey?.GetValue(valueName)?.ToString();
            }
            catch { /* Best effort */ }

            // Create or open key
            using var key = hive.CreateSubKey(fullPath);
            if (key == null)
                return Task.FromResult(ApplyResult.Error(tweak.Id, $"Cannot create/open registry key: {keyPath}"));

            // Write value
            switch (valueType.ToUpperInvariant())
            {
                case "DWORD":
                    if (int.TryParse(valueData, out var dwordVal))
                        key.SetValue(valueName, dwordVal, RegKeyKind.DWord);
                    else
                        return Task.FromResult(ApplyResult.Error(tweak.Id, $"Cannot parse DWORD value: {valueData}"));
                    break;

                case "QWORD":
                    if (long.TryParse(valueData, out var qwordVal))
                        key.SetValue(valueName, qwordVal, RegKeyKind.QWord);
                    else
                        return Task.FromResult(ApplyResult.Error(tweak.Id, $"Cannot parse QWORD value: {valueData}"));
                    break;

                case "SZ":
                case "STRING":
                    key.SetValue(valueName, valueData, RegKeyKind.String);
                    break;

                case "EXPAND_SZ":
                    key.SetValue(valueName, valueData, RegKeyKind.ExpandString);
                    break;

                case "BINARY":
                    var bytes = Convert.FromHexString(valueData.Replace(" ", ""));
                    key.SetValue(valueName, bytes, RegKeyKind.Binary);
                    break;

                case "MULTI_SZ":
                    var lines = valueData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    key.SetValue(valueName, lines, RegKeyKind.MultiString);
                    break;

                default:
                    key.SetValue(valueName, valueData, RegKeyKind.String);
                    break;
            }

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = $"{keyPath}::{valueName}",
                OldValue = oldValue,
                NewValue = valueData,
                Method = TweakMethod.Registry,
                Command = $"Set-ItemProperty -Path '{keyPath}' -Name '{valueName}' -Value '{valueData}'",
                RestoreCommand = oldValue != null
                    ? $"Set-ItemProperty -Path '{keyPath}' -Name '{valueName}' -Value '{oldValue}'"
                    : $"Remove-ItemProperty -Path '{keyPath}' -Name '{valueName}' -ErrorAction SilentlyContinue"
            };

            return Task.FromResult(ApplyResult.Ok(tweak.Id, entry));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(ApplyResult.NeedElevation(tweak.Id));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Error(tweak.Id, ex.Message));
        }
    }

    public Task<RollbackResult> RollbackAsync(SnapshotEntry entry)
    {
        try
        {
            // Support both new "::" format and legacy "\" format
            string[] parts;
            if (entry.Target.Contains("::"))
                parts = entry.Target.Split("::", 2, StringSplitOptions.None);
            else
                parts = entry.Target.Split('\\', 2);
            if (parts.Length < 2)
                return Task.FromResult(new RollbackResult { TweakId = entry.TweakId, Success = false, Message = "Invalid target format." });

            var keyPath = NormalizeRegistryPath(parts[0]);
            var valueName = parts[1];

            var isHkcu = parts[0].StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
                         parts[0].StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);

            var hive = isHkcu ? RegKey.CurrentUser : RegKey.LocalMachine;
            using var key = hive.CreateSubKey(keyPath);
            if (key == null)
                return Task.FromResult(new RollbackResult { TweakId = entry.TweakId, Success = false, Message = $"Cannot open key: {keyPath}" });

            if (entry.OldValue == null)
                key.DeleteValue(valueName, throwOnMissingValue: false);
            else
                key.SetValue(valueName, entry.OldValue);

            return Task.FromResult(new RollbackResult { TweakId = entry.TweakId, Success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new RollbackResult { TweakId = entry.TweakId, Success = false, Message = ex.Message });
        }
    }

    public Task<bool> VerifyAsync(TweakDefinition tweak)
    {
        return DetectAsync(tweak).ContinueWith(t => t.Result.State == TweakState.Applied);
    }

    private static string NormalizeRegistryPath(string path)
    {
        return path
            .Replace("HKLM:\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKLM\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_LOCAL_MACHINE\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKCU:\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKCU\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_CURRENT_USER\\", "", StringComparison.OrdinalIgnoreCase);
    }
}
