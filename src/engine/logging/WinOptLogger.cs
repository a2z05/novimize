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
/// Audit trail entry — immutable record of system modifications.
/// </summary>
public sealed class AuditEntry
{
    public string AuditId { get; init; } = Guid.NewGuid().ToString("D");
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Operation { get; init; } = string.Empty; // apply, rollback, detect
    public string TweakId { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public TweakMethod Method { get; init; }
    public string? Command { get; init; }
    public string Result { get; init; } = string.Empty; // success, failure
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

    // --- Audit trail ---

    public void AuditApply(TweakDefinition tweak, string target, string? oldValue, string? newValue,
        string? command, bool success, string? sessionId = null, string? snapshotId = null)
    {
        var entry = new AuditEntry
        {
            Operation = "apply",
            TweakId = tweak.Id,
            Target = target,
            OldValue = oldValue,
            NewValue = newValue,
            Method = tweak.Method,
            Command = command,
            Result = success ? "success" : "failure",
            SessionId = sessionId,
            SnapshotId = snapshotId
        };
        _auditBuffer.Enqueue(entry);
        WriteAuditFile(entry);
    }

    public void AuditRollback(string tweakId, TweakMethod method, string target,
        string? oldValue, bool success, string? sessionId = null)
    {
        var entry = new AuditEntry
        {
            Operation = "rollback",
            TweakId = tweakId,
            Target = target,
            OldValue = oldValue,
            Method = method,
            Result = success ? "success" : "failure",
            SessionId = sessionId
        };
        _auditBuffer.Enqueue(entry);
        WriteAuditFile(entry);
    }

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
