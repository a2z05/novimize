using System.Diagnostics;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;
using WinOpt.Engine.Detection;
using WinOpt.Engine.Logging;
using WinOpt.Engine.Providers;
using WinOpt.Engine.Security;
using WinOpt.Engine.Snapshots;
using WinOpt.Engine.Tweaks;

namespace WinOpt.Engine;

/// <summary>
/// Optimization mode for auto-optimize.
/// </summary>
public enum OptimizeMode
{
    Safe,
    Balanced,
    Gaming,
    Network,
    Cleanup,
    Privacy,
    Aggressive
}

/// <summary>
/// Top-level optimization engine. Orchestrates detection, security
/// checks, application, verification, and rollback.
/// </summary>
public sealed class OptimizeEngine : IDisposable
{
    private readonly TweakDatabase _database;
    private readonly ProviderRegistry _providers;
    private readonly SystemDetector _detector;
    private readonly SecurityGuard _security;
    private readonly SnapshotManager _snapshots;
    private readonly WinOptLogger _logger;

    public OptimizeEngine(string? tweaksDir = null, string? logDir = null)
    {
        _logger = new WinOptLogger(logDir);
        _database = new TweakDatabase(tweaksDir);
        _providers = new ProviderRegistry();
        _detector = new SystemDetector();
        _security = new SecurityGuard(_logger);
        _snapshots = new SnapshotManager(_logger);
    }

    /// <summary>
    /// Access the tweak database.
    /// </summary>
    public TweakDatabase Database => _database;

    /// <summary>
    /// Access the provider registry.
    /// </summary>
    public ProviderRegistry Providers => _providers;

    /// <summary>
    /// Access the snapshot manager.
    /// </summary>
    public SnapshotManager Snapshots => _snapshots;

    /// <summary>
    /// Access the logger.
    /// </summary>
    public WinOptLogger Logger => _logger;

    // ===== System Detection =====

    /// <summary>
    /// Detect complete system information.
    /// </summary>
    public async Task<SystemInfo> DetectSystemAsync()
    {
        _logger.Info("Detecting system information...", "detect");
        var info = await _detector.DetectAsync();
        _logger.Info($"System: {info.OsEdition} Build {info.BuildNumber} | {info.CpuName} | {info.RamTotalGb}GB | {info.GpuName} | {info.FormFactor}", "detect");
        _logger.Info($"Overall tier: {info.OverallTier} (CPU={info.CpuTier} RAM={info.RamTier} Storage={info.StorageTier} GPU={info.GpuTier})", "detect");
        return info;
    }

    // ===== State Detection =====

    /// <summary>
    /// Detect the state of all tweaks (or a subset).
    /// </summary>
    public async Task<List<DetectionResult>> ScanAsync(
        IReadOnlyList<TweakDefinition>? tweaks = null)
    {
        tweaks ??= _database.Tweaks.Values.ToList();
        var results = new List<DetectionResult>();

        _logger.Info($"Scanning {tweaks.Count} tweaks...", "scan");

        foreach (var tweak in tweaks)
        {
            var provider = _providers.GetProviderFor(tweak);
            if (provider == null)
            {
                results.Add(DetectionResult.Failed(tweak.Id, $"No provider for method {tweak.Method}"));
                continue;
            }

            try
            {
                var result = await provider.DetectAsync(tweak);
                results.Add(result);
            }
            catch (Exception ex)
            {
                results.Add(DetectionResult.Failed(tweak.Id, ex.Message));
                _logger.Error($"Detection failed for {tweak.Id}: {ex.Message}", "scan", tweak.Id);
            }
        }

        var applied = results.Count(r => r.State == TweakState.Applied);
        var notApplied = results.Count(r => r.State == TweakState.NotApplied);
        var failed = results.Count(r => r.State == TweakState.DetectionFailed);
        _logger.Info($"Scan complete: {applied} applied, {notApplied} not applied, {failed} failed", "scan");

        return results;
    }

    /// <summary>
    /// Detect the state of a single tweak.
    /// </summary>
    public async Task<DetectionResult> ScanSingleAsync(string tweakId)
    {
        var tweak = _database.Get(tweakId);
        if (tweak == null)
            return DetectionResult.Failed(tweakId, $"Tweak '{tweakId}' not found in database.");

        var provider = _providers.GetProviderFor(tweak);
        if (provider == null)
            return DetectionResult.Failed(tweakId, $"No provider for method {tweak.Method}.");

        return await provider.DetectAsync(tweak);
    }

    // ===== Apply =====

    /// <summary>
    /// Apply a single tweak with full security check, snapshot, and verification.
    /// </summary>
    public async Task<TweakResult> ApplyAsync(string tweakId, bool dryRun = false,
        bool isAutoMode = false, string? snapshotId = null)
    {
        var tweak = _database.Get(tweakId);
        if (tweak == null)
            return new TweakResult { TweakId = tweakId, Status = TweakResultStatus.Failed, Message = "Tweak not found." };

        // Security check
        var (allowed, reason) = _security.CheckCanApply(tweak, isAutoMode);
        if (!allowed)
        {
            _logger.Warn($"Security blocked: {reason}", "apply", tweakId);
            return new TweakResult { TweakId = tweakId, Status = TweakResultStatus.SecurityBlocked, Message = reason };
        }

        // Get provider
        var provider = _providers.GetProviderFor(tweak);
        if (provider == null)
            return new TweakResult { TweakId = tweakId, Status = TweakResultStatus.Failed, Message = $"No provider for {tweak.Method}." };

        // A definition with no apply step is a measurement, not a change - skip it
        // rather than failing the session with "Missing command in apply spec".
        // Providers also read params as a fallback, so those count as an apply step.
        var applyCommand = tweak.Apply?.Command ?? tweak.Params.GetValueOrDefault("applyCommand");
        if (string.IsNullOrEmpty(applyCommand) &&
            string.IsNullOrEmpty(tweak.Apply?.RegistryKey) &&
            string.IsNullOrEmpty(tweak.Params.GetValueOrDefault("registryKey")) &&
            string.IsNullOrEmpty(tweak.Apply?.ServiceName) &&
            string.IsNullOrEmpty(tweak.Params.GetValueOrDefault("serviceName")))
        {
            return new TweakResult
            {
                TweakId = tweakId,
                Status = TweakResultStatus.Skipped,
                Message = "Measurement only - this tweak reports a value and makes no changes.",
                PreviousState = TweakState.NotApplied
            };
        }

        // Detect current state
        var detection = await provider.DetectAsync(tweak);
        if (detection.State == TweakState.Applied)
        {
            return new TweakResult
            {
                TweakId = tweakId,
                Status = TweakResultStatus.AlreadyApplied,
                Message = "Tweak is already applied.",
                PreviousState = TweakState.Applied,
                CurrentState = TweakState.Applied
            };
        }

        if (detection.State == TweakState.Incompatible)
        {
            return new TweakResult
            {
                TweakId = tweakId,
                Status = TweakResultStatus.Incompatible,
                Message = detection.Message ?? "Incompatible with current system."
            };
        }

        if (detection.State == TweakState.ConflictsDetected)
        {
            return new TweakResult
            {
                TweakId = tweakId,
                Status = TweakResultStatus.ConflictsDetected,
                Message = detection.Message ?? "Conflicts detected."
            };
        }

        if (dryRun)
        {
            return new TweakResult
            {
                TweakId = tweakId,
                Status = TweakResultStatus.Success,
                Message = $"Dry run: would change {detection.CurrentValue ?? "unknown"} → {tweak.TargetValue}",
                PreviousState = detection.State
            };
        }

        // Ensure snapshot
        if (snapshotId == null)
        {
            var snapshot = _snapshots.Create($"Auto-created before applying {tweakId}", null);
            snapshotId = snapshot.Id;
        }

        // Apply
        var sw = Stopwatch.StartNew();
        _logger.Info($"Applying: {tweakId} ({tweak.Name}) — {detection.CurrentValue ?? "?"} → {tweak.TargetValue}", "apply", tweakId);

        var applyResult = await provider.ApplyAsync(tweak);
        sw.Stop();

        if (!applyResult.Success)
        {
            if (applyResult.RequiresElevation)
            {
                return new TweakResult
                {
                    TweakId = tweakId,
                    Status = TweakResultStatus.RequiresElevation,
                    Message = "Elevation required to apply this tweak."
                };
            }

            _logger.Error($"Apply failed: {applyResult.Message}", "apply", tweakId);
            return new TweakResult
            {
                TweakId = tweakId,
                Status = TweakResultStatus.Failed,
                Message = applyResult.Message ?? "Apply failed."
            };
        }

        // Record snapshot entry
        if (applyResult.SnapshotEntry != null)
        {
            _snapshots.AddEntry(snapshotId, applyResult.SnapshotEntry);
        }

        // Verify (skip if tweak has no verify block — apply-only tweaks like cleanup)
        bool verified;
        var hasVerify = !string.IsNullOrEmpty(tweak.Verify?.Command);
        if (hasVerify)
        {
            verified = await provider.VerifyAsync(tweak);
        }
        else
        {
            verified = true; // apply-only: success = command ran without error
        }
        if (!verified)
        {
            _logger.Warn($"Verification failed for {tweakId} — considering rollback", "apply", tweakId);
            return new TweakResult
            {
                TweakId = tweakId,
                Status = TweakResultStatus.VerificationFailed,
                PreviousState = detection.State,
                CurrentState = TweakState.NotApplied,
                Message = "Verification failed after apply."
            };
        }

        // Audit
        _logger.AuditApply(tweak, tweakId, detection.CurrentValue, tweak.TargetValue,
            applyResult.SnapshotEntry?.Command, true, snapshotId: snapshotId);

        _logger.Info($"Applied successfully: {tweakId} ({sw.ElapsedMilliseconds}ms)", "apply", tweakId);

        return new TweakResult
        {
            TweakId = tweakId,
            Status = TweakResultStatus.Success,
            PreviousState = detection.State,
            CurrentState = TweakState.Applied,
            Verified = true,
            SnapshotEntry = applyResult.SnapshotEntry
        };
    }

    /// <summary>
    /// Apply multiple tweaks with snapshot protection.
    /// </summary>
    public async Task<SessionResult> ApplyBatchAsync(
        IReadOnlyList<TweakDefinition> tweaks,
        bool dryRun = false,
        bool isAutoMode = false,
        string? description = null)
    {
        var sw = Stopwatch.StartNew();
        var sessionResult = new SessionResult();

        // Pre-flight: security check all
        if (!dryRun)
        {
            var securityIssues = _security.CheckBulkSecurity(tweaks, isAutoMode);
            if (securityIssues.Count > 0)
            {
                foreach (var issue in securityIssues)
                    _logger.Warn($"Security: {issue}", "apply");
            }
        }

        // Create snapshot
        string? snapshotId = null;
        if (!dryRun)
        {
            var snapshot = _snapshots.Create(
                description ?? $"Batch apply: {tweaks.Count} tweaks",
                null);
            snapshotId = snapshot.Id;
            sessionResult = sessionResult with { SnapshotId = snapshotId };
        }

        // Detect conflicts
        var conflicts = _database.FindConflicts(tweaks);
        if (conflicts.Count > 0)
        {
            foreach (var (a, b, reason) in conflicts)
                _logger.Warn($"Conflict: {reason}", "apply");
        }

        // Apply each tweak
        foreach (var tweak in tweaks)
        {
            sessionResult = sessionResult with { TweaksAttempted = sessionResult.TweaksAttempted + 1 };

            var result = await ApplyAsync(tweak.Id, dryRun, isAutoMode, snapshotId);
            sessionResult.Results.Add(result);

            switch (result.Status)
            {
                case TweakResultStatus.Success:
                case TweakResultStatus.AlreadyApplied:
                    sessionResult = sessionResult with { TweaksSucceeded = sessionResult.TweaksSucceeded + 1 };
                    break;
                case TweakResultStatus.Skipped:
                    sessionResult = sessionResult with { TweaksSkipped = sessionResult.TweaksSkipped + 1 };
                    break;
                case TweakResultStatus.RequiresElevation:
                    // Not a defect in the tweak — the shell simply isn't elevated.
                    // Counting it as a failure made every unelevated run look broken.
                    sessionResult = sessionResult with
                    {
                        TweaksNeedElevation = sessionResult.TweaksNeedElevation + 1
                    };
                    _logger.Info($"Skipped (needs admin): {result.TweakId}", "apply", result.TweakId);
                    break;
                default:
                    sessionResult = sessionResult with { TweaksFailed = sessionResult.TweaksFailed + 1 };
                    break;
            }
        }

        sw.Stop();
        sessionResult.Duration = sw.Elapsed;

        _logger.Info($"Batch complete: {sessionResult.TweaksSucceeded} succeeded, " +
            $"{sessionResult.TweaksFailed} failed, {sessionResult.TweaksSkipped} skipped, " +
            $"{sessionResult.TweaksNeedElevation} need admin " +
            $"in {sw.ElapsedMilliseconds}ms", "apply");

        return sessionResult;
    }

    // ===== Rollback =====

    /// <summary>
    /// Rollback a single tweak using its snapshot entry.
    /// </summary>
    public async Task<RollbackResult> RollbackAsync(string tweakId, string snapshotId)
    {
        var snapshot = _snapshots.Load(snapshotId);
        if (snapshot == null)
            return new RollbackResult { TweakId = tweakId, Success = false, Message = $"Snapshot '{snapshotId}' not found." };

        var entry = snapshot.Entries.FirstOrDefault(e => e.TweakId == tweakId);
        if (entry == null)
            return new RollbackResult { TweakId = tweakId, Success = false, Message = $"No snapshot entry for tweak '{tweakId}'." };

        var provider = _providers.GetProvider(entry.Method);
        if (provider == null)
            return new RollbackResult { TweakId = tweakId, Success = false, Message = $"No provider for method {entry.Method}." };

        _logger.Info($"Rolling back: {tweakId}", "rollback", tweakId);

        var result = await provider.RollbackAsync(entry);

        _logger.AuditRollback(tweakId, entry.Method, entry.Target, entry.OldValue,
            result.Success, snapshotId);

        if (result.Success)
            _logger.Info($"Rollback successful: {tweakId}", "rollback", tweakId);
        else
            _logger.Error($"Rollback failed: {tweakId} — {result.Message}", "rollback", tweakId);

        return result;
    }

    /// <summary>
    /// Rollback all tweaks from a snapshot.
    /// </summary>
    public async Task<int> RollbackAllAsync(string snapshotId)
    {
        var snapshot = _snapshots.Load(snapshotId);
        if (snapshot == null) return 0;

        var successCount = 0;
        foreach (var entry in snapshot.Entries)
        {
            var result = await RollbackAsync(entry.TweakId, snapshotId);
            if (result.Success) successCount++;
        }

        _logger.Info($"Rollback complete: {successCount}/{snapshot.Entries.Count} successful", "rollback");
        return successCount;
    }

    // ===== Profile-based =====

    /// <summary>
    /// Apply an optimization profile.
    /// </summary>
    public async Task<SessionResult> ApplyProfileAsync(string profileId,
        SystemInfo systemInfo, bool dryRun = false, bool isAutoMode = false)
    {
        var profile = BuiltInProfiles.All.FirstOrDefault(p => p.Id == profileId);
        if (profile == null)
            return new SessionResult { TweaksFailed = 1 };

        // Get compatible tweaks for this profile and system
        var profileTweaks = _database.GetForProfile(profile);
        var compatibleTweaks = _database.FilterCompatible(profileTweaks, systemInfo);

        _logger.Info($"Profile '{profile.Name}': {compatibleTweaks.Count} compatible tweaks out of {profileTweaks.Count}", "profile");

        return await ApplyBatchAsync(compatibleTweaks, dryRun, isAutoMode,
            $"Profile: {profile.Name}");
    }

    /// <summary>
    /// Auto-optimize: select profile based on system info, apply safe tweaks.
    /// </summary>
    public async Task<SessionResult> AutoOptimizeAsync(
        SystemInfo systemInfo, OptimizeMode mode = OptimizeMode.Balanced,
        bool dryRun = false)
    {
        // Map mode to profile
        var profileId = mode switch
        {
            OptimizeMode.Gaming => "gaming",
            OptimizeMode.Network => "daily",
            OptimizeMode.Cleanup => "daily",
            OptimizeMode.Privacy => "daily",
            OptimizeMode.Safe => "daily",
            OptimizeMode.Aggressive => "potato-pc",
            _ => "daily"
        };

        var profile = BuiltInProfiles.All.FirstOrDefault(p => p.Id == profileId);
        if (profile == null)
            return new SessionResult { TweaksFailed = 1, TweaksAttempted = 1 };

        var profileTweaks = _database.GetForProfile(profile);
        var compatibleTweaks = _database.FilterCompatible(profileTweaks, systemInfo);

        // Filter to only Safe tweaks in auto mode
        var safeTweaks = compatibleTweaks.Where(t =>
            t.Risk == RiskLevel.Safe && t.Evidence >= 4).ToList();

        _logger.Info($"Auto-optimize ({mode}): {safeTweaks.Count} safe tweaks selected", "auto");

        return await ApplyBatchAsync(safeTweaks, dryRun, isAutoMode: true,
            $"Auto-optimize: {mode}");
    }

    // ===== Diagnostics =====

    /// <summary>
    /// Run system health diagnostics.
    /// </summary>
    public async Task<DiagnosticReport> RunDiagnosticsAsync()
    {
        var report = new DiagnosticReport();
        var systemInfo = await DetectSystemAsync();
        report.SystemInfo = systemInfo;

        // Check critical services
        var criticalServices = new[] { "WinDefend", "MpsSvc", "BFE", "wuauserv", "RpcSs", "DcomLaunch" };
        foreach (var svc in criticalServices)
        {
            var detection = await DetectServiceStateAsync(svc);
            report.ServiceChecks.Add(new DiagnosticCheck
            {
                Name = svc,
                Status = detection.State == TweakState.Applied ? "Running" : "Issue",
                Details = detection.CurrentValue,
                Severity = detection.State == TweakState.Applied ? "Ok" : "Warning"
            });
        }

        // Check disk space
        report.DiskChecks.Add(new DiagnosticCheck
        {
            Name = "C:\\ Drive Free Space",
            Details = GetDiskFreeSpace("C:"),
            Severity = "Info"
        });

        _logger.Info($"Diagnostics complete: {report.ServiceChecks.Count(s => s.Severity == "Warning")} warnings", "diagnostic");

        return report;
    }

    private async Task<DetectionResult> DetectServiceStateAsync(string serviceName)
    {
        // Simple service state check without a full tweak definition
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {serviceName}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
                return DetectionResult.Failed(serviceName, "Failed to start sc.exe");

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var isRunning = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            return DetectionResult.Success(serviceName,
                isRunning ? TweakState.Applied : TweakState.NotApplied,
                isRunning ? "Running" : "Not Running");
        }
        catch (Exception ex)
        {
            return DetectionResult.Failed(serviceName, ex.Message);
        }
    }

    private static string GetDiskFreeSpace(string drive)
    {
        try
        {
            var driveInfo = new DriveInfo(drive);
            var freeGb = Math.Round(driveInfo.AvailableFreeSpace / 1073741824.0, 2);
            var totalGb = Math.Round(driveInfo.TotalSize / 1073741824.0, 2);
            var usedPercent = Math.Round((1 - (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize) * 100, 1);
            return $"{freeGb}GB free / {totalGb}GB total ({usedPercent}% used)";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _logger.Dispose();
    }
}

/// <summary>
/// Diagnostic report.
/// </summary>
public sealed class DiagnosticReport
{
    public SystemInfo? SystemInfo { get; set; }
    public List<DiagnosticCheck> ServiceChecks { get; init; } = new();
    public List<DiagnosticCheck> DiskChecks { get; init; } = new();
    public List<DiagnosticCheck> SecurityChecks { get; init; } = new();
}

public sealed class DiagnosticCheck
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Severity { get; init; } = "Info"; // Ok, Info, Warning, Error
}
