using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinOpt.Engine.Diagnostics;

/// <summary>
/// The three exports the brief asks for.
///
/// JSON is the report itself, so a script can read it. The text file is what
/// gets pasted into a forum post or an email. The HTML is self-contained —
/// no external stylesheet, no script — so the file still says what it said
/// the day it was written, on a machine with no network.
/// </summary>
public static class HealthReportWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<string> Formats { get; } = new[] { "json", "txt", "html" };

    public static string Render(HealthReport report, string format) =>
        format.ToLowerInvariant() switch
        {
            "json" => ToJson(report),
            "htm" or "html" => ToHtml(report),
            _ => ToText(report),
        };

    public static string ExtensionFor(string format) =>
        format.ToLowerInvariant() switch
        {
            "json" => ".json",
            "htm" or "html" => ".html",
            _ => ".txt",
        };

    public static string ToJson(HealthReport report) => JsonSerializer.Serialize(report, Json);

    public static string ToText(HealthReport report)
    {
        var info = report.SystemInfo;
        var sb = new StringBuilder();
        sb.AppendLine("Novimize health report");
        sb.AppendLine($"Generated {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', 72));

        if (info is not null)
        {
            sb.AppendLine($"Machine    {info.OsCaption} {info.OsVersion} (build {info.BuildNumber}) {info.OsArchitecture}");
            sb.AppendLine($"CPU        {info.CpuName} ({info.CpuCores}C/{info.CpuLogicalProcessors}T)");
            sb.AppendLine($"Memory     {info.RamTotalGb} GB");
            sb.AppendLine($"Storage    {info.PrimaryStorageModel} ({info.PrimaryStorageType}, {info.PrimaryStorageSizeGb} GB)");
            sb.AppendLine($"GPU        {info.GpuName}");
            sb.AppendLine($"Form       {info.FormFactor}");
            sb.AppendLine();
        }

        sb.AppendLine($"Overall    {report.OverallStatus}");
        sb.AppendLine();
        sb.AppendLine("Checks");
        sb.AppendLine(new string('-', 72));
        foreach (var check in report.Checks)
        {
            var mark = check.Status switch
            {
                HealthStatus.Ok => "  ok  ",
                HealthStatus.Warning => " warn ",
                _ => " CRIT ",
            };
            sb.AppendLine($"[{mark}] {check.Name}");
            sb.AppendLine($"         {check.Details}");
        }

        if (report.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Warnings");
            sb.AppendLine(new string('-', 72));
            foreach (var warning in report.Warnings) sb.AppendLine($"  · {warning}");
        }

        sb.AppendLine();
        sb.AppendLine("Novimize changes nothing by producing this file.");
        return sb.ToString();
    }

    public static string ToHtml(HealthReport report)
    {
        var info = report.SystemInfo;
        var tone = report.OverallStatus switch
        {
            HealthStatus.Ok => "#16a34a",
            HealthStatus.Warning => "#d97706",
            _ => "#dc2626",
        };

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine("<title>Novimize health report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font:14px/1.5 system-ui,sans-serif;margin:2rem auto;max-width:52rem;color:#111;padding:0 1rem}");
        sb.AppendLine("h1{font-size:1.3rem;margin:0 0 .25rem}h2{font-size:1rem;margin:1.5rem 0 .5rem}");
        sb.AppendLine("table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:.4rem .5rem;border-bottom:1px solid #e5e7eb;vertical-align:top}");
        sb.AppendLine("th{font-size:.72rem;text-transform:uppercase;letter-spacing:.05em;color:#6b7280}");
        sb.AppendLine(".ok{color:#16a34a;font-weight:600}.warn{color:#d97706;font-weight:600}.crit{color:#dc2626;font-weight:600}");
        sb.AppendLine(".meta{color:#6b7280;font-size:.85rem}code{font-family:ui-monospace,monospace;font-size:.85rem}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>Novimize health report <span style=\"color:{tone}\">{WebUtility.HtmlEncode(report.OverallStatus.ToString())}</span></h1>");
        sb.AppendLine($"<p class=\"meta\">Generated {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}</p>");

        if (info is not null)
        {
            sb.AppendLine("<h2>Machine</h2><table>");
            Row(sb, "Operating system", $"{info.OsCaption} {info.OsVersion} (build {info.BuildNumber}) {info.OsArchitecture}");
            Row(sb, "CPU", $"{info.CpuName} ({info.CpuCores} cores / {info.CpuLogicalProcessors} threads)");
            Row(sb, "Memory", $"{info.RamTotalGb} GB");
            Row(sb, "Storage", $"{info.PrimaryStorageModel} — {info.PrimaryStorageType}, {info.PrimaryStorageSizeGb} GB");
            Row(sb, "GPU", info.GpuName);
            Row(sb, "Form factor", info.FormFactor.ToString());
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<h2>Checks</h2><table><tr><th>Check</th><th>Status</th><th>Detail</th></tr>");
        foreach (var check in report.Checks)
        {
            var cls = check.Status switch
            {
                HealthStatus.Ok => "ok",
                HealthStatus.Warning => "warn",
                _ => "crit",
            };
            sb.AppendLine($"<tr><td>{WebUtility.HtmlEncode(check.Name)}</td>" +
                          $"<td class=\"{cls}\">{WebUtility.HtmlEncode(check.Status.ToString())}</td>" +
                          $"<td>{WebUtility.HtmlEncode(check.Details)}</td></tr>");
        }
        sb.AppendLine("</table>");

        if (report.Warnings.Count > 0)
        {
            sb.AppendLine("<h2>Warnings</h2><ul>");
            foreach (var warning in report.Warnings)
                sb.AppendLine($"<li>{WebUtility.HtmlEncode(warning)}</li>");
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("<p class=\"meta\">Novimize changes nothing by producing this file.</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string label, string value) =>
        sb.AppendLine($"<tr><th>{WebUtility.HtmlEncode(label)}</th><td>{WebUtility.HtmlEncode(value)}</td></tr>");

    /// <summary>Write the report, choosing a filename when none was given.</summary>
    public static async Task<string> WriteAsync(
        HealthReport report, string format, string? output, CancellationToken cancel = default)
    {
        var path = output;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinOpt", "health",
                $"health-{report.GeneratedAt:yyyyMMdd-HHmmss}{ExtensionFor(format)}");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, Render(report, format), cancel);
        return path;
    }
}
