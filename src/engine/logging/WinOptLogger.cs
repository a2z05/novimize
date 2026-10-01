using System.Collections.Concurrent;
using System.Text.Json;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Logging;

/// <summary>
/// Log levels for WinOpt.
/// </summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    Critical = 4
}

/// <summary>
/// A structured log entry.
/// </summary>
public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public LogLevel Level { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? TweakId { get; init; }
    public string? Details { get; init; }
    public string? ErrorDetails { get; init; }
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// An immutable record of one system modification, appended to the change
/// journal as it happens.
///
/// The journal is the honest answer to "what did this machine actually change,
/// and when". Snapshots record what *would* need undoing; the journal records
/// what did, including failures and run-twice outcomes — and it outlives
/// snapshot pruning, so it stays answerable after the snapshot is gone.
/// </summary>
public sealed class AuditEntry
{
    public string AuditId { get; init; } = Guid.NewGuid().ToString("D");
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>apply | rollback</summary>
    public string Operation { get; init; } = string.Empty;

    public string TweakId { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public TweakMethod Method { get; init; }
    public string? Command { get; init; }

    /// <summary>
    /// success | failure | skipped | blocked — "skipped" covers
    /// already-applied and measurement-only tweaks, which changed nothing but
    /// are worth recording so a re-run is visibly a no-op.
    /// </summary>
    public string Result { get; init; } = string.Empty;

    public string? ErrorDetails { get; init; }
    public bool ElevationUsed { get; init; }
    public string? SessionId { get; init; }
    public string? SnapshotId { get; init; }
}

/// <summary>
/// Structured logger for WinOpt operations.
/// Writes to daily log files and maintains an append-only audit trail.
/// </summary>
public sealed class WinOptLogger
{
    private readonly string _logDir;
    private readonly string _auditDir;
    private readonly ConcurrentQueue<LogEntry> _logBuffer = new();
    private readonly ConcurrentQueue<AuditEntry> _auditBuffer = new();
    private readonly Timer _flushTimer;
    private readonly object _writeLock = new();

    public LogLevel MinLevel { get; set; } = LogLevel.Info;

    public WinOptLogger(string? logDir = null)
    {
        _logDir = logDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "logs");
        _auditDir = Path.Combine(_logDir, "audit");
        Directory.CreateDirectory(_logDir);
        Directory.CreateDirectory(_auditDir);

        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    // --- Logging methods ---

    public void Debug(string message, string? category = null, string? tweakId = null)
        => WriteLog(LogLevel.Debug, message, category, tweakId);

    public void Info(string message, string? category = null, string? tweakId = null)
        => WriteLog(LogLevel.Info, message, category, tweakId);

    public void Warn(string message, string? category = null, string? tweakId = null)
        => WriteLog(LogLevel.Warn, message, category, tweakId);

    public void Error(string message, string? details = null, string? tweakId = null)
        => WriteLog(LogLevel.Error, message, "error", tweakId, details);

    public void Critical(string message, string? details = null, string? tweakId = null)
        => WriteLog(LogLevel.Critical, message, "critical", tweakId, details);

    // --- Change journal ---

    /// <summary>
    /// Record an apply attempt. Every outcome is journaled — success, failure,
    /// blocked, already-applied — because a question like "did this ever run?"
    /// has no useful answer if only the successes were written down.
    /// </summary>
    public void AuditApply(TweakDefinition tweak, string target, string? oldValue, string? newValue,
        string? command, string result, string? error = null,
        string? sessionId = null, string? snapshotId = null, bool elevationUsed = false)
    {
        Append(new AuditEntry
        {
            Operation = "apply",
            TweakId = tweak.Id,
            Target = target,
            OldValue = oldValue,
            NewValue = newValue,
            Method = tweak.Method,
            Command = command,
            Result = result,
            ErrorDetails = error,
            SessionId = sessionId,
            SnapshotId = snapshotId,
            ElevationUsed = elevationUsed,
        });
    }

    public void AuditRollback(string tweakId, TweakMethod method, string target,
        string? oldValue, string? newValue, string result, string? error = null,
        string? sessionId = null, string? snapshotId = null)
    {
        Append(new AuditEntry
        {
            Operation = "rollback",
            TweakId = tweakId,
            Target = target,
            OldValue = oldValue,
            NewValue = newValue,
            Method = method,
            Result = result,
            ErrorDetails = error,
            SessionId = sessionId,
            SnapshotId = snapshotId,
        });
    }

    private void Append(AuditEntry entry)
    {
        _auditBuffer.Enqueue(entry);
        WriteAuditFile(entry);
    }

    /// <summary>
    /// Directory holding the append-only journal, one JSONL file per month.
    /// </summary>
    public string JournalDirectory => _auditDir;

    /// <summary>
    /// Read recent journal entries, newest first.
    /// </summary>
    public IReadOnlyList<AuditEntry> ReadJournal(int limit = 200, string? tweakId = null,
        string? operation = null, string? result = null)
    {
        if (!Directory.Exists(_auditDir)) return Array.Empty<AuditEntry>();

        var entries = new List<AuditEntry>();
        var files = Directory.GetFiles(_auditDir, "audit-*.jsonl")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (entries.Count >= limit * 4) break;

            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<AuditEntry>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (entry == null) continue;
                    if (tweakId != null && !string.Equals(entry.TweakId, tweakId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (operation != null && !string.Equals(entry.Operation, operation, StringComparison.OrdinalIgnoreCase)) continue;
                    if (result != null && !string.Equals(entry.Result, result, StringComparison.OrdinalIgnoreCase)) continue;
                    entries.Add(entry);
                }
                catch
                {
                    // A torn line from a crash mid-write must not make the rest
                    // of the journal unreadable.
                }
            }
        }

        return entries
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Journal entries as JSON, for the CLI and the UI to render. Written with
    /// the same casing the CLI uses everywhere else so consumers see one shape.
    /// </summary>
    public string ReadJournalJson(int limit = 200, string? tweakId = null,
        string? operation = null, string? result = null)
        => JsonSerializer.Serialize(
            ReadJournal(limit, tweakId, operation, result),
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    // --- Log file management ---

    private void WriteLog(LogLevel level, string message, string? category,
        string? tweakId, string? details = null)
    {
        if (level < MinLevel) return;

        var entry = new LogEntry
        {
            Level = level,
            Category = category ?? "general",
            Message = message,
            TweakId = tweakId,
            Details = details
        };
        _logBuffer.Enqueue(entry);

        // Also write to console for CLI mode
        var color = level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warn => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.DarkRed,
            _ => ConsoleColor.White
        };
        var oldColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Error.WriteLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {level,-8} {message}");
        Console.ForegroundColor = oldColor;
    }

    private void WriteAuditFile(AuditEntry entry)
    {
        var fileName = $"audit-{entry.Timestamp:yyyy-MM}.jsonl";
        var filePath = Path.Combine(_auditDir, fileName);
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });

        lock (_writeLock)
        {
            File.AppendAllText(filePath, json + Environment.NewLine);
        }
    }

    /// <summary>
    /// Rotate journal files older than the retention window. The journal is
    /// append-only and never rewritten, so this only ever deletes whole files.
    /// </summary>
    public int CleanupJournal(int retentionDays = 180)
    {
        if (!Directory.Exists(_auditDir)) return 0;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = 0;
        foreach (var file in Directory.GetFiles(_auditDir, "audit-*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
            File.Delete(file);
            deleted++;
        }
        return deleted;
    }

    private void Flush()
    {
        if (_logBuffer.IsEmpty) return;

        var fileName = $"winopt-{DateTime.UtcNow:yyyy-MM-dd}.log";
        var filePath = Path.Combine(_logDir, fileName);

        lock (_writeLock)
        {
            while (_logBuffer.TryDequeue(out var entry))
            {
                var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });
                File.AppendAllText(filePath, json + Environment.NewLine);
            }
        }
    }

    /// <summary>
    /// Force flush all buffered entries to disk.
    /// </summary>
    public void FlushNow()
    {
        Flush();
    }

    /// <summary>
    /// Clean up old log files (older than retentionDays).
    /// </summary>
    public void Cleanup(int retentionDays = 90)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var logFiles = Directory.GetFiles(_logDir, "winopt-*.log");
        foreach (var file in logFiles)
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
                File.Delete(file);
        }
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        Flush();
    }
}
