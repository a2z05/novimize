using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Blocker;

/// <summary>
/// Everything the Blocker page does, in one place.
///
/// The hosts file and the firewall are two different mechanisms with the same
/// job — keeping names and addresses away from the network stack — and they
/// need different promises from the code that writes them. The hosts file is
/// a shared user file, so this class only ever touches the section between the
/// markers. The firewall is a set of machine rules, so it only ever touches
/// rules carrying the Novimize prefix. Neither promise depends on remembering
/// to filter: filtering is what the underlying classes do, and this one never
/// hands them a target outside their own namespace.
/// </summary>
public sealed class BlockerManager
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(90),
    };

    static BlockerManager()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Novimize/1.0 (+blocklist fetch)");
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
    }

    public HostsFile Hosts { get; }
    public FirewallBlocker Firewall { get; }
    public BlocklistCatalog Catalog { get; }

    /// <summary>Where fetched copies live, so an update can diff against them.</summary>
    public string CacheDirectory { get; }

    /// <summary>
    /// The hosts file is taken as a whole rather than as a path, because its
    /// backup lives beside it as a design decision: passing a path and letting
    /// the backup fall back to a process-wide default means two different hosts
    /// files share one backup, and the second one's restore quietly overwrites
    /// the first one's.
    /// </summary>
    public BlockerManager(HostsFile? hosts = null, string? cacheDirectory = null, string? firewallSetDirectory = null)
    {
        Hosts = hosts ?? new HostsFile();
        Firewall = new FirewallBlocker(firewallSetDirectory);
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "blocker");
        CacheDirectory = cacheDirectory ?? Path.Combine(root, "cache");
        Catalog = new BlocklistCatalog(BlocklistCatalog.ResolveDirectory());
        Catalog.Load();
    }

    // --- status ------------------------------------------------------------

    public async Task<BlockerStatus> StatusAsync(CancellationToken cancel = default)
    {
        var document = Hosts.Read();
        var managed = HostsFile.ParseInner(document.Inner);
        var firewall = await Firewall.ReadAsync(cancel);

        var status = new BlockerStatus
        {
            HostsPath = Hosts.HostsPath,
            HostsExists = Hosts.Exists,
            HostsMalformed = document.Malformed,
            Managed = managed.Count,
            Enabled = managed.Count(r => r.Enabled),
            Unmanaged = HostsFile.CountUnmanaged(document.Prefix, document.Suffix),
            BackupExists = Hosts.BackupExists,
            BackupPath = Hosts.BackupExists ? Hosts.BackupPath : null,
            Writable = IsHostsWritable(),
            FirewallRules = firewall.Rules.Count,
            FirewallReadable = firewall.Error is null,
            Sources = Catalog.Sources.ToList(),
            Rules = managed.Select(r => ToRule(r)).ToList(),
        };

        foreach (var entry in firewall.Rules)
            status.Rules.Add(ToRule(entry));

        // A list is "applied" when the section carries rules attributed to it.
        foreach (var group in managed.GroupBy(r => r.Source ?? "custom"))
        {
            var source = Catalog.Find(group.Key);
            status.Applied.Add(new AppliedSource
            {
                Source = group.Key,
                Name = source?.Name,
                Domains = group.Count(),
                AppliedAt = group.Select(r => r.AddedAt).Min() ?? DateTimeOffset.Now,
                UpdatedAt = group.Select(r => r.UpdatedAt ?? r.AddedAt).Max(),
                Url = source?.Url ?? group.First().SourceUrl,
            });
        }

        // Address sets applied to the firewall count as applied too; they do
        // not appear in the hosts section at all, so without this the page
        // would report a list as missing while Windows is enforcing it.
        foreach (var file in SetFiles())
        {
            if (status.Applied.Any(a => a.Source.Equals(Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)))
                continue;
            var source = Catalog.Find(Path.GetFileNameWithoutExtension(file));
            status.Applied.Add(new AppliedSource
            {
                Source = Path.GetFileNameWithoutExtension(file),
                Name = source?.Name,
                Domains = CountLines(file),
                AppliedAt = File.GetCreationTimeUtc(file),
                UpdatedAt = File.GetLastWriteTimeUtc(file),
                Url = source?.Url,
            });
        }

        return status;
    }

    private IEnumerable<string> SetFiles() =>
        Directory.Exists(Firewall.SetDirectory)
            ? Directory.GetFiles(Firewall.SetDirectory, "*.ips")
            : Array.Empty<string>();

    private static int CountLines(string path)
    {
        try { return File.ReadLines(path).Count(l => l.Trim().Length > 0); }
        catch { return 0; }
    }

    /// <summary>
    /// Whether a write would be allowed right now. Probing by opening the file
    /// rather than by assuming from the elevation check: a hosts file on a
    /// test path is writable without admin, and a machine can be elevated and
    /// still have the file marked read-only.
    /// </summary>
    private bool IsHostsWritable()
    {
        try
        {
            if (!Hosts.Exists) return true;
            using var _ = File.Open(Hosts.HostsPath, FileMode.Open, FileAccess.Write, FileShare.Read);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>A hosts rule as the page shows it.</summary>
    private static BlockRule ToRule(BlockRule rule) => rule with { Kind = BlockKind.Hosts, Managed = true };

    private BlockRule ToRule(FirewallEntry entry)
    {
        FirewallBlocker.ParseDescription(entry.Description,
            out var category, out var severity, out var source, out var purpose);

        // A set can be thousands of addresses; the page gets the file rather
        // than the whole list through every status read.
        var setPath = Path.Combine(Firewall.SetDirectory, source + ".ips");
        var value = entry.Program
            ?? (entry.RemoteAddresses.Count > 0
                ? (File.Exists(setPath) ? setPath : $"{entry.RemoteAddresses.Count} addresses")
                : string.Empty);

        return new BlockRule
        {
            Id = entry.Id,
            Kind = BlockKind.Firewall,
            Value = value,
            Category = category,
            Purpose = purpose,
            Source = source,
            Severity = severity,
            Enabled = entry.Enabled,
            Managed = true,
            Application = entry.Program,
        };
    }

    // --- fetching ----------------------------------------------------------

    /// <summary>
    /// Download a list and say what it contains. Nothing is written to the
    /// hosts file or the firewall here — the whole point of the two-step is
    /// that the count and the diff are visible before the change is made.
    /// </summary>
    public async Task<BlockFetchResult> FetchAsync(string sourceId, CancellationToken cancel = default)
    {
        var source = Catalog.Find(sourceId);
        if (source is null)
            return new BlockFetchResult { Source = sourceId, FetchedAt = DateTimeOffset.Now, FirstTime = true };

        var text = await DownloadAsync(source, cancel);
        if (text is null)
            return new BlockFetchResult { Source = source.Id, Url = source.Url, FetchedAt = DateTimeOffset.Now, FirstTime = true };

        var parsed = Parse(source, text);
        var values = parsed.Values;

        Directory.CreateDirectory(CacheDirectory);
        var path = Path.Combine(CacheDirectory, BlocklistCatalog.CacheFileName(source));
        await File.WriteAllTextAsync(path, text, cancel);

        var applied = AppliedValues(source);
        var (added, removed) = BlocklistFormats.Diff(applied, values);

        return new BlockFetchResult
        {
            Source = source.Id,
            Url = source.Url,
            CachedAt = path,
            Bytes = System.Text.Encoding.UTF8.GetByteCount(text),
            Domains = values.Count,
            Sha256 = Sha256(text),
            Added = added,
            Removed = removed,
            FetchedAt = DateTimeOffset.Now,
            FirstTime = applied.Count == 0,
        };
    }

    private async Task<string?> DownloadAsync(BlockSource source, CancellationToken cancel)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return null;

        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode) return null;
            // Bounded: a list that has grown past this is either wrong or is
            // not something 100k hosts lines will thank us for.
            using var stream = await response.Content.ReadAsStreamAsync(cancel);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync(cancel);
            return text.Length <= 32 * 1024 * 1024 ? text : null;
        }
        catch
        {
            return null;
        }
    }

    private static ParsedList Parse(BlockSource source, string text) =>
        source.Format.Equals("ips", StringComparison.OrdinalIgnoreCase)
            ? BlocklistFormats.ParseAddresses(text)
            : BlocklistFormats.ParseDomains(text);

    /// <summary>What this machine already enforces for that source.</summary>
    private IReadOnlyCollection<string> AppliedValues(BlockSource source)
    {
        if (source.Format.Equals("ips", StringComparison.OrdinalIgnoreCase))
        {
            var file = Path.Combine(Firewall.SetDirectory, source.Id + ".ips");
            if (File.Exists(file))
                try { return File.ReadAllLines(file).Where(l => l.Trim().Length > 0).ToList(); }
                catch { /* a missing or unreadable set reads as "nothing applied" */ }
            return Array.Empty<string>();
        }

        return HostsFile.ParseInner(Hosts.Read().Inner)
            .Where(r => string.Equals(r.Source, source.Id, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Id)
            .ToList();
    }

    private static string Sha256(string text)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // --- applying ----------------------------------------------------------

    /// <summary>
    /// Write a fetched list into the machine. Hosts lists land in the managed
    /// section; address lists become firewall rules.
    /// </summary>
    public async Task<BlockChange> ApplyAsync(string sourceId, CancellationToken cancel = default)
    {
        var source = Catalog.Find(sourceId);
        if (source is null)
            return Fail("apply", $"'{sourceId}' is not a list Novimize knows about.");

        var path = Path.Combine(CacheDirectory, BlocklistCatalog.CacheFileName(source));
        if (!File.Exists(path))
            return Fail("apply", $"'{source.Name}' has not been fetched yet, so there is nothing to apply.");

        string text;
        try { text = await File.ReadAllTextAsync(path, cancel); }
        catch (Exception ex) { return Fail("apply", $"The cached copy could not be read: {ex.Message}"); }

        return source.Format.Equals("ips", StringComparison.OrdinalIgnoreCase)
            ? await ApplyAddressesAsync(source, text, cancel)
            : ApplyDomainsAsync(source, text);
    }

    private BlockChange ApplyDomainsAsync(BlockSource source, string text)
    {
        var document = Hosts.Read();
        if (document.Malformed)
            return Fail("apply", "The Novimize markers in the hosts file are unbalanced, so nothing was written.");

        var now = DateTimeOffset.Now;
        var existing = HostsFile.ParseInner(document.Inner);
        var fresh = BlocklistFormats.ParseDomains(text).Values;

        // Keep what the user had: their disabled individual rules stay
        // disabled across an update, and a domain that has been here a while
        // keeps the date it first arrived rather than being re-dated by every
        // refresh.
        var prior = existing
            .Where(r => string.Equals(r.Source, source.Id, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

        var rules = existing
            .Where(r => !string.Equals(r.Source, source.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var domain in fresh)
        {
            prior.TryGetValue(domain, out var old);
            rules.Add(new BlockRule
            {
                Id = domain,
                Kind = BlockKind.Hosts,
                Value = HostsFile.Address,
                Category = source.Category,
                Purpose = source.Purpose,
                Source = source.Id,
                SourceUrl = source.Url,
                Severity = source.Severity,
                Enabled = old?.Enabled ?? true,
                Managed = true,
                AddedAt = old?.AddedAt ?? now,
                UpdatedAt = now,
            });
        }

        var change = Hosts.Write(HostsFile.ToGroups(rules, now));
        if (!change.Success) return change;

        return change with
        {
            Action = "apply",
            Message = $"{source.Name}: {fresh.Count} domain rule{(fresh.Count == 1 ? "" : "s")} written to the hosts section"
                      + (prior.Count > 0 ? $" ({prior.Count} already there were kept as they were)." : "."),
        };
    }

    private async Task<BlockChange> ApplyAddressesAsync(BlockSource source, string text, CancellationToken cancel)
    {
        var addresses = BlocklistFormats.ParseAddresses(text).Values;
        if (addresses.Count == 0)
            return Fail("apply", $"{source.Name} parsed to no addresses, so no firewall rule was written.");

        return await Firewall.BlockAddressesAsync(
            source.Id, addresses, source.Category, source.Id, source.Purpose, source.Severity, cancel);
    }

    // --- single rules ------------------------------------------------------

    public BlockChange AddDomain(
        string domain, BlockCategory category, string? purpose, BlockSeverity severity)
    {
        var normalized = BlocklistFormats.NormalizeDomain(domain);
        if (normalized is null)
            return Fail("add", $"'{domain}' is not a domain that can go in a hosts file. "
                             + "Loopback names, addresses, wildcards and single labels are refused.");

        var document = Hosts.Read();
        if (document.Malformed)
            return Fail("add", "The Novimize markers in the hosts file are unbalanced, so nothing was written.");

        var now = DateTimeOffset.Now;
        var rules = HostsFile.ParseInner(document.Inner);
        if (rules.Any(r => string.Equals(r.Id, normalized, StringComparison.OrdinalIgnoreCase)))
            return new BlockChange
            {
                Action = "add",
                Success = true,
                Unchanged = true,
                Affected = 1,
                Message = $"{normalized} is already in the managed section.",
            };

        rules.Add(new BlockRule
        {
            Id = normalized,
            Kind = BlockKind.Hosts,
            Value = HostsFile.Address,
            Category = category,
            Purpose = purpose,
            Source = "custom",
            Severity = severity,
            Enabled = true,
            Managed = true,
            AddedAt = now,
            UpdatedAt = now,
        });

        var change = Hosts.Write(HostsFile.ToGroups(rules, now));
        return change.Success
            ? change with { Action = "add", Message = $"{normalized} is now blocked." }
            : change;
    }

    public BlockChange RemoveDomain(string domain)
    {
        var normalized = BlocklistFormats.NormalizeDomain(domain) ?? domain.Trim().ToLowerInvariant();
        var document = Hosts.Read();
        if (document.Malformed)
            return Fail("remove", "The Novimize markers in the hosts file are unbalanced, so nothing was written.");

        var now = DateTimeOffset.Now;
        var rules = HostsFile.ParseInner(document.Inner);
        var kept = rules.Where(r => !string.Equals(r.Id, normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        if (kept.Count == rules.Count)
            return new BlockChange { Action = "remove", Success = true, Unchanged = true, Message = $"{normalized} was not in the managed section." };

        var change = Hosts.Write(HostsFile.ToGroups(kept, now));
        return change.Success
            ? change with { Action = "remove", Affected = rules.Count - kept.Count, Message = $"{normalized} was removed." }
            : change;
    }

    public BlockChange SetDomainEnabled(string domain, bool enabled)
    {
        var normalized = BlocklistFormats.NormalizeDomain(domain) ?? domain.Trim().ToLowerInvariant();
        var document = Hosts.Read();
        if (document.Malformed)
            return Fail(enabled ? "enable" : "disable",
                "The Novimize markers in the hosts file are unbalanced, so nothing was written.");

        var now = DateTimeOffset.Now;
        var rules = HostsFile.ParseInner(document.Inner);
        var found = false;
        var updated = rules.Select(r =>
        {
            if (!string.Equals(r.Id, normalized, StringComparison.OrdinalIgnoreCase)) return r;
            found = true;
            return r with { Enabled = enabled };
        }).ToList();

        if (!found)
            return new BlockChange
            {
                Action = enabled ? "enable" : "disable",
                Success = false,
                Message = $"{normalized} is not in the managed section, so there is nothing to {(enabled ? "enable" : "disable")}.",
            };

        var change = Hosts.Write(HostsFile.ToGroups(updated, now));
        return change.Success
            ? change with { Action = enabled ? "enable" : "disable", Affected = 1, Message = $"{normalized} is now {(enabled ? "blocking" : "let through")}." }
            : change;
    }

    /// <summary>
    /// Take one applied list back off, leaving every other list and every
    /// hand-written line alone. This is the difference between "that list
    /// broke something, take it off" and "start again" — unmerge throws away
    /// the custom rules alongside it, and a rollback that costs more than the
    /// mistake is not a rollback.
    /// </summary>
    public async Task<BlockChange> RemoveSourceAsync(string sourceId, CancellationToken cancel = default)
    {
        var id = sourceId.Trim();
        const string action = "remove-source";
        if (id.Length == 0) return Fail(action, "remove --source needs a source id.");

        var document = Hosts.Read();
        if (document.Malformed)
            return Fail(action, "The Novimize markers in the hosts file are unbalanced, so nothing was written.");

        var touched = 0;

        // The firewall goes first: it is the half that can ask for rights, and
        // failing there must happen before anything on disk has changed rather
        // than half way through a write the user cannot undo.
        var setPath = Path.Combine(Firewall.SetDirectory, id + ".ips");
        if (File.Exists(setPath))
        {
            var firewall = await Firewall.RemoveAsync(id, cancel);
            if (!firewall.Success) return firewall with { Action = action };
            touched += firewall.Affected;
        }

        var rules = HostsFile.ParseInner(document.Inner);
        var kept = rules
            .Where(r => !string.Equals(r.Source, id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (kept.Count != rules.Count)
        {
            var change = Hosts.Write(HostsFile.ToGroups(kept, DateTimeOffset.Now));
            if (!change.Success) return change with { Action = action };
            touched += rules.Count - kept.Count;
        }

        if (touched == 0)
            return new BlockChange
            {
                Action = action,
                Success = true,
                Unchanged = true,
                Message = $"'{id}' is not applied to this machine.",
            };

        return new BlockChange
        {
            Action = action,
            Success = true,
            Affected = touched,
            Message = $"'{id}' was taken off: {touched} rule(s) removed. Nothing outside its own rules was touched.",
        };
    }

    /// <summary>Block one program from reaching the network.</summary>
    public Task<BlockChange> AddProgramAsync(
        string id, string program, BlockCategory category, string? purpose,
        BlockSeverity severity = BlockSeverity.Low, CancellationToken cancel = default)
        => Firewall.BlockProgramAsync(id, program, category, "custom", purpose, severity, cancel);
    /// <summary>Remove the firewall rules for one id.</summary>
    public Task<BlockChange> RemoveFirewallAsync(string id, CancellationToken cancel = default)
        => Firewall.RemoveAsync(id, cancel);

    public Task<BlockChange> SetFirewallEnabledAsync(string id, bool enabled, CancellationToken cancel = default)
        => Firewall.SetEnabledAsync(id, enabled, cancel);

    public Task<BlockChange> ClearFirewallAsync(CancellationToken cancel = default)
        => Firewall.RemoveAllAsync(cancel);

    // --- rollback ----------------------------------------------------------

    public BlockChange Unmerge() => Hosts.RemoveManaged();
    public BlockChange Restore() => Hosts.RestoreBackup();

    // --- export / import ---------------------------------------------------

    /// <summary>
    /// One file holding everything Novimize enforces, so the same set can be
    /// put back on another machine or after a reinstall.
    /// </summary>
    public async Task<BlockChange> ExportAsync(string? output, CancellationToken cancel = default)
    {
        var status = await StatusAsync(cancel);
        var document = Hosts.Read();

        var export = new BlockExport
        {
            ExportedAt = DateTimeOffset.Now,
            HostsSection = document.Inner ?? string.Empty,
            UnmanagedCount = HostsFile.CountUnmanaged(document.Prefix, document.Suffix),
            Firewall = status.Rules.Where(r => r.Kind == BlockKind.Firewall).ToList(),
            AddressSets = SetFiles().ToDictionary(
                f => Path.GetFileNameWithoutExtension(f),
                f => File.ReadAllLines(f).Where(l => l.Trim().Length > 0).ToList()),
        };

        var path = output;
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinOpt", "blocker", $"blocker-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }), cancel);
        }
        catch (Exception ex)
        {
            return Fail("export", ex.Message);
        }

        return new BlockChange
        {
            Action = "export",
            Success = true,
            Message = $"Exported {status.Rules.Count} rule(s) to {path}.",
        };
    }

    /// <summary>
    /// Put back an exported file. The hosts section is written whole, because
    /// it is Novimize's own section and the export carries it verbatim; the
    /// firewall rules are recreated one at a time under the usual prefix, so a
    /// partially failed import still leaves the rules it did write, all of them
    /// removable in one call.
    /// </summary>
    public async Task<BlockChange> ImportAsync(string input, CancellationToken cancel = default)
    {
        if (!File.Exists(input))
            return Fail("import", $"No export file at {input}.");

        BlockExport? export;
        try
        {
            export = JsonSerializer.Deserialize<BlockExport>(
                await File.ReadAllTextAsync(input, cancel),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            return Fail("import", $"That file could not be read as a Novimize export: {ex.Message}");
        }

        if (export is null) return Fail("import", "That file contains nothing.");

        var document = Hosts.Read();
        if (document.Malformed)
            return Fail("import", "The Novimize markers in the hosts file are unbalanced, so nothing was written.");

        var change = Hosts.Write(HostsFile.ToGroups(HostsFile.ParseInner(export.HostsSection), DateTimeOffset.Now));
        if (!change.Success) return change;

        var firewallDone = 0;
        foreach (var rule in export.Firewall.Where(r => r.Kind == BlockKind.Firewall))
        {
            if (rule.Value.Length > 0 && File.Exists(rule.Value))
            {
                var addresses = File.ReadAllLines(rule.Value).Where(l => l.Trim().Length > 0).ToList();
                if (addresses.Count > 0)
                {
                    var applied = await Firewall.BlockAddressesAsync(
                        rule.Source ?? "import", addresses, rule.Category, rule.Source, rule.Purpose,
                        rule.Severity, cancel);
                    if (applied.Success) firewallDone++;
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(rule.Application)) continue;
            var program = await Firewall.BlockProgramAsync(
                rule.Id, rule.Application, rule.Category, rule.Source, rule.Purpose, rule.Severity, cancel);
            if (program.Success) firewallDone++;
        }

        return change with
        {
            Action = "import",
            Affected = change.Affected + firewallDone,
            Message = $"Imported {change.Affected} hosts rule(s)"
                      + (firewallDone > 0 ? $" and {firewallDone} firewall rule(s)." : ".")
                      + (document.Inner is null ? "" : " The previous hosts section was replaced by the file's."),
        };
    }

    private static BlockChange Fail(string action, string message) => new()
    {
        Action = action,
        Success = false,
        Message = message,
    };
}

/// <summary>The on-disk shape of an export.</summary>
public sealed record BlockExport
{
    [JsonPropertyName("exportedAt")]
    public DateTimeOffset ExportedAt { get; init; }

    /// <summary>Novimize's own hosts section, verbatim.</summary>
    [JsonPropertyName("hostsSection")]
    public string HostsSection { get; init; } = string.Empty;

    [JsonPropertyName("unmanagedCount")]
    public int UnmanagedCount { get; init; }

    [JsonPropertyName("firewall")]
    public List<BlockRule> Firewall { get; init; } = new();

    /// <summary>Source id → the addresses it contributed.</summary>
    [JsonPropertyName("addressSets")]
    public Dictionary<string, List<string>> AddressSets { get; init; } = new();
}
