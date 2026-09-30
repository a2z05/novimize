using System.Diagnostics;
using System.Text.RegularExpressions;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;

namespace WinOpt.Providers.ScheduledTask;

/// <summary>
/// Provider for scheduled task management. Handles task enumeration,
/// enable/disable operations via PowerShell Get-ScheduledTask,
/// Disable-ScheduledTask, and Enable-ScheduledTask.
/// </summary>
public sealed class ScheduledTaskProvider : ITweakProvider
{
    public string Name => "ScheduledTask";
    public IReadOnlyList<TweakMethod> SupportedMethods => new[] { TweakMethod.TaskScheduler };

    public async Task<DetectionResult> DetectAsync(TweakDefinition tweak)
    {
        var command = tweak.Detection.Command ?? tweak.Params.GetValueOrDefault("detectCommand");
        if (string.IsNullOrEmpty(command))
            return DetectionResult.Failed(tweak.Id, "Missing command in detection spec.");

        try
        {
            var output = await RunPowerShellAsync(command);

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

            // No regex — check for task state in output
            var trimmed = output.Trim();

            // Look for the "State" field in Get-ScheduledTask output
            var stateMatch = Regex.Match(trimmed, @"State\s*:\s*(\S+)", RegexOptions.IgnoreCase);
            if (stateMatch.Success)
            {
                var taskState = stateMatch.Groups[1].Value;

                if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                    string.Equals(taskState, tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                    return DetectionResult.Success(tweak.Id, TweakState.Applied, taskState);

                if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault) &&
                    string.Equals(taskState, tweak.Detection.ExpectedDefault, StringComparison.OrdinalIgnoreCase))
                    return DetectionResult.Success(tweak.Id, TweakState.NotApplied, taskState);

                return DetectionResult.Success(tweak.Id, TweakState.PartiallyApplied, taskState);
            }

            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                trimmed.Contains(tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                return DetectionResult.Success(tweak.Id, TweakState.Applied, trimmed);

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
                try { oldValue = (await RunPowerShellAsync(detectCmd)).Trim(); }
                catch { /* Best effort */ }
            }

            // Extract task state before apply for the snapshot
            var taskName = tweak.Params.GetValueOrDefault("taskName") ?? tweak.TargetValue;
            var previousTaskState = await GetTaskStateAsync(taskName);

            var output = await RunPowerShellAsync(command);

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = taskName,
                OldValue = previousTaskState ?? oldValue,
                NewValue = tweak.TargetValue,
                Method = tweak.Method,
                Command = command,
                RestoreCommand = tweak.Rollback.Command ?? tweak.Params.GetValueOrDefault("rollbackCommand")
                    ?? GenerateRestoreCommand(taskName, previousTaskState)
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

    private async Task<string?> GetTaskStateAsync(string taskName)
    {
        if (string.IsNullOrEmpty(taskName))
            return null;

        try
        {
            var command = $"(Get-ScheduledTask -TaskName '{taskName.Replace("'", "''")}' -ErrorAction SilentlyContinue).State";
            var output = await RunPowerShellAsync(command);
            var state = output.Trim();
            return string.IsNullOrEmpty(state) ? null : state;
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateRestoreCommand(string taskName, string? previousState)
    {
        if (string.IsNullOrEmpty(taskName) || string.IsNullOrEmpty(previousState))
            return string.Empty;

        // Map the previous state to the appropriate Enable/Disable command
        if (string.Equals(previousState, "Disabled", StringComparison.OrdinalIgnoreCase))
            return $"Disable-ScheduledTask -TaskName '{taskName.Replace("'", "''")}' -ErrorAction SilentlyContinue";

        return $"Enable-ScheduledTask -TaskName '{taskName.Replace("'", "''")}' -ErrorAction SilentlyContinue";
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
