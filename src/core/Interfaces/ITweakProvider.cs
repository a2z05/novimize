using WinOpt.Core.Models;

namespace WinOpt.Core.Interfaces;

/// <summary>
/// A tweak provider handles a specific category of tweaks.
/// Each provider knows how to detect, apply, and rollback
/// tweaks in its domain (e.g. Registry, Services, Network).
/// </summary>
public interface ITweakProvider
{
    /// <summary>Provider name for logging and identification.</summary>
    string Name { get; }

    /// <summary>The tweak methods this provider supports.</summary>
    IReadOnlyList<TweakMethod> SupportedMethods { get; }

    /// <summary>
    /// Detect the current state of a tweak.
    /// </summary>
    Task<DetectionResult> DetectAsync(TweakDefinition tweak);

    /// <summary>
    /// Apply a tweak to the system.
    /// </summary>
    Task<ApplyResult> ApplyAsync(TweakDefinition tweak, SnapshotEntry? previousState = null);

    /// <summary>
    /// Rollback a tweak using a snapshot entry.
    /// </summary>
    Task<RollbackResult> RollbackAsync(SnapshotEntry entry);

    /// <summary>
    /// Verify that a tweak was applied correctly after application.
    /// </summary>
    Task<bool> VerifyAsync(TweakDefinition tweak);
}

/// <summary>
/// Registry of all available providers, keyed by name.
/// </summary>
public interface IProviderRegistry
{
    void Register(ITweakProvider provider);
    ITweakProvider? GetProvider(TweakMethod method);
    ITweakProvider? GetProvider(string name);
    IReadOnlyList<ITweakProvider> GetAll();
}
