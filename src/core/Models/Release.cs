using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>One downloadable file on a published release.</summary>
public sealed record ReleaseAsset
{
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }

    /// <summary>SHA-256 as GitHub publishes it, without the "sha256:" prefix.</summary>
    public string? Sha256 { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Where Novimize itself stands against what has been published.</summary>
public sealed record AppUpdateStatus
{
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
    public List<ReleaseAsset> Assets { get; init; } = new();
    public string Notes { get; init; } = string.Empty;

    public bool UpdateAvailable { get; init; }

    /// <summary>True when the check itself could not be completed.</summary>
    public bool CheckFailed { get; init; }
    public string? Error { get; init; }

    /// <summary>Where a downloaded file landed, once one has been fetched.</summary>
    public string? DownloadedPath { get; init; }
    public long? DownloadedBytes { get; init; }

    /// <summary>True when the bytes matched the published SHA-256.</summary>
    public bool? DigestVerified { get; init; }
    public string? DigestNote { get; init; }
}
