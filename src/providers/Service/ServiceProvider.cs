using System.Diagnostics;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;
using WinOpt.Core;

namespace WinOpt.Providers.Service;

/// <summary>
/// Provider for Windows service tweaks. Handles start type changes,
/// service start/stop, and dependency validation.
/// </summary>
public sealed class ServiceProvider : ITweakProvider
{
    public string Name => "Service";
    public IReadOnlyList<TweakMethod> SupportedMethods => new[] { TweakMethod.Service };

    public Task<DetectionResult> DetectAsync(TweakDefinition tweak)
    {
        var serviceName = tweak.Detection.ServiceName ?? tweak.Params.GetValueOrDefault("serviceName");
        if (string.IsNullOrEmpty(serviceName))
            return Task.FromResult(DetectionResult.Failed(tweak.Id, "Missing serviceName in detection spec."));

        try
        {
            var service = System.ServiceProcess.ServiceController.GetServices()
                .FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));

            if (service == null)
                return Task.FromResult(DetectionResult.Failed(tweak.Id, $"Service '{serviceName}' not found."));

            // Get start type via sc.exe for more reliable results
            var startType = GetServiceStartType(serviceName);

            // Determine state
            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied))
            {
                if (string.Equals(startType, tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(service.Status.ToString(), tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.Applied, startType));
                }
            }

            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault))
            {
                if (string.Equals(startType, tweak.Detection.ExpectedDefault, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.NotApplied, startType));
                }
            }

            // Check if current state matches the target
            var targetStart = tweak.TargetValue.ToLowerInvariant();
            var currentMatchesTarget = startType.ToLowerInvariant() switch
            {
                "auto" or "automatic" => targetStart is "auto" or "automatic",
                "demand" or "manual" => targetStart is "demand" or "manual",
                "disabled" => targetStart == "disabled",
                _ => false
            };

            if (currentMatchesTarget)
                return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.Applied, startType));

            return Task.FromResult(DetectionResult.Success(tweak.Id, TweakState.NotApplied, startType));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DetectionResult.Failed(tweak.Id, ex.Message));
        }
    }

    public Task<ApplyResult> ApplyAsync(TweakDefinition tweak, SnapshotEntry? previousState = null)
    {
        var serviceName = tweak.Apply.ServiceName ?? tweak.Params.GetValueOrDefault("serviceName");
        var targetStartType = tweak.Apply.ServiceStartType ?? tweak.TargetValue;
        var shouldStop = tweak.Apply.ServiceStop;
        var shouldStart = tweak.Apply.ServiceStart;

        if (string.IsNullOrEmpty(serviceName))
            return Task.FromResult(ApplyResult.Error(tweak.Id, "Missing serviceName in apply spec."));

        try
        {
            // Capture current state
            var oldStartType = GetServiceStartType(serviceName);
            var oldStatus = GetServiceStatus(serviceName);

            // Set start type via sc.exe
            var startArg = targetStartType.ToLowerInvariant() switch
            {
                "auto" or "automatic" => "auto",
                "delayed" or "delayed-auto" => "delayed-auto",
                "demand" or "manual" => "demand",
                "disabled" => "disabled",
                _ => targetStartType
            };

            var exitCode = RunCommand("sc.exe", $"config \"{serviceName}\" start= {startArg}");
            if (exitCode != 0)
            {
                if (exitCode == 5)
                    return Task.FromResult(ApplyResult.NeedElevation(tweak.Id));
                return Task.FromResult(ApplyResult.Error(tweak.Id, $"sc.exe config failed with exit code {exitCode}."));
            }

            // Handle stop
            if (shouldStop && string.Equals(oldStatus, "Running", StringComparison.OrdinalIgnoreCase))
            {
                RunCommand("sc.exe", $"stop \"{serviceName}\"");
                Thread.Sleep(1000); // Give it time to stop
            }

            // Handle start
            if (shouldStart)
            {
                RunCommand("sc.exe", $"start \"{serviceName}\"");
                Thread.Sleep(1000);
            }

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = serviceName,
                OldValue = oldStartType,
                NewValue = startArg,
                Method = TweakMethod.Service,
                Command = $"sc.exe config \"{serviceName}\" start= {startArg}",
                RestoreCommand = $"sc.exe config \"{serviceName}\" start= {MapStartTypeForSc(oldStartType)}"
            };

            return Task.FromResult(ApplyResult.Ok(tweak.Id, entry));
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
            if (string.IsNullOrEmpty(entry.OldValue))
                return Task.FromResult(new RollbackResult { TweakId = entry.TweakId, Success = false, Message = "No old value in snapshot." });

            var serviceName = entry.Target;
            var oldStartType = MapStartTypeForSc(entry.OldValue);

            var exitCode = RunCommand("sc.exe", $"config \"{serviceName}\" start= {oldStartType}");
            if (exitCode != 0)
                return Task.FromResult(new RollbackResult { TweakId = entry.TweakId, Success = false, Message = $"sc.exe config failed with exit code {exitCode}." });

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

    private static string GetServiceStartType(string serviceName)
    {
        try
        {
            var output = RunCommandOutput("sc.exe", $"qc \"{serviceName}\"");
            // Parse "START_TYPE : 2 AUTO_START"
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("START_TYPE", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':', StringSplitOptions.TrimEntries);
                    if (parts.Length > 1)
                    {
                        var val = parts[1].Split(' ')[0]; // Get just the number
                        return val switch
                        {
                            "2" => "Auto",
                            "3" => "Manual",
                            "4" => "Disabled",
                            "0" => "Boot",
                            "1" => "System",
                            _ => parts[1]
                        };
                    }
                }
            }
        }
        catch { }
        return "Unknown";
    }

    private static string GetServiceStatus(string serviceName)
    {
        try
        {
            var controller = new System.ServiceProcess.ServiceController(serviceName);
            return controller.Status.ToString();
        }
        catch { return "Unknown"; }
    }

    private static string MapStartTypeForSc(string startType)
    {
        return startType.ToLowerInvariant() switch
        {
            "auto" or "automatic" => "auto",
            "manual" or "demand" => "demand",
            "disabled" => "disabled",
            "delayed-auto" or "delayed" => "delayed-auto",
            "2" => "auto",
            "3" => "demand",
            "4" => "disabled",
            _ => "demand"
        };
    }

    private static int RunCommand(string fileName, string arguments)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(10000);
            return process?.ExitCode ?? -1;
        }
        catch { return -1; }
    }

    private static string RunCommandOutput(string fileName, string arguments)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return "";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);
            return output;
        }
        catch { return ""; }
    }
}
