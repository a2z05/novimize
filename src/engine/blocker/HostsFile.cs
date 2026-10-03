using System.Net;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Blocker;

/// <summary>A run of managed rules written under one list header.</summary>
public sealed record HostsGroup(
    string Source,
    BlockCategory Category,
    string? Url,
    DateTimeOffset AppliedAt,
    DateTimeOffset UpdatedAt,
    string? Purpose,
    IReadOnlyList<BlockRule> Rules);

/// <summary>Everything outside the managed markers, and everything inside them.</summary>
public sealed record HostsDocument(
    string Prefix,
    string? Inner,
    string Suffix,
    string NewLine,
    bool Malformed);

/// <summary>
/// Reads and rewrites the Novimize section of the Windows hosts file.
///
/// The contract is narrow on purpose: bytes outside the two markers are copied
/// through untouched, and the write is refused rather than guessed at whenever
/// the markers do not pair up. A blocklist tool that "helpfully" repairs a file
/// it only half understands is how somebody loses the entries they added by
/// hand, and there is no undo for that — which is why the original is copied
/// aside before the very first write and why unmerge and restore are separate
/// operations with different consequences.
///
/// The section is self-describing. Each list carries one header comment and each
/// ad-hoc rule that differs from its list carries one too, so if Novimize is
/// uninstalled the file still says what the lines are — and Windows still reads
/// every one of them.
/// </summary>
public sealed class HostsFile
{
    public const string StartMarker = "# >>> novimize:start";
    public const string EndMarker = "# <<< novimize:end";
    public const string ListPrefix = "# novimize-list:";
    public const string RulePrefix = "# novimize-rule:";
    public const string DisabledPrefix = "# novimize-off ";

    /// <summary>
    /// The address written for a blocked name. Windows does not open a socket on
    /// 0.0.0.0, so nothing is left waiting for a connection that never comes —
    /// the failure is immediate instead of a timeout.
    /// </summary>
    public const string Address = "0.0.0.0";

    /// <summary>The path property shadows System.IO.Path inside this class.</summary>
    public string HostsPath { get; }

    public string BackupPath { get; }

    public HostsFile(string? hostsPath = null, string? backupPath = null)
    {
        HostsPath = hostsPath ?? DefaultHostsPath();
        BackupPath = backupPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "blocker", "hosts.bak");
    }

    public static string DefaultHostsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32", "drivers", "etc", "hosts");

    public bool Exists => File.Exists(HostsPath);

    public bool BackupExists => File.Exists(BackupPath);

    // --- reading -----------------------------------------------------------

    /// <summary>Split the file on the markers, preserving both halves byte for byte.</summary>
    public HostsDocument Read()
        => Exists ? Split(File.ReadAllText(HostsPath)) : Empty;

    private static readonly HostsDocument Empty =
        new(string.Empty, null, string.Empty, Environment.NewLine, false);

    /// <summary>
    /// Split raw hosts text. Pure, so the pairing rules can be tested without a
    /// hosts file anywhere on the machine.
    /// </summary>
    public static HostsDocument Split(string text)
    {
        var newLine = text.Contains("\r\n") ? "\r\n" : "\n";
        var starts = FindMarkerLines(text, StartMarker);
        var ends = FindMarkerLines(text, EndMarker);

        // No markers at all is the normal first-run case: the section gets
        // appended and everything already there is left where it is.
        if (starts.Count == 0 && ends.Count == 0)
            return new HostsDocument(text, null, string.Empty, newLine, false);

        // More than one of either, one without the other, or the pair the wrong
        // way round: the file has been edited in a way this cannot resolve.
        // Refusing to write is the only safe answer, because every alternative
        // involves deleting something that might be the user's own block.
        if (starts.Count != 1 || ends.Count != 1 || ends[0].Start < starts[0].Start)
            return new HostsDocument(text, null, string.Empty, newLine, true);

        var start = starts[0];
        var end = ends[0];
        return new HostsDocument(
            text[..start.Start],
            text[start.End..end.Start],
            text[end.End..],
            newLine,
            false);
    }

    /// <summary>The span of a marker line: its first character to just past its terminator.</summary>
    private readonly record struct Marker(int Start, int End);

    /// <summary>Every line that is exactly the marker, so a comment that merely
    /// quotes it does not count as one.</summary>
    private static List<Marker> FindMarkerLines(string text, string marker)
    {
        var found = new List<Marker>();
        var position = 0;
        while (position < text.Length)
        {
            var lineEnd = text.IndexOfAny(new[] { '\r', '\n' }, position);
            if (lineEnd < 0) lineEnd = text.Length;
            var line = text[position..lineEnd].Trim();
            var after = lineEnd >= text.Length
                ? text.Length
                : text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n'
                    ? lineEnd + 2
                    : lineEnd + 1;
            if (line == marker) found.Add(new Marker(position, after));
            if (lineEnd >= text.Length) break;
            position = after;
        }
        return found;
    }

    /// <summary>Managed rules, in file order, with their headers attached.</summary>
    public List<BlockRule> ReadManagedRules() => ParseInner(Read().Inner);

    /// <summary>
    /// Parse a managed section. Pure: takes the text between the markers rather
    /// than the file, so the format can be tested on a string.
    /// </summary>
    public static List<BlockRule> ParseInner(string? inner)
    {
        var rules = new List<BlockRule>();
        if (string.IsNullOrEmpty(inner)) return rules;

        string listSource = "custom", listUrl = null, listPurpose = null;
        var listCategory = BlockCategory.Custom;
        DateTimeOffset? listAdded = null, listUpdated = null;

        string ruleSource = null, rulePurpose = null;
        var ruleCategory = BlockCategory.Custom;
        var ruleSeverity = BlockSeverity.Low;
        DateTimeOffset? ruleAdded = null;
        var ruleHeaderPending = false;

        foreach (var raw in inner.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith(ListPrefix, StringComparison.Ordinal))
            {
                // category | source | addedAt | updatedAt | url | purpose
                var parts = SplitFields(line[ListPrefix.Length..]);
                if (parts.Length >= 6)
                {
                    listCategory = ParseCategory(parts[0]);
                    listSource = Nullish(parts[1]) ?? "custom";
                    listAdded = ParseDate(parts[2]);
                    listUpdated = ParseDate(parts[3]);
                    listUrl = Nullish(parts[4]);
                    listPurpose = Nullish(parts[5]);
                }
                ruleHeaderPending = false;
                continue;
            }

            if (line.StartsWith(RulePrefix, StringComparison.Ordinal))
            {
                // category | source | severity | addedAt | purpose
                var parts = SplitFields(line[RulePrefix.Length..]);
                if (parts.Length >= 5)
                {
                    ruleCategory = ParseCategory(parts[0]);
                    ruleSource = Nullish(parts[1]) ?? "custom";
                    ruleSeverity = ParseSeverity(parts[2]);
                    ruleAdded = ParseDate(parts[3]);
                    rulePurpose = Nullish(parts[4]);
                }
                ruleHeaderPending = true;
                continue;
            }

            var disabled = false;
            var payload = line;
            if (line.StartsWith(DisabledPrefix, StringComparison.Ordinal))
            {
                disabled = true;
                payload = line[DisabledPrefix.Length..].Trim();
            }
            else if (line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            var parsed = ParseAddressLine(payload);
            if (parsed is null) continue;

            // A rule with no header above it still blocks. It is reported under
            // the last list header rather than dropped, because dropping it
            // would claim a block is absent when Windows is enforcing it.
            var perRule = ruleHeaderPending;
            rules.Add(new BlockRule
            {
                Id = parsed.Value.Host,
                Kind = BlockKind.Hosts,
                Value = parsed.Value.Address,
                Category = perRule ? ruleCategory : listCategory,
                Purpose = perRule ? rulePurpose : listPurpose,
                Source = perRule ? ruleSource : listSource,
                SourceUrl = perRule ? null : listUrl,
                Severity = perRule ? ruleSeverity : BlockSeverity.Low,
                Enabled = !disabled,
                Managed = true,
                AddedAt = perRule ? ruleAdded : listAdded,
                UpdatedAt = perRule ? listUpdated ?? listAdded : listUpdated,
            });
            ruleHeaderPending = false;
        }

        return rules;
    }

    private static (string Address, string Host)? ParseAddressLine(string line)
    {
        if (line.Length == 0 || line[0] is '#' or ';') return null;
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !IsAddress(parts[0])) return null;

        var host = parts[1];
        // A trailing comment is not part of the name; taking it as one would
        // block a domain that does not exist instead of the one that does.
        var comment = host.IndexOf('#');
        if (comment >= 0) host = host[..comment];
        return host.Length == 0 ? null : (parts[0], host);
    }

    private static bool IsAddress(string token) =>
        token is "0.0.0.0" or "127.0.0.1" or "::" or "::1" || IPAddress.TryParse(token, out _);

    /// <summary>Domain lines the file has that Novimize did not write.</summary>
    public static int CountUnmanaged(string prefix, string suffix)
    {
        var count = 0;
        foreach (var raw in (prefix + "\n" + suffix).Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (ParseAddressLine(line) is not null) count++;
        }
        return count;
    }

    // --- writing -----------------------------------------------------------

    /// <summary>
    /// Copy the file aside, once. A later write never overwrites it, so
    /// "restore" keeps meaning the file as Novimize first found it no matter
    /// how many times the list has been updated since.
    /// </summary>
    public string? TakeBackup()
    {
        if (!Exists) return null;
        if (BackupExists) return BackupPath;
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        File.Copy(HostsPath, BackupPath);
        return BackupPath;
    }

    /// <summary>Replace the managed section with these groups.</summary>
    public BlockChange Write(IEnumerable<HostsGroup> groups)
    {
        var document = Read();
        if (document.Malformed)
            return new BlockChange
            {
                Action = "write",
                Success = false,
                Message = "The Novimize markers in the hosts file are unbalanced, so nothing was written. Fix or remove them by hand first.",
            };

        var body = Build(groups.Select(g => g with { Rules = Inherit(g) }), document.NewLine);
        var prefix = document.Prefix;
        var suffix = document.Suffix;

        if (document.Inner is null)
        {
            // Appending: the existing text must end with a line break of its own
            // or the first managed line joins whatever was on the last one.
            if (prefix.Length > 0 && !prefix.EndsWith('\n')) prefix += document.NewLine;
        }
        else if (suffix.Length == 0)
        {
            // The markers were the end of the file; give the last line a break.
            suffix = document.NewLine;
        }

        var text = prefix + StartMarker + document.NewLine + body + EndMarker + suffix;
        var backup = TakeBackup();
        return Commit(text, "write", backup, CountManaged(body));
    }

    /// <summary>Strip the managed section and leave the rest exactly as found.</summary>
    public BlockChange RemoveManaged()
    {
        var document = Read();
        if (document.Malformed)
            return new BlockChange
            {
                Action = "unmerge",
                Success = false,
                Message = "The Novimize markers in the hosts file are unbalanced, so nothing was written.",
            };
        if (document.Inner is null)
            return new BlockChange { Action = "unmerge", Success = true, Unchanged = true, Message = "There was no Novimize section to remove." };

        return Commit(document.Prefix + document.Suffix, "unmerge", null, 0,
            "The Novimize section was removed. Rules you added outside it were left alone.");
    }

    /// <summary>Put back the file as it stood before the first write.</summary>
    public BlockChange RestoreBackup()
    {
        if (!BackupExists)
            return new BlockChange
            {
                Action = "restore",
                Success = false,
                Message = "No backup exists yet, so there is nothing to restore. A backup is taken on the first write.",
            };

        return Commit(File.ReadAllText(BackupPath), "restore", null, 0,
            "The hosts file was put back to the copy taken before Novimize first wrote to it. Anything added since that copy is gone.");
    }

    private BlockChange Commit(string text, string action, string? backup, int affected, string? message = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(HostsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Write beside the target and move it into place, so a failure
            // halfway leaves the original file rather than a truncated one.
            var temp = HostsPath + ".novimize-tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, HostsPath, overwrite: true);

            return new BlockChange
            {
                Action = action,
                Success = true,
                Affected = affected,
                Backup = backup,
                Message = message ?? $"The hosts file now carries {affected} managed rule{(affected == 1 ? "" : "s")}.",
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new BlockChange
            {
                Action = action,
                Success = false,
                NeedsElevation = true,
                Message = "Writing the hosts file needs administrator rights.",
            };
        }
        catch (Exception ex)
        {
            return new BlockChange { Action = action, Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Give every rule in a group the group's own values where the rule
    /// declares none of its own.
    ///
    /// A row that carries no source and no purpose was never attributed to
    /// anything: it is one of this list's rows, not an override of it. Writing
    /// a per-rule header for it would triple the section's size and, worse,
    /// make the file claim an override that nobody asked for.
    /// </summary>
    private static IReadOnlyList<BlockRule> Inherit(HostsGroup group) =>
        group.Rules.Select(r =>
        {
            var unattributed = r.Source is null && r.Purpose is null;
            return r with
            {
                Source = r.Source ?? group.Source,
                Purpose = r.Purpose ?? group.Purpose,
                Category = unattributed ? group.Category : r.Category,
            };
        }).ToList();

    private static string Build(IEnumerable<HostsGroup> groups, string newLine)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Written by Novimize. Everything outside these two markers is left as it was.")
          .Append(newLine);

        foreach (var group in groups)
        {
            sb.Append(newLine);
            sb.Append(ListPrefix).Append(' ')
              .Append(group.Category.ToString().ToLowerInvariant()).Append(" | ")
              .Append(group.Source).Append(" | ")
              .Append(group.AppliedAt.ToString("o")).Append(" | ")
              .Append(group.UpdatedAt.ToString("o")).Append(" | ")
              .Append(group.Url ?? "-").Append(" | ")
              .Append(string.IsNullOrWhiteSpace(group.Purpose) ? "-" : group.Purpose)
              .Append(newLine);

            foreach (var rule in group.Rules)
            {
                // Per-rule headers only where the rule does not simply inherit
                // the group's — a 40,000-domain list must not become 40,000
                // comment lines, and a header claiming an override that the
                // rule never asked for is a lie told about the file.
                if (rule.Category != group.Category
                    || rule.Severity != BlockSeverity.Low
                    || rule.Source != group.Source
                    || !string.Equals(rule.Purpose, group.Purpose, StringComparison.Ordinal))
                {
                    sb.Append(RulePrefix).Append(' ')
                      .Append(rule.Category.ToString().ToLowerInvariant()).Append(" | ")
                      .Append(rule.Source ?? group.Source).Append(" | ")
                      .Append(rule.Severity.ToString().ToLowerInvariant()).Append(" | ")
                      .Append((rule.AddedAt ?? group.AppliedAt).ToString("o")).Append(" | ")
                      .Append(string.IsNullOrWhiteSpace(rule.Purpose) ? "-" : rule.Purpose)
                      .Append(newLine);
                }

                var line = $"{Address} {rule.Id}";
                sb.Append(rule.Enabled ? line : DisabledPrefix + line).Append(newLine);
            }
        }

        return sb.ToString();
    }

    private static int CountManaged(string body) =>
        body.Split('\n').Count(l =>
            l.TrimEnd('\r').StartsWith(Address) || l.TrimEnd('\r').StartsWith(DisabledPrefix));

    // --- parsing helpers ---------------------------------------------------

    private static string[] SplitFields(string value) =>
        value.Split('|').Select(p => p.Trim()).ToArray();

    private static string? Nullish(string value) =>
        value is "-" or "" or "null" ? null : value;

    private static BlockCategory ParseCategory(string value) => value.ToLowerInvariant() switch
    {
        "ads" => BlockCategory.Ads,
        "trackers" => BlockCategory.Trackers,
        "telemetry" => BlockCategory.Telemetry,
        "malware" => BlockCategory.Malware,
        "analytics" => BlockCategory.Analytics,
        "software" => BlockCategory.Software,
        _ => BlockCategory.Custom,
    };

    private static BlockSeverity ParseSeverity(string value) => value.ToLowerInvariant() switch
    {
        "medium" => BlockSeverity.Medium,
        "high" => BlockSeverity.High,
        _ => BlockSeverity.Low,
    };

    private static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var moment) ? moment : null;

    /// <summary>
    /// Group rules by the list they came from, ready to be written back.
    ///
    /// The group's dates are derived from its members: added is the earliest
    /// and updated the latest, so a list that has been refreshed several times
    /// still shows when it first landed. Only if no member carries a date does
    /// the group fall back to now.
    /// </summary>
    public static List<HostsGroup> ToGroups(IEnumerable<BlockRule> rules, DateTimeOffset now) =>
        rules.GroupBy(r => r.Source ?? "custom")
            .Select(g =>
            {
                var members = g.ToList();
                var first = members[0];
                var added = members.Select(r => r.AddedAt).Min() ?? now;
                var updated = members.Select(r => r.UpdatedAt ?? r.AddedAt).Max() ?? now;
                return new HostsGroup(
                    g.Key,
                    first.Category,
                    first.SourceUrl,
                    added,
                    updated,
                    first.Purpose,
                    members);
            })
            .ToList();
}
