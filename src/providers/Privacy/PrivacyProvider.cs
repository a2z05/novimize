using System.Diagnostics;
using System.Text.RegularExpressions;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;

namespace WinOpt.Providers.Privacy;

/// <summary>
/// Provider for privacy-related tweaks. Handles telemetry level,
/// advertising ID, activity history, Cortana, and cloud content settings.
/// Uses a combination of registry checks and PowerShell commands.
/// </summary>
public sealed class PrivacyProvider : ITweakProvider
{
    public string Name => "Privacy";
    // Consolidated: Registry handled by RegistryProvider, PowerShell by CleanupProvider.
    // Retained for name lookup; does not claim a TweakMethod to avoid ProviderRegistry overwrite.
    public IReadOnlyList<TweakMethod> SupportedMethods => Array.Empty<TweakMethod>();

    public async Task<DetectionResult> DetectAsync(TweakDefinition tweak)
    {
        // Prefer registry-based detection when both registry key and value are specified
        if (!string.IsNullOrEmpty(tweak.Detection.RegistryKey) && !string.IsNullOrEmpty(tweak.Detection.RegistryValue))
        {
            return DetectRegistry(tweak);
        }

        // Fall back to command-based detection
        var command = tweak.Detection.Command;
        if (string.IsNullOrEmpty(command))
            return DetectionResult.Failed(tweak.Id, "Missing detection command or registry key/value.");

        try
        {
            var output = await RunPowerShellAsync(command);

            if (!string.IsNullOrEmpty(tweak.Detection.ExtractPattern))
            {
                var match = Regex.Match(output, tweak.Detection.ExtractPattern);
                if (match.Success)
                {
                    var extracted = (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value).Trim();

                    if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                        string.Equals(extracted, tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                        return DetectionResult.Success(tweak.Id, TweakState.Applied, extracted);

                    if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault) &&
                        string.Equals(extracted, tweak.Detection.ExpectedDefault, StringComparison.OrdinalIgnoreCase))
                        return DetectionResult.Success(tweak.Id, TweakState.NotApplied, extracted);

                    return DetectionResult.Success(tweak.Id, TweakState.PartiallyApplied, extracted);
                }

                return DetectionResult.Failed(tweak.Id, $"Pattern '{tweak.Detection.ExtractPattern}' did not match output.");
            }

            var trimmed = output.Trim();
            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                trimmed.Contains(tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                return DetectionResult.Success(tweak.Id, TweakState.Applied, trimmed);

            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault) &&
                trimmed.Contains(tweak.Detection.ExpectedDefault, StringComparison.OrdinalIgnoreCase))
                return DetectionResult.Success(tweak.Id, TweakState.NotApplied, trimmed);

            return DetectionResult.Success(tweak.Id, TweakState.NotApplied, trimmed);
        }
        catch (Exception ex)
        {
            return DetectionResult.Failed(tweak.Id, ex.Message);
        }
    }

    public async Task<ApplyResult> ApplyAsync(TweakDefinition tweak, SnapshotEntry? previousState = null)
    {
        // If this tweak has registry write parameters, use registry-based apply
        if (!string.IsNullOrEmpty(tweak.Apply.RegistryKey) && !string.IsNullOrEmpty(tweak.Apply.RegistryValue))
        {
            return ApplyRegistry(tweak);
        }

        // Fall back to command-based apply
        var command = tweak.Apply.Command;
        if (string.IsNullOrEmpty(command))
            return ApplyResult.Error(tweak.Id, "Missing apply command or registry write spec.");

        try
        {
            // Capture old state first (best-effort)
            string? oldValue = null;
            var detectCmd = tweak.Detection.Command;
            if (!string.IsNullOrEmpty(detectCmd))
            {
                try { oldValue = (await RunPowerShellAsync(detectCmd)).Trim(); }
                catch { /* Best effort */ }
            }
            else if (!string.IsNullOrEmpty(tweak.Detection.RegistryKey) && !string.IsNullOrEmpty(tweak.Detection.RegistryValue))
            {
                oldValue = ReadRegistryValue(tweak.Detection.RegistryKey, tweak.Detection.RegistryValue);
            }

            await RunPowerShellAsync(command);

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = tweak.Id,
                OldValue = oldValue,
                NewValue = tweak.TargetValue,
                Method = tweak.Method,
                Command = command,
                RestoreCommand = tweak.Rollback.Command
            };

            return ApplyResult.Ok(tweak.Id, entry);
        }
        catch (Exception ex)
        {
            return ApplyResult.Error(tweak.Id, ex.Message);
        }
    }

    public async Task<RollbackResult> RollbackAsync(SnapshotEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RestoreCommand))
        {
            return new RollbackResult
            {
                TweakId = entry.TweakId,
                Success = false,
                Message = "No rollback command available."
            };
        }

        try
        {
            await RunPowerShellAsync(entry.RestoreCommand);
            return new RollbackResult { TweakId = entry.TweakId, Success = true };
        }
        catch (Exception ex)
        {
            return new RollbackResult { TweakId = entry.TweakId, Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> VerifyAsync(TweakDefinition tweak)
    {
        var result = await DetectAsync(tweak);
        return result.State == TweakState.Applied;
    }

    private static DetectionResult DetectRegistry(TweakDefinition tweak)
    {
        try
        {
            var keyPath = NormalizeRegistryPath(tweak.Detection.RegistryKey!);
            var valueName = tweak.Detection.RegistryValue!;

            // Try HKLM first, then HKCU
            var valueStr = ReadRegistryValue(tweak.Detection.RegistryKey!, valueName);

            if (valueStr == null)
            {
                // Key doesn't exist — check if that's expected for default state
                if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault))
                    return DetectionResult.Success(tweak.Id, TweakState.NotApplied, null);

                return DetectionResult.Failed(tweak.Id, $"Registry value not found: {tweak.Detection.RegistryKey}\\{valueName}");
            }

            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                string.Equals(valueStr, tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                return DetectionResult.Success(tweak.Id, TweakState.Applied, valueStr);

            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault) &&
                string.Equals(valueStr, tweak.Detection.ExpectedDefault, StringComparison.OrdinalIgnoreCase))
                return DetectionResult.Success(tweak.Id, TweakState.NotApplied, valueStr);

            return DetectionResult.Success(tweak.Id, TweakState.PartiallyApplied, valueStr);
        }
        catch (Exception ex)
        {
            return DetectionResult.Failed(tweak.Id, ex.Message);
        }
    }

    private static ApplyResult ApplyRegistry(TweakDefinition tweak)
    {
        try
        {
            var keyPath = tweak.Apply.RegistryKey!;
            var valueName = tweak.Apply.RegistryValue!;
            var valueData = tweak.Apply.RegistryData ?? tweak.TargetValue;
            var valueType = tweak.Apply.RegistryType ?? tweak.Params.GetValueOrDefault("registryType", "DWORD");

            var fullPath = NormalizeRegistryPath(keyPath);

            // Determine hive from path
            var isHkcu = keyPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
                         keyPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);

            var hive = isHkcu
                ? Microsoft.Win32.Registry.CurrentUser
                : Microsoft.Win32.Registry.LocalMachine;

            // Capture old value
            string? oldValue = null;
            try
            {
                using var readKey = hive.OpenSubKey(fullPath);
                oldValue = readKey?.GetValue(valueName)?.ToString();
            }
            catch { /* Best effort */ }

            // Create or open key and write value
            using var key = hive.CreateSubKey(fullPath);
            if (key == null)
                return ApplyResult.Error(tweak.Id, $"Cannot create/open registry key: {keyPath}");

            switch (valueType.ToUpperInvariant())
            {
                case "DWORD":
                    if (int.TryParse(valueData, out var dwordVal))
                        key.SetValue(valueName, dwordVal, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        return ApplyResult.Error(tweak.Id, $"Cannot parse DWORD value: {valueData}");
                    break;

                case "QWORD":
                    if (long.TryParse(valueData, out var qwordVal))
                        key.SetValue(valueName, qwordVal, Microsoft.Win32.RegistryValueKind.QWord);
                    else
                        return ApplyResult.Error(tweak.Id, $"Cannot parse QWORD value: {valueData}");
                    break;

                case "SZ":
                case "STRING":
                    key.SetValue(valueName, valueData, Microsoft.Win32.RegistryValueKind.String);
                    break;

                case "EXPAND_SZ":
                    key.SetValue(valueName, valueData, Microsoft.Win32.RegistryValueKind.ExpandString);
                    break;

                default:
                    key.SetValue(valueName, valueData, Microsoft.Win32.RegistryValueKind.String);
                    break;
            }

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = $"{keyPath}\\{valueName}",
                OldValue = oldValue,
                NewValue = valueData,
                Method = TweakMethod.Registry,
                Command = $"Set-ItemProperty -Path '{keyPath}' -Name '{valueName}' -Value '{valueData}'",
                RestoreCommand = oldValue != null
                    ? $"Remove-ItemProperty -Path '{keyPath}' -Name '{valueName}' -ErrorAction SilentlyContinue; " +
                      $"if ('{oldValue}') {{ Set-ItemProperty -Path '{keyPath}' -Name '{valueName}' -Value '{oldValue}' }}"
                    : $"Remove-ItemProperty -Path '{keyPath}' -Name '{valueName}' -ErrorAction SilentlyContinue"
            };

            return ApplyResult.Ok(tweak.Id, entry);
        }
        catch (UnauthorizedAccessException)
        {
            return ApplyResult.NeedElevation(tweak.Id);
        }
        catch (Exception ex)
        {
            return ApplyResult.Error(tweak.Id, ex.Message);
        }
    }

    private static string? ReadRegistryValue(string keyPath, string valueName)
    {
        try
        {
            var fullPath = NormalizeRegistryPath(keyPath);
            var isHkcu = keyPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
                         keyPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);

            var hive = isHkcu
                ? Microsoft.Win32.Registry.CurrentUser
                : Microsoft.Win32.Registry.LocalMachine;

            using var key = hive.OpenSubKey(fullPath);
            return key?.GetValue(valueName)?.ToString();
        }
        catch
        {
            return null;
        }
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

    private static async Task<string> RunPowerShellAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start PowerShell process.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(stderr))
            throw new InvalidOperationException($"PowerShell exited with code {process.ExitCode}: {stderr.Trim()}");

        return stdout;
    }
}
