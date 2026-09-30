using System.Diagnostics;
using System.Text.RegularExpressions;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;

namespace WinOpt.Providers.Storage;

/// <summary>
/// Provider for storage-related tweaks. Handles TRIM verification,
/// write caching, NTFS optimization, defrag/trim, and Storage Sense.
/// </summary>
public sealed class StorageProvider : ITweakProvider
{
    public string Name => "Storage";
    // Consolidated: PowerShell/Script handled by PowerShellProvider.
    // Retained for name-based lookup; does not claim dispatch.
    public IReadOnlyList<TweakMethod> SupportedMethods => Array.Empty<TweakMethod>();

    public async Task<DetectionResult> DetectAsync(TweakDefinition tweak)
    {
        var command = tweak.Detection.Command ?? tweak.Params.GetValueOrDefault("detectCommand");
        if (string.IsNullOrEmpty(command))
            return DetectionResult.Failed(tweak.Id, "Missing command in detection spec.");

        try
        {
            var output = await RunCommandAsync(command);

            // Apply regex extraction if specified
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

            // No regex — compare raw output
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
        var command = tweak.Apply.Command ?? tweak.Params.GetValueOrDefault("applyCommand");
        if (string.IsNullOrEmpty(command))
            return ApplyResult.Error(tweak.Id, "Missing command in apply spec.");

        try
        {
            // Capture old state first (best-effort)
            string? oldValue = null;
            var detectCmd = tweak.Detection.Command ?? tweak.Params.GetValueOrDefault("detectCommand");
            if (!string.IsNullOrEmpty(detectCmd))
            {
                try { oldValue = (await RunCommandAsync(detectCmd)).Trim(); }
                catch { /* Best effort */ }
            }

            var output = await RunCommandAsync(command);

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = tweak.Id,
                OldValue = oldValue,
                NewValue = tweak.TargetValue,
                Method = tweak.Method,
                Command = command,
                RestoreCommand = tweak.Rollback.Command ?? tweak.Params.GetValueOrDefault("rollbackCommand")
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
            await RunCommandAsync(entry.RestoreCommand);
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

    private static async Task<string> RunCommandAsync(string command)
    {
        // Determine whether this is a PowerShell command or a native command (fsutil, defrag, etc.)
        if (IsNativeCommand(command))
        {
            return await RunNativeCommandAsync(command);
        }

        return await RunPowerShellAsync(command);
    }

    private static bool IsNativeCommand(string command)
    {
        var trimmed = command.TrimStart();
        return trimmed.StartsWith("fsutil", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("defrag", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("del ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("rd ", StringComparison.OrdinalIgnoreCase);
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

    private static async Task<string> RunNativeCommandAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start command process.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(stderr))
            throw new InvalidOperationException($"Command exited with code {process.ExitCode}: {stderr.Trim()}");

        return stdout;
    }
}
