using System.Diagnostics;
using System.Text.RegularExpressions;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;

namespace WinOpt.Providers.NetSh;

/// <summary>
/// Provider for netsh-based network tweaks. Handles TCP/IP stack
/// configuration, NIC properties, and network optimization.
/// </summary>
public sealed class NetShProvider : ITweakProvider
{
    public string Name => "NetSh";
    public IReadOnlyList<TweakMethod> SupportedMethods => new[] { TweakMethod.NetSh };

    public async Task<DetectionResult> DetectAsync(TweakDefinition tweak)
    {
        var command = tweak.Detection.Command;
        if (string.IsNullOrEmpty(command))
            return DetectionResult.Failed(tweak.Id, "Missing command in detection spec.");

        try
        {
            var output = await RunNetShAsync(command);

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
            }

            var trimmed = output.Trim();
            return DetectionResult.Success(tweak.Id, TweakState.NotApplied, trimmed);
        }
        catch (Exception ex)
        {
            return DetectionResult.Failed(tweak.Id, ex.Message);
        }
    }

    public async Task<ApplyResult> ApplyAsync(TweakDefinition tweak, SnapshotEntry? previousState = null)
    {
        var command = tweak.Apply.Command;
        if (string.IsNullOrEmpty(command))
            return ApplyResult.Error(tweak.Id, "Missing command in apply spec.");

        try
        {
            // Capture old state
            string? oldValue = null;
            var detectCmd = tweak.Detection.Command ?? tweak.Params.GetValueOrDefault("detectCommand");
            if (!string.IsNullOrEmpty(detectCmd))
            {
                try
                {
                    var detectOutput = await RunNetShAsync(detectCmd);
                    oldValue = ExtractValue(detectOutput, tweak.Detection.ExtractPattern);
                }
                catch { /* Best effort */ }
            }

            var output = await RunNetShAsync(command);

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = command,
                OldValue = oldValue,
                NewValue = tweak.TargetValue,
                Method = TweakMethod.NetSh,
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
            // Try to restore by re-reading old value and applying it
            return new RollbackResult
            {
                TweakId = entry.TweakId,
                Success = false,
                Message = "No rollback command available. Use 'netsh int tcp reset' for full TCP/IP reset."
            };
        }

        try
        {
            await RunNetShAsync(entry.RestoreCommand);
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

    private static string? ExtractValue(string output, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return output.Trim();

        var match = Regex.Match(output, pattern);
        return match.Success
            ? (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value).Trim()
            : output.Trim();
    }

    private static async Task<string> RunNetShAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start netsh process.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }
}
