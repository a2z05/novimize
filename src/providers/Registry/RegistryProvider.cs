using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;
using RegKey = Microsoft.Win32.Registry;
using RegKeyKind = Microsoft.Win32.RegistryValueKind;
using RegRoot = Microsoft.Win32.RegistryKey;

namespace WinOpt.Providers.Registry;

/// <summary>
/// Provider for registry-based tweaks. Handles read/write/delete
/// operations on Windows registry keys.
/// </summary>
public sealed class RegistryProvider : ITweakProvider
{
    public string Name => "Registry";
    public IReadOnlyList<TweakMethod> SupportedMethods => new[] { TweakMethod.Registry };

    public Task<DetectionResult> DetectAsync(TweakDefinition tweak)
    {
        try
        {
            var keyPath = tweak.Detection.RegistryKey ?? tweak.Params.GetValueOrDefault("registryKey");
            var valueName = tweak.Detection.RegistryValue ?? tweak.Params.GetValueOrDefault("registryValue");

            // An empty value name is not a missing one: it addresses the key's
            // default value, which is how Windows spells "this key alone does it"
            // — the classic context menu is enabled by nothing but an empty
            // InprocServer32. Only null means the spec never said.
            if (string.IsNullOrEmpty(keyPath) || valueName == null)
                return Task.FromResult(DetectionResult.Failed(tweak.Id, "Missing registry key or value in detection spec."));

            var (root, fullPath) = ResolveRegistryRoot(keyPath);

            using var key = root.OpenSubKey(fullPath);
            if (key == null)
            {
                // Key doesn't exist — check if that means not applied or detection failure
                if (IsDefaultState(tweak))
                {
                    return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.NotApplied, null));
                }
                return Task.FromResult(DetectionResult.Failed(tweak.Id, $"Registry key not found: {keyPath}"));
            }

            var value = key.GetValue(valueName);
            var valueStr = value?.ToString();

            // An absent value means Windows is using its own default, which is
            // the state the tweak wants to move away from — not a partial apply.
            if (value == null)
            {
                if (IsDefaultState(tweak))
                    return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.NotApplied, null));
                if (tweak.Detection.ExpectedApplied != null)
                    return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.NotApplied, null));
                return Task.FromResult(DetectionResult.Failed(tweak.Id, $"Registry value not found: {keyPath}\\{valueName}"));
            }

            // Determine state. Presence is tested against null rather than
            // emptiness: an applied state of "" is a real answer for settings
            // expressed by the mere existence of a value.
            var expectedApplied = tweak.Detection.ExpectedApplied;
            if (expectedApplied != null &&
                string.Equals(valueStr, expectedApplied, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.Applied, valueStr));
            }

            var expectedDefault = tweak.Detection.ExpectedDefault;
            if (expectedDefault != null &&
                string.Equals(valueStr, expectedDefault, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.NotApplied, valueStr));
            }

            // Value exists but doesn't match expected — partially applied or custom
            return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.PartiallyApplied, valueStr));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DetectionResult.Failed(tweak.Id, ex.Message));
        }
    }

    /// <summary>
    /// True when "the value is not there" is a valid answer for this tweak —
    /// either because it names the default Windows ships with, or because the
    /// spec says outright that absence *is* the default.
    /// </summary>
    private static bool IsDefaultState(TweakDefinition tweak)
        => tweak.Detection.ExpectedAbsent || !string.IsNullOrEmpty(tweak.Detection.ExpectedDefault);

    public Task<ApplyResult> ApplyAsync(TweakDefinition tweak, SnapshotEntry? previousState = null)
    {
        try
        {
            var keyPath = tweak.Apply.RegistryKey ?? tweak.Params.GetValueOrDefault("registryKey");
            var valueName = tweak.Apply.RegistryValue ?? tweak.Params.GetValueOrDefault("registryValue");
            var valueData = tweak.Apply.RegistryData ?? tweak.TargetValue;
            var valueType = tweak.Apply.RegistryType ?? tweak.Params.GetValueOrDefault("registryType", "DWORD");

            // Same rule as detection: "" addresses the default value, so a
            // tweak whose whole effect is "create this key" must still apply.
            if (string.IsNullOrEmpty(keyPath) || valueName == null)
                return Task.FromResult(ApplyResult.Error(tweak.Id, "Missing registry key or value in apply spec."));

            var (root, fullPath) = ResolveRegistryRoot(keyPath);

            // Capture current value for snapshot
            string? oldValue = null;
            using (var readKey = root.OpenSubKey(fullPath))
            {
                oldValue = readKey?.GetValue(valueName)?.ToString();
            }

            // Create or open key
            using var key = root.CreateSubKey(fullPath);
            if (key == null)
                return Task.FromResult(ApplyResult.Error(tweak.Id, $"Cannot create/open registry key: {keyPath}"));

            // Write value
            switch (valueType.ToUpperInvariant())
            {
                case "DWORD":
                    if (uint.TryParse(valueData, out var dwordVal))
                        key.SetValue(valueName, unchecked((int)dwordVal), RegKeyKind.DWord);
                    else if (valueData.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(valueData[2..], System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                        key.SetValue(valueName, unchecked((int)hexVal), RegKeyKind.DWord);
                    else
                        return Task.FromResult(ApplyResult.Error(tweak.Id, $"Cannot parse DWORD value: {valueData}"));
                    break;

                case "QWORD":
                    if (long.TryParse(valueData, out var qwordVal))
                        key.SetValue(valueName, qwordVal, RegKeyKind.QWord);
                    else if (ulong.TryParse(valueData, out var uqwordVal))
                        key.SetValue(valueName, unchecked((long)uqwordVal), RegKeyKind.QWord);
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
                Command = $"Set-ItemProperty -Path 'HKLM:{fullPath}' -Name '{valueName}' -Value '{valueData}'"
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

            var (root, keyPath) = ResolveRegistryRoot(parts[0]);
            var valueName = parts[1];

            using var key = root.CreateSubKey(keyPath);
            if (key == null)
                return Task.FromResult(new RollbackResult { TweakId = entry.TweakId, Success = false, Message = $"Cannot open key: {keyPath}" });

            if (entry.OldValue == null)
            {
                // Key didn't exist before — delete it
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                // Restore with best-effort type detection (snapshot stores string)
                if (uint.TryParse(entry.OldValue, out var dwordVal))
                    key.SetValue(valueName, unchecked((int)dwordVal), RegKeyKind.DWord);
                else if (long.TryParse(entry.OldValue, out var qwordVal))
                    key.SetValue(valueName, qwordVal, RegKeyKind.QWord);
                else
                    key.SetValue(valueName, entry.OldValue, RegKeyKind.String);
            }

            return Task.FromResult(new RollbackResult { TweakId = entry.TweakId, Success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new RollbackResult { TweakId = entry.TweakId, Success = false, Message = ex.Message });
        }
    }

    public async Task<bool> VerifyAsync(TweakDefinition tweak)
    {
        var result = await DetectAsync(tweak);
        return result.State == TweakState.Applied;
    }

    /// <summary>
    /// Resolve the correct registry root and normalize the subpath.
    /// "HKCU\Software\Foo" → (Registry.CurrentUser, "Software\Foo")
    /// "HKLM\Software\Foo" → (Registry.LocalMachine, "Software\Foo")
    /// </summary>
    private static (RegRoot root, string subPath) ResolveRegistryRoot(string path)
    {
        if (path.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
        {
            return (RegKey.CurrentUser, NormalizeRegistryPath(path));
        }
        // Default to HKLM
        return (RegKey.LocalMachine, NormalizeRegistryPath(path));
    }

    /// <summary>
    /// Convert friendly registry path to full subpath.
    /// "HKLM\SOFTWARE\Foo" → "SOFTWARE\Foo"
    /// "HKLM:\SOFTWARE\Foo" → "SOFTWARE\Foo"
    /// "HKCU\Software\Foo" → "Software\Foo"
    /// </summary>
    private static string NormalizeRegistryPath(string path)
    {
        return path
            .Replace("HKCU:\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKCU\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_CURRENT_USER\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKLM:\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKLM\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_LOCAL_MACHINE\\", "", StringComparison.OrdinalIgnoreCase);
    }
}
