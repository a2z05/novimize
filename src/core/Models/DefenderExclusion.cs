using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>
/// The current Defender exclusion state.
///
/// <c>Get-MpPreference</c> refuses without administrator rights by returning the
/// sentence <c>N/A: Must be an administrator to view exclusions</c> — not an
/// error, not an empty list. Read as a list, that sentence becomes one exclusion
/// path and the UI shows a path that does not exist. <see cref="ElevationRequired"/>
/// is how that is kept from happening.
/// </summary>
public sealed class DefenderExclusionState
{
    [JsonPropertyName("paths")]
    public List<string> Paths { get; init; } = new();

    [JsonPropertyName("processes")]
    public List<string> Processes { get; init; } = new();

    [JsonPropertyName("elevationRequired")]
    public bool ElevationRequired { get; init; }

    /// <summary>True when Defender reported an empty list rather than refusing to say.</summary>
    [JsonPropertyName("readable")]
    public bool Readable { get; init; } = true;

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>Result of one add or remove, with the state it started from.</summary>
public sealed class ExclusionChange
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Whether the operation was a no-op because the path was already in that state.</summary>
    [JsonPropertyName("unchanged")]
    public bool Unchanged { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>Exclusion list before the change, so it can be put back.</summary>
    [JsonPropertyName("before")]
    public List<string> Before { get; init; } = new();

    [JsonPropertyName("after")]
    public List<string> After { get; init; } = new();
}
