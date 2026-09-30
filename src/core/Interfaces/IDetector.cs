using WinOpt.Core.Models;

namespace WinOpt.Core.Interfaces;

/// <summary>
/// Detects the current state of a tweak on the system.
/// </summary>
public interface IDetector
{
    /// <summary>
    /// The method this detector handles.
    /// </summary>
    TweakMethod SupportedMethod { get; }

    /// <summary>
    /// Detect the current state of a tweak.
    /// </summary>
    Task< DetectionResult> DetectAsync(TweakDefinition tweak);

    /// <summary>
    /// Detect the current state of multiple tweaks in batch.
    /// </summary>
    Task<List<DetectionResult>> DetectBatchAsync(IEnumerable<TweakDefinition> tweaks);
}

/// <summary>
/// Result of detecting a tweak's current state.
/// </summary>
public sealed class DetectionResult
{
    public string TweakId { get; init; } = string.Empty;
    public TweakState State { get; init; }
    public string? CurrentValue { get; init; }
    public string? Message { get; init; }
    public bool DetectionSucceeded { get; init; } = true;

    public static DetectionResult Success(string tweakId, TweakState state, string? currentValue = null)
        => new() { TweakId = tweakId, State = state, CurrentValue = currentValue, DetectionSucceeded = true };

    public static DetectionResult Failed(string tweakId, string message)
        => new() { TweakId = tweakId, State = TweakState.DetectionFailed, Message = message, DetectionSucceeded = false };

    public static DetectionResult Incompatible(string tweakId, string message)
        => new() { TweakId = tweakId, State = TweakState.Incompatible, Message = message };
}

/// <summary>
/// Applies a tweak to the system.
/// </summary>
public interface IApplier
{
    TweakMethod SupportedMethod { get; }
    Task<ApplyResult> ApplyAsync(TweakDefinition tweak, SnapshotEntry? previousState = null);
}

/// <summary>
/// Result of applying a tweak.
/// </summary>
public sealed class ApplyResult
{
    public string TweakId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Message { get; init; }
    public SnapshotEntry? SnapshotEntry { get; init; }
    public bool RequiresElevation { get; init; }

    public static ApplyResult Ok(string tweakId, SnapshotEntry entry)
        => new() { TweakId = tweakId, Success = true, SnapshotEntry = entry };

    public static ApplyResult Error(string tweakId, string message)
        => new() { TweakId = tweakId, Success = false, Message = message };

    public static ApplyResult NeedElevation(string tweakId)
        => new() { TweakId = tweakId, Success = false, RequiresElevation = true, Message = "Elevation required" };
}

/// <summary>
/// Rolls back a tweak to its previous state.
/// </summary>
public interface IRollbacker
{
    TweakMethod SupportedMethod { get; }
    Task<RollbackResult> RollbackAsync(SnapshotEntry entry);
}

public sealed class RollbackResult
{
    public string TweakId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Message { get; init; }
}
