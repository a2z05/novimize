using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinOpt.Core.Models;
using WinOpt.Engine.Logging;

namespace WinOpt.Engine.Snapshots;

/// <summary>
/// Manages snapshot creation, storage, loading, and integrity verification.
/// Snapshots are JSON files stored in %LOCALAPPDATA%\WinOpt\snapshots\.
/// </summary>
public sealed class SnapshotManager
{
    private readonly string _snapshotDir;
    private readonly WinOptLogger _logger;

    public SnapshotManager(WinOptLogger logger, string? snapshotDir = null)
    {
        _logger = logger;
        _snapshotDir = snapshotDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "snapshots");
        Directory.CreateDirectory(_snapshotDir);
    }

    /// <summary>
    /// Create a new snapshot before applying tweaks.
    /// </summary>
    public Snapshot Create(string description, SystemInfo? systemInfo, List<SnapshotEntry>? entries = null)
    {
        var snapshot = new Snapshot
        {
            Description = description,
            SystemInfo = systemInfo != null ? new SystemInfoSnapshot
            {
                OsVersion = systemInfo.OsVersion,
                BuildNumber = systemInfo.BuildNumber,
                CpuName = systemInfo.CpuName,
                RamGb = systemInfo.RamTotalGb
            } : null,
            Entries = entries ?? new List<SnapshotEntry>()
        };

        // Compute checksum
        snapshot = snapshot with
        {
            Checksum = ComputeChecksum(snapshot)
        };

        Save(snapshot);

        _logger.Info($"Snapshot created: {snapshot.Id} — {description}", "snapshot");

        return snapshot;
    }

    /// <summary>
    /// Add an entry to an existing snapshot.
    /// </summary>
    public void AddEntry(string snapshotId, SnapshotEntry entry)
    {
        var snapshot = Load(snapshotId);
        if (snapshot == null) return;

        snapshot.Entries.Add(entry);
        snapshot = snapshot with { Checksum = ComputeChecksum(snapshot) };
        Save(snapshot);
    }

    /// <summary>
    /// Save a snapshot to disk.
    /// </summary>
    public void Save(Snapshot snapshot)
    {
        var filePath = GetSnapshotPath(snapshot.Id);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(snapshot, options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Load a snapshot from disk.
    /// </summary>
    public Snapshot? Load(string snapshotId)
    {
        var filePath = GetSnapshotPath(snapshotId);
        if (!File.Exists(filePath)) return null;

        var json = File.ReadAllText(filePath);
        var snapshot = JsonSerializer.Deserialize<Snapshot>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Verify integrity
        if (snapshot != null)
        {
            var expectedChecksum = ComputeChecksum(snapshot);
            if (snapshot.Checksum != expectedChecksum)
            {
                _logger.Warn($"Snapshot {snapshotId} integrity check failed! Expected {expectedChecksum}, got {snapshot.Checksum}", "snapshot");
            }
        }

        return snapshot;
    }

    /// <summary>
    /// List all snapshots, newest first.
    /// </summary>
    public IReadOnlyList<SnapshotInfo> List()
    {
        if (!Directory.Exists(_snapshotDir))
            return Array.Empty<SnapshotInfo>();

        return Directory.GetFiles(_snapshotDir, "*.json")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .Select(f =>
            {
                try
                {
                    var json = File.ReadAllText(f);
                    var snapshot = JsonSerializer.Deserialize<Snapshot>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (snapshot == null) return null;

                    return new SnapshotInfo
                    {
                        Id = snapshot.Id,
                        Timestamp = snapshot.Timestamp,
                        Description = snapshot.Description,
                        EntryCount = snapshot.Entries.Count,
                        TweaksApplied = snapshot.TweaksApplied.Count,
                        FileSize = new FileInfo(f).Length
                    };
                }
                catch { return null; }
            })
            .Where(s => s != null)
            .Cast<SnapshotInfo>()
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Delete a snapshot.
    /// </summary>
    public bool Delete(string snapshotId)
    {
        var filePath = GetSnapshotPath(snapshotId);
        if (!File.Exists(filePath)) return false;

        File.Delete(filePath);
        _logger.Info($"Snapshot deleted: {snapshotId}", "snapshot");
        return true;
    }

    /// <summary>
    /// Clean up old snapshots, keeping the most recent ones.
    /// </summary>
    public int Cleanup(int keepCount = 20)
    {
        var snapshots = List().ToList();
        if (snapshots.Count <= keepCount) return 0;

        var toDelete = snapshots.Skip(keepCount).ToList();
        foreach (var snap in toDelete)
        {
            Delete(snap.Id);
        }
        return toDelete.Count;
    }

    /// <summary>
    /// Compute SHA256 checksum of snapshot content (excluding checksum field).
    /// </summary>
    private static string ComputeChecksum(Snapshot snapshot)
    {
        // Create a copy without the checksum for hashing
        var entriesJson = JsonSerializer.Serialize(snapshot.Entries,
            new JsonSerializerOptions { WriteIndented = false });
        var tweaksJson = JsonSerializer.Serialize(snapshot.TweaksApplied,
            new JsonSerializerOptions { WriteIndented = false });
        var payload = $"{snapshot.Id}|{snapshot.Timestamp:s}|{snapshot.Description}|{entriesJson}|{tweaksJson}";

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GetSnapshotPath(string snapshotId)
    {
        if (string.IsNullOrWhiteSpace(snapshotId) || snapshotId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || snapshotId.Contains('/') || snapshotId.Contains('\\'))
            throw new ArgumentException($"Invalid snapshotId: '{snapshotId}'", nameof(snapshotId));
        return Path.Combine(_snapshotDir, $"{snapshotId}.json");
    }
}

/// <summary>
/// Lightweight snapshot info for listing.
/// </summary>
public sealed class SnapshotInfo
{
    public string Id { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public string Description { get; init; } = string.Empty;
    public int EntryCount { get; init; }
    public int TweaksApplied { get; init; }
    public long FileSize { get; init; }
}
