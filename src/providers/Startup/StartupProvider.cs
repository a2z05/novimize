using System.Diagnostics;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;
using RegKey = Microsoft.Win32.Registry;
using RegKeyKind = Microsoft.Win32.RegistryValueKind;

namespace WinOpt.Providers.Startup;

/// <summary>
/// Provider for startup management tweaks. Handles Registry Run key
/// management, startup folder enumeration, and startup impact assessment.
/// </summary>
public sealed class StartupProvider : ITweakProvider
{
    public string Name => "Startup";
    // Consolidated: Script handled by PowerShellProvider; Startup domain logic
    // retained for name lookup but does not claim dispatch to avoid overwrite.
    public IReadOnlyList<TweakMethod> SupportedMethods => Array.Empty<TweakMethod>();

    public async Task<DetectionResult> DetectAsync(TweakDefinition tweak)
    {
        var command = tweak.Detection.Command ?? tweak.Params.GetValueOrDefault("detectCommand");

        // If a registry key/value is specified, check that directly
        if (!string.IsNullOrEmpty(tweak.Detection.RegistryKey) && !string.IsNullOrEmpty(tweak.Detection.RegistryValue))
        {
            return DetectRegistryStartupItem(tweak);
        }

        // Command-based detection (e.g. listing startup folder items, Get-CimInstance)
        if (string.IsNullOrEmpty(command))
            return DetectionResult.Failed(tweak.Id, "Missing detection command or registry key/value.");

        try
        {
            var output = await RunPowerShellAsync(command);
            var trimmed = output.Trim();

            // Check if the target startup item exists in output
            var targetItem = tweak.Params.GetValueOrDefault("startupItem") ?? tweak.TargetValue;

            if (!string.IsNullOrEmpty(targetItem) && trimmed.Contains(targetItem, StringComparison.OrdinalIgnoreCase))
            {
                // Item is present in startup — check if it's enabled
                if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                    trimmed.Contains(tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                    return DetectionResult.Success(tweak.Id, TweakState.Applied, trimmed);

                // Item found but state unknown
                return DetectionResult.Success(tweak.Id, TweakState.PartiallyApplied, trimmed);
            }

            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault) &&
                trimmed.Contains(tweak.Detection.ExpectedDefault, StringComparison.OrdinalIgnoreCase))
                return DetectionResult.Success(tweak.Id, TweakState.NotApplied, trimmed);

            // Item not found in startup — effectively disabled/not applied
            return DetectionResult.Success(tweak.Id, TweakState.NotApplied, trimmed);
        }
        catch (Exception ex)
        {
            return DetectionResult.Failed(tweak.Id, ex.Message);
        }
    }

    public async Task<ApplyResult> ApplyAsync(TweakDefinition tweak, SnapshotEntry? previousState = null)
    {
        // If registry-based disable is specified, delete/rename the registry value
        if (!string.IsNullOrEmpty(tweak.Apply.RegistryKey) && !string.IsNullOrEmpty(tweak.Apply.RegistryValue))
        {
            return ApplyRegistryStartupDisable(tweak);
        }

        // Command-based apply (e.g. Remove-Item to delete from startup folder)
        var command = tweak.Apply.Command;
        if (string.IsNullOrEmpty(command))
            return ApplyResult.Error(tweak.Id, "Missing apply command or registry write spec.");

        try
        {
            // Capture old state
            string? oldValue = null;
            var detectCmd = tweak.Detection.Command ?? tweak.Params.GetValueOrDefault("detectCommand");
            if (!string.IsNullOrEmpty(detectCmd))
            {
                try { oldValue = (await RunPowerShellAsync(detectCmd)).Trim(); }
                catch { /* Best effort */ }
            }

            await RunPowerShellAsync(command);

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = tweak.Params.GetValueOrDefault("startupItem") ?? tweak.Id,
                OldValue = oldValue,
                NewValue = "Disabled",
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

    private static DetectionResult DetectRegistryStartupItem(TweakDefinition tweak)
    {
        try
        {
            var keyPath = tweak.Detection.RegistryKey!;
            var valueName = tweak.Detection.RegistryValue!;

            // Check both HKLM and HKCU Run keys
            var valueStr = ReadStartupRegistryValue(keyPath, valueName);

            // Item exists in registry — it's enabled
            if (!string.IsNullOrEmpty(valueStr))
            {
                if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                    string.Equals(valueStr, tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                    return DetectionResult.Success(tweak.Id, TweakState.Applied, valueStr);

                return DetectionResult.Success(tweak.Id, TweakState.PartiallyApplied, valueStr);
            }

            // Value not found — item is disabled (renamed or deleted from Run key)
            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault))
                return DetectionResult.Success(tweak.Id, TweakState.NotApplied, null);

            return DetectionResult.Success(tweak.Id, TweakState.NotApplied, null);
        }
        catch (Exception ex)
        {
            return DetectionResult.Failed(tweak.Id, ex.Message);
        }
    }

    private static ApplyResult ApplyRegistryStartupDisable(TweakDefinition tweak)
    {
        try
        {
            var keyPath = tweak.Apply.RegistryKey!;
            var valueName = tweak.Apply.RegistryValue!;
            var valueType = tweak.Apply.RegistryType ?? tweak.Params.GetValueOrDefault("registryType", "SZ");

            var normalizedPath = NormalizeRegistryPath(keyPath);

            // Determine hive
            var isHkcu = keyPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
                         keyPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);

            var actualHive = isHkcu ? RegKey.CurrentUser : RegKey.LocalMachine;

            // Read old value
            string? oldValue = null;
            try
            {
                using var readKey = actualHive.OpenSubKey(normalizedPath);
                oldValue = readKey?.GetValue(valueName)?.ToString();
            }
            catch { /* Best effort */ }

            if (oldValue == null)
                return ApplyResult.Ok(tweak.Id, new SnapshotEntry
                {
                    TweakId = tweak.Id,
                    Target = $"{keyPath}::{valueName}",
                    OldValue = null,
                    NewValue = "Already disabled",
                    Method = TweakMethod.Registry
                });

            // Rename value to disable (prefix with underscore) rather than deleting
            var disabledName = $"_{valueName}";
            using var key = actualHive.OpenSubKey(normalizedPath, true);
            if (key == null)
                return ApplyResult.Error(tweak.Id, $"Cannot open registry key for writing: {keyPath}");

            key.SetValue(disabledName, oldValue, RegKeyKind.String);
            key.DeleteValue(valueName, throwOnMissingValue: false);

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = $"{keyPath}\\{valueName}",
                OldValue = oldValue,
                NewValue = $"[renamed to {disabledName}]",
                Method = TweakMethod.Registry,
                Command = $"Rename-ItemProperty -Path '{keyPath}' -Name '{valueName}' -NewName '{disabledName}'",
                RestoreCommand = $"Rename-ItemProperty -Path '{keyPath}' -Name '{disabledName}' -NewName '{valueName}'"
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

    private static string? ReadStartupRegistryValue(string keyPath, string valueName)
    {
        try
        {
            var fullPath = NormalizeRegistryPath(keyPath);
            var isHkcu = keyPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
                         keyPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);

            var hive = isHkcu ? RegKey.CurrentUser : RegKey.LocalMachine;
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
