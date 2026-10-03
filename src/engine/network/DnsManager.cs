using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Network;

/// <summary>
/// What the machine's resolver is set to, and how to put it back.
///
/// Three rules shape this class. The previous configuration is recorded before
/// the first change to an adapter, so "revert" restores what the user had
/// rather than a default that happens to be DHCP. The catalogue of resolvers is
/// data with no ranking attached, because no public resolver is universally the
/// fastest and a page that said otherwise would be guessing. And DoH status is
/// read from Windows itself — <c>Get-DnsClientDohServerAddress</c> is the
/// authority for what this machine will actually do, so the app never reports
/// DoH as working because a file somewhere claims it should.
/// </summary>
public sealed class DnsManager
{
    /// <summary>Windows' DNS client speaks DoH, not DoT.</summary>
    public const string DotNote =
        "Windows' built-in resolver does not support DNS-over-TLS. DoH is the encrypted transport " +
        "this OS offers; a DoT client would have to be installed and run separately.";

    private readonly string _statePath;

    public DnsManager(string? statePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "network");
        _statePath = statePath ?? Path.Combine(dir, "dns.json");
    }

    // --- catalogue ---------------------------------------------------------

    /// <summary>
    /// The resolvers Novimize knows how to configure.
    ///
    /// Provenance, so nobody has to take the next reader's word for it: every
    /// IPv4 address below answered a real query from this machine and answered
    /// ping. The IPv6 addresses and the DoH templates for Cloudflare, Google
    /// and Quad9 match what Windows itself lists in
    /// <c>Get-DnsClientDohServerAddress</c>, which is the authority for what
    /// this OS will do with them. AdGuard's addresses were confirmed by
    /// resolving <c>dns.adguard-dns.com</c> (AAAA: 2a10:50c0::ad1:ff, ::ad2:ff)
    /// and its host answers on 94.140.14.14 / 94.140.15.15; it is not in
    /// Windows' DoH table, so its template is left null and the section offers
    /// plain DNS for it rather than writing an unchecked URL into the resolver.
    /// NextDNS answered on 45.90.28.0 / 45.90.30.0, but its DoH endpoint needs
    /// an account id, so it has no template either. Latency is never stored
    /// here — it is measured.
    /// </summary>
    public static IReadOnlyList<DnsProvider> Providers { get; } = new[]
    {
        new DnsProvider
        {
            Id = "cloudflare",
            Name = "Cloudflare",
            Ipv4 = new() { "1.1.1.1", "1.0.0.1" },
            Ipv6 = new() { "2606:4700:4700::1111", "2606:4700:4700::1001" },
            DohTemplate = "https://cloudflare-dns.com/dns-query",
            Homepage = "https://one.one.one.one/",
            Note = "Publishes no query logs beyond a daily aggregate, and is anycast worldwide.",
        },
        new DnsProvider
        {
            Id = "google",
            Name = "Google Public DNS",
            Ipv4 = new() { "8.8.8.8", "8.8.4.4" },
            Ipv6 = new() { "2001:4860:4860::8888", "2001:4860:4860::8844" },
            DohTemplate = "https://dns.google/dns-query",
            Homepage = "https://developers.google.com/speed/public-dns",
            Note = "Very widely deployed; keeps temporary query logs for security and debugging.",
        },
        new DnsProvider
        {
            Id = "quad9",
            Name = "Quad9",
            Ipv4 = new() { "9.9.9.9", "149.112.112.112" },
            Ipv6 = new() { "2620:fe::fe", "2620:fe::9" },
            DohTemplate = "https://dns.quad9.net/dns-query",
            Homepage = "https://quad9.net/",
            Note = "Blocks known-malicious domains by default and is run by a non-profit.",
        },
        new DnsProvider
        {
            Id = "adguard",
            Name = "AdGuard DNS",
            Ipv4 = new() { "94.140.14.14", "94.140.15.15" },
            Ipv6 = new() { "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff" },
            DohTemplate = null,
            Homepage = "https://adguard.com/kb/general/dns-providers/",
            Note = "Filters advertising and tracking domains. No DoH template is offered: this " +
                   "server is not in Windows' known list and its endpoint could not be confirmed.",
        },
        new DnsProvider
        {
            Id = "nextdns",
            Name = "NextDNS",
            Ipv4 = new() { "45.90.28.0", "45.90.30.0" },
            Ipv6 = new(),
            DohTemplate = null,
            Homepage = "https://nextdns.io/",
            Note = "Configurable blocklists, but the DoH endpoint is account-specific, so only the " +
                   "shared plain-DNS addresses are offered here.",
        },
    };

    /// <summary>
    /// Windows reports the deprecated fec0::/10 anycast name servers when an
    /// adapter has no IPv6 resolver at all. They are not something anybody
    /// configured — RFC 3879 withdrew the prefix — and printing them as a DNS
    /// server would be printing a placeholder as a setting.
    /// </summary>
    public static bool IsDeprecatedSiteLocal(string address) =>
        address.StartsWith("fec0:", StringComparison.OrdinalIgnoreCase);

    public static DnsProvider? FindProvider(string id) =>
        Providers.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // --- reading -----------------------------------------------------------

    public async Task<DnsStatus> StatusAsync(CancellationToken cancel = default)
    {
        var raw = await RunAsync(new { op = "read" }, cancel);
        var parsed = raw is null ? null : JsonSerializer.Deserialize<ReadResult>(raw, Raw);
        if (parsed?.Adapters is null)
        {
            return new DnsStatus
            {
                Providers = Providers.ToList(),
                ResolverInfo = parsed?.Error ?? "The network configuration could not be read.",
            };
        }

        var adapters = parsed.Adapters.Select(a => new AdapterDns
        {
            Name = a.Name ?? string.Empty,
            Index = a.Index,
            Description = a.Description ?? string.Empty,
            Status = a.Status ?? string.Empty,
            Mac = a.Mac ?? string.Empty,
            Ipv4 = a.Ipv4 ?? new(),
            Ipv6 = (a.Ipv6 ?? new()).Where(v => !IsDeprecatedSiteLocal(v)).ToList(),
            Dhcp4 = a.Dhcp4,
            Dhcp6 = a.Dhcp6,
            ChangedByNovimize = a.ChangedAt != null,
            ChangedAt = a.ChangedAt,
            Previous4 = a.Previous4,
            Previous6 = a.Previous6,
            NetworkCategory = a.NetworkCategory,
            Gateway = a.Gateway,
        }).ToList();

        return new DnsStatus
        {
            Adapters = adapters,
            Doh = parsed.Doh ?? new(),
            Providers = Providers.ToList(),
            ActiveProvider = ActiveProvider(adapters),
            Ipv6Available = adapters.Any(a => a.Ipv6.Count > 0),
            Ipv6Reachable = parsed.Ipv6Reachable,
            ResolverInfo = parsed.Error,
            Measured = true,
        };
    }

    /// <summary>
    /// Which catalogue entry the machine is on. "custom" whenever anything
    /// does not line up, because a half-match reported as a provider name
    /// would be a claim about a configuration nobody has checked.
    /// </summary>
    public static string ActiveProvider(IReadOnlyList<AdapterDns> adapters)
    {
        // Only adapters that are up and actually name a resolver: a adapter
        // with no DNS of its own is not evidence about what the machine uses.
        var candidates = adapters
            .Where(a => a.Ipv4.Count > 0 || a.Ipv6.Count > 0)
            .ToList();
        if (candidates.Count == 0) return "none";

        foreach (var provider in Providers)
        {
            if (candidates.All(a => Matches(a, provider))) return provider.Id;
        }
        return "custom";

        static bool Matches(AdapterDns adapter, DnsProvider provider)
        {
            // v4 servers, when the adapter has any, must all be the provider's.
            if (adapter.Ipv4.Count > 0 &&
                !adapter.Ipv4.All(s => provider.Ipv4.Contains(s, StringComparer.OrdinalIgnoreCase)))
                return false;

            if (adapter.Ipv6.Count > 0 &&
                !adapter.Ipv6.All(s => provider.Ipv6.Contains(s, StringComparer.OrdinalIgnoreCase)))
                return false;

            // An adapter with only v6 configured still counts as the provider
            // if those addresses belong to it, and vice versa.
            if (adapter.Ipv4.Count == 0 && adapter.Ipv6.Count == 0) return false;
            return adapter.Ipv4.Any(s => provider.Ipv4.Contains(s, StringComparer.OrdinalIgnoreCase))
                || adapter.Ipv6.Any(s => provider.Ipv6.Contains(s, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>What the machine resolves with right now, measured rather than assumed.</summary>
    public async Task<LookupReport> ResolveAsync(
        string name, string type = "A", string? server = null, CancellationToken cancel = default)
    {
        var payload = new { op = "resolve", name, type, server };
        var raw = await RunAsync(payload, cancel);
        var report = raw is null ? null : JsonSerializer.Deserialize<LookupReport>(raw, Raw);
        return report ?? new LookupReport { Query = name, Type = type, Error = "The resolver did not answer." };
    }

    /// <summary>
    /// Round-trip time to each resolver, from this machine, right now. It is
    /// printed as a measurement with a timestamp and never as a ranking.
    /// </summary>
    public async Task<IReadOnlyList<DnsProvider>> MeasureAsync(CancellationToken cancel = default)
    {
        var results = new List<DnsProvider>();
        foreach (var provider in Providers)
        {
            var target = provider.Ipv4.FirstOrDefault();
            if (target is null) { results.Add(provider); continue; }

            var report = await PingAsync(target, 3, cancel);
            results.Add(provider with
            {
                LatencyMs = report.Average,
                PacketLoss = report.Sent == 0 ? null : (int)Math.Round(report.LossPercent),
            });
        }
        return results;
    }

    public async Task<PingReport> PingAsync(string host, int count = 4, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        // ping.exe is locale-dependent in its prose but stable in its numbers,
        // and parsing those numbers is the whole job here.
        var result = await ProcessRunner.RunAsync(
            "ping", new[] { "-n", Math.Clamp(count, 1, 10).ToString(), "-w", "2000", host },
            TimeSpan.FromSeconds(Math.Max(15, count * 3)));

        var text = result.StdOut;
        var report = ParsePing(host, text);
        if (report.Sent == 0 && !string.IsNullOrWhiteSpace(result.StdErr))
            return report with { Error = result.StdErr.Trim() };
        if (report.Sent == 0 && report.Error is null)
            return report with { Error = result.Output.Trim() };
        return report;
    }

    /// <summary>
    /// Parse ping's summary block. The times line is the one place all three
    /// numbers appear, and loss comes from "Lost = n", both of which survive
    /// localization of the surrounding prose well enough to be worth reading.
    /// </summary>
    public static PingReport ParsePing(string host, string output)
    {
        var times = new List<int>();
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(output, @"time[=<](\d+)\s*ms", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            times.Add(int.Parse(m.Groups[1].Value));

        var sent = MatchInt(output, @"Packets:\s*Sent\s*=\s*(\d+)")
                   ?? MatchInt(output, @"Sent\s*=\s*(\d+)")
                   ?? times.Count;
        // "Packets: Sent = 4, Received = 3, Lost = 1 (25% loss)". The first
        // pattern is the count; the second recovers it from the percentage
        // when a localized build prints no "Lost =" at all.
        var lost = MatchInt(output, @"Lost\s*=\s*(\d+)");
        if (lost is null)
        {
            var percent = MatchInt(output, @"\((\d+)%\s*loss\)");
            if (percent is not null && sent > 0)
                lost = (int)Math.Round(sent * percent.Value / 100.0);
        }
        var avg = MatchInt(output, @"Average\s*=\s*(\d+)ms");

        if (lost is null && sent == 0)
        {
            // Nothing came back at all and the summary never printed: that is
            // a refusal or an unreachable host, not zero packets sent.
            var firstLine = output.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
            return new PingReport { Host = host, Error = firstLine ?? "No answer." };
        }

        return new PingReport
        {
            Host = host,
            Sent = sent,
            Lost = lost ?? 0,
            Min = MatchInt(output, @"Minimum\s*=\s*(\d+)ms"),
            Max = MatchInt(output, @"Maximum\s*=\s*(\d+)ms"),
            Average = avg ?? (times.Count > 0 ? (int?)times.Average() : null),
            Times = times,
        };
    }

    private static int? MatchInt(string text, string pattern)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    // --- changing ----------------------------------------------------------

    /// <summary>
    /// Point one adapter, or every physical one, at a provider. The current
    /// configuration is recorded first, and the write is refused outright when
    /// there is no previous configuration to go back to and no provider to go
    /// forward to — a change with no undo is not offered.
    /// </summary>
    public async Task<NetworkChange> SetAsync(
        string providerId, string? adapter = null, bool enableDoh = false, CancellationToken cancel = default)
    {
        var provider = FindProvider(providerId);
        if (provider is null)
            return Fail("dns-set", $"'{providerId}' is not a resolver Novimize knows about.");

        if (provider.Ipv4.Count == 0 && provider.Ipv6.Count == 0)
            return Fail("dns-set", $"{provider.Name} has no address that can be configured here.");

        var status = await StatusAsync(cancel);
        var targets = Targets(status, adapter);

        if (targets.Count == 0)
            return Fail("dns-set", adapter is null
                ? "No adapter is up, so there is nothing to configure."
                : $"No adapter called '{adapter}' was found.");

        if (!Elevation.IsElevated())
            return new NetworkChange
            {
                Action = "dns-set",
                Success = false,
                NeedsElevation = true,
                Message = "Changing DNS on an adapter needs administrator rights.",
                Preview = PreviewSet(provider, targets, enableDoh),
            };

        Save(targets);

        var servers = new List<string>();
        servers.AddRange(provider.Ipv4);
        if (targets.Any(t => t.Ipv6.Count > 0 || status.Ipv6Reachable)) servers.AddRange(provider.Ipv6);

        var raw = await RunAsync(new
        {
            op = "set",
            indexes = targets.Select(t => t.Index).ToArray(),
            servers = servers.ToArray(),
            doh = enableDoh && provider.DohTemplate is not null,
            template = provider.DohTemplate,
        }, cancel);

        var change = raw is null
            ? Fail("dns-set", "The resolver was not changed; PowerShell did not answer.")
            : JsonSerializer.Deserialize<NetworkChange>(raw, Raw) ?? Fail("dns-set", "No answer.");

        return change with
        {
            Action = "dns-set",
            Affected = targets.Count,
            Preview = PreviewSet(provider, targets, enableDoh),
            Message = change.Success
                ? $"{provider.Name} is now the resolver for {targets.Count} adapter(s): "
                  + string.Join(", ", targets.Select(t => t.Name)) + "."
                : change.Message,
        };
    }

    /// <summary>
    /// The adapters a change would touch: the ones that are up, narrowed to
    /// the one that was named. Shared by the preview and the write so the
    /// command lines shown and the command lines run cannot drift apart.
    /// </summary>
    public static List<AdapterDns> Targets(DnsStatus status, string? adapter)
    {
        // Named by the user: take it as asked, even if it is down — setting
        // DNS before a cable is plugged in is a normal thing to want.
        if (adapter is not null)
            return status.Adapters
                .Where(a => a.Name.Equals(adapter, StringComparison.OrdinalIgnoreCase)
                            || a.Index.ToString() == adapter)
                .ToList();

        // Otherwise only what is live. Exactly "Up", not a substring match:
        // "Disconnected" contains "Connected", and a dead NIC is not a target.
        return status.Adapters
            .Where(a => a.Status.Equals("Up", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// What `set` would do, without doing it. The UI shows this before the
    /// confirmation, so the dialog carries the real addresses rather than a
    /// description written twice.
    /// </summary>
    public async Task<NetworkChange> PreviewAsync(
        string providerId, string? adapter = null, bool doh = false, CancellationToken cancel = default)
    {
        var provider = FindProvider(providerId);
        if (provider is null)
            return Fail("dns-set", $"'{providerId}' is not a resolver Novimize knows about.");

        var status = await StatusAsync(cancel);
        var targets = Targets(status, adapter);
        if (targets.Count == 0)
            return Fail("dns-set", adapter is null
                ? "No adapter is up, so there is nothing to configure."
                : $"No adapter called '{adapter}' was found.");

        return new NetworkChange
        {
            Action = "dns-set",
            Success = true,
            Unchanged = true,
            Affected = targets.Count,
            Preview = PreviewSet(provider, targets, doh),
            Message = $"{provider.Name} would become the resolver for "
                      + string.Join(", ", targets.Select(t => t.Name)) + ".",
        };
    }

    /// <summary>What `revert` would put back, without putting it back.</summary>
    public async Task<NetworkChange> RevertPreviewAsync(string? adapter = null, CancellationToken cancel = default)
    {
        var status = await StatusAsync(cancel);
        var saved = Load();
        var targets = status.Adapters
            .Where(a => adapter is null
                        || a.Name.Equals(adapter, StringComparison.OrdinalIgnoreCase)
                        || a.Index.ToString() == adapter)
            .Where(a => saved.ContainsKey(a.Name))
            .ToList();

        if (targets.Count == 0)
            return new NetworkChange
            {
                Action = "dns-revert",
                Success = true,
                Unchanged = true,
                Message = "No adapter was changed by Novimize, so there is nothing to put back.",
            };

        return new NetworkChange
        {
            Action = "dns-revert",
            Success = true,
            Unchanged = true,
            Affected = targets.Count,
            Preview = targets.Select(t =>
            {
                var s = saved[t.Name];
                return !s.Dhcp4 && s.Ipv4.Count > 0
                    ? $"Set-DnsClientServerAddress -InterfaceIndex {t.Index} -ServerAddresses ({string.Join(", ", s.Ipv4.Select(Quote))})"
                    : $"Set-DnsClientServerAddress -InterfaceIndex {t.Index} -ResetServerAddresses";
            }).ToList(),
            Message = $"DNS would be put back the way it was found on {targets.Count} adapter(s).",
        };
    }

    private static List<string> PreviewSet(DnsProvider provider, IReadOnlyList<AdapterDns> targets, bool doh)
    {
        var lines = new List<string>();
        foreach (var t in targets)
            lines.Add($"Set-DnsClientServerAddress -InterfaceIndex {t.Index} "
                      + $"-ServerAddresses ({string.Join(", ", provider.Ipv4.Concat(provider.Ipv6).Select(Quote))})");
        if (doh && provider.DohTemplate is not null)
            foreach (var t in targets)
                foreach (var s in provider.Ipv4.Concat(provider.Ipv6))
                    lines.Add($"Add-DnsClientDohServerAddress -ServerAddress {s} -DohTemplate {Quote(provider.DohTemplate)}");
        lines.Add("Clear-DnsClientCache");
        return lines;
    }

    /// <summary>
    /// Put an adapter back the way it was found: DHCP if that is what it was
    /// using, otherwise the exact server list that was configured.
    /// </summary>
    public async Task<NetworkChange> RevertAsync(string? adapter = null, CancellationToken cancel = default)
    {
        var status = await StatusAsync(cancel);
        var saved = Load();
        var targets = status.Adapters
            .Where(a => adapter is null
                        || a.Name.Equals(adapter, StringComparison.OrdinalIgnoreCase)
                        || a.Index.ToString() == adapter)
            .Where(a => saved.ContainsKey(a.Name))
            .ToList();

        if (targets.Count == 0)
            return new NetworkChange
            {
                Action = "dns-revert",
                Success = true,
                Unchanged = true,
                Message = "No adapter was changed by Novimize, so there is nothing to put back.",
            };

        if (!Elevation.IsElevated())
            return new NetworkChange
            {
                Action = "dns-revert",
                Success = false,
                NeedsElevation = true,
                Message = "Changing DNS on an adapter needs administrator rights.",
                Preview = targets.Select(t => $"Set-DnsClientServerAddress -InterfaceIndex {t.Index} -ResetServerAddresses").ToList(),
            };

        var payload = new
        {
            op = "revert",
            entries = targets.Select(t =>
            {
                var s = saved[t.Name];
                return new
                {
                    index = t.Index,
                    reset = s.Dhcp4 || s.Ipv4.Count == 0,
                    servers = s.Ipv4.Concat(s.Dhcp6 ? Array.Empty<string>() : s.Ipv6).ToArray(),
                };
            }).ToArray(),
        };

        var raw = await RunAsync(payload, cancel);
        var change = raw is null
            ? Fail("dns-revert", "The resolver was not changed; PowerShell did not answer.")
            : JsonSerializer.Deserialize<NetworkChange>(raw, Raw) ?? Fail("dns-revert", "No answer.");

        var preview = targets.Select(t =>
            saved.TryGetValue(t.Name, out var s) && !s.Dhcp4 && s.Ipv4.Count > 0
                ? $"Set-DnsClientServerAddress -InterfaceIndex {t.Index} -ServerAddresses ({string.Join(", ", s.Ipv4.Select(Quote))})"
                : $"Set-DnsClientServerAddress -InterfaceIndex {t.Index} -ResetServerAddresses").ToList();

        if (change.Success)
        {
            foreach (var t in targets) saved.Remove(t.Name);
            Save(saved);
        }

        return change with
        {
            Action = "dns-revert",
            Affected = targets.Count,
            Preview = preview,
        };
    }

    /// <summary>Drop the resolver cache. Read-only with respect to configuration.</summary>
    public async Task<NetworkChange> FlushAsync(CancellationToken cancel = default)
    {
        var lines = new List<string> { "Clear-DnsClientCache", "ipconfig /flushdns" };
        var clear = await ProcessRunner.RunAsync(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", "Clear-DnsClientCache" },
            TimeSpan.FromSeconds(30));
        var flush = await ProcessRunner.RunAsync("ipconfig", new[] { "/flushdns" }, TimeSpan.FromSeconds(30));

        var ok = clear.Success || flush.Success;
        return new NetworkChange
        {
            Action = "dns-flush",
            Success = true,
            Affected = ok ? 1 : 0,
            Unchanged = !ok,
            Preview = lines,
            Message = ok
                ? "The DNS resolver cache was flushed."
                : "Neither the PowerShell nor the ipconfig cache flush reported success.",
            Log = (clear.Output + "\n" + flush.Output).Trim(),
        };
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";

    private static NetworkChange Fail(string action, string message) => new()
    {
        Action = action,
        Success = false,
        Message = message,
    };

    // --- saved configuration ------------------------------------------------

    private sealed class SavedAdapter
    {
        public List<string> Ipv4 { get; init; } = new();
        public List<string> Ipv6 { get; init; } = new();
        public bool Dhcp4 { get; init; }
        public bool Dhcp6 { get; init; }
        public DateTimeOffset SavedAt { get; init; }
    }

    private Dictionary<string, SavedAdapter> Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, SavedAdapter>>(
                       File.ReadAllText(_statePath), State) ?? new();
        }
        catch { return new(); }
    }

    private void Save(IReadOnlyCollection<AdapterDns> adapters)
    {
        var state = Load();
        foreach (var a in adapters)
        {
            if (state.ContainsKey(a.Name)) continue;
            state[a.Name] = new SavedAdapter
            {
                Ipv4 = a.Ipv4.ToList(),
                Ipv6 = a.Ipv6.ToList(),
                Dhcp4 = a.Dhcp4,
                Dhcp6 = a.Dhcp6,
                SavedAt = DateTimeOffset.Now,
            };
        }
        Save(state);
    }

    private void Save(Dictionary<string, SavedAdapter> state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, State));
        }
        catch { /* a backup that cannot be written must not block the change */ }
    }

    private static readonly JsonSerializerOptions State = new() { WriteIndented = true };

    // --- PowerShell ---------------------------------------------------------

    private static readonly JsonSerializerOptions Raw = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// One script, one payload file, one JSON answer. The payload rides as a
    /// file rather than on the command line because adapter indexes and
    /// address lists have no business being interpolated into a command line
    /// that a shell will read.
    /// </summary>
    private async Task<string?> RunAsync(object payload, CancellationToken cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"novimize-dns-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(directory, "payload.json");
        var scriptPath = Path.Combine(directory, "dns.ps1");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(payloadPath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                cancel);
            await File.WriteAllTextAsync(scriptPath, Script, cancel);

            var result = await ProcessRunner.RunAsync("powershell.exe", new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", scriptPath, "-Payload", payloadPath,
            }, TimeSpan.FromSeconds(90));

            var stdout = result.StdOut;
            var start = stdout.IndexOf('{');
            return start < 0 ? null : stdout[start..];
        }
        catch { return null; }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* temp */ }
        }
    }

    private const string Script = """
        param([string]$Payload)

        $ErrorActionPreference = 'Stop'
        $p = Get-Content -LiteralPath $Payload -Raw -Encoding UTF8 | ConvertFrom-Json

        function Reply([object]$body) {
            $body | ConvertTo-Json -Depth 10 -Compress
        }

        function Addr([object]$row) {
            if ($null -eq $row) { return @() }
            $out = @()
            foreach ($a in @($row.ServerAddresses)) { if ($a) { $out += [string]$a } }
            return $out
        }

        try {
            switch ($p.op) {

                'read' {
                    $all = @(Get-DnsClientServerAddress -ErrorAction SilentlyContinue)
                    $nics = @(Get-NetAdapter -ErrorAction SilentlyContinue | Sort-Object -Property ifIndex)
                    $profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue)
                    $rows = @()

                    foreach ($n in $nics) {
                        $v4row = $all | Where-Object { $_.InterfaceIndex -eq $n.ifIndex -and $_.AddressFamily -eq 2 } | Select-Object -First 1
                        $v6row = $all | Where-Object { $_.InterfaceIndex -eq $n.ifIndex -and $_.AddressFamily -eq 23 } | Select-Object -First 1
                        $v4 = @(Addr $v4row)
                        $v6 = @(Addr $v6row)

                        $dhcp4 = $false
                        $dhcp6 = $false
                        $ip4 = Get-NetIPInterface -InterfaceIndex $n.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
                        $ip6 = Get-NetIPInterface -InterfaceIndex $n.ifIndex -AddressFamily IPv6 -ErrorAction SilentlyContinue | Select-Object -First 1
                        if ($ip4) { $dhcp4 = ([string]$ip4.Dhcp) -eq 'Enabled' }
                        if ($ip6) { $dhcp6 = ([string]$ip6.Dhcp) -eq 'Enabled' }

                        $gw = $null
                        $cfg = Get-NetIPConfiguration -InterfaceIndex $n.ifIndex -ErrorAction SilentlyContinue
                        if ($cfg -and $cfg.IPv4DefaultGateway) { $gw = [string]$cfg.IPv4DefaultGateway.NextHop }

                        $cat = $null
                        $prof = $profiles | Where-Object { $_.InterfaceIndex -eq $n.ifIndex } | Select-Object -First 1
                        if ($prof) { $cat = [string]$prof.NetworkCategory }

                        $rows += [pscustomobject]@{
                            name          = [string]$n.Name
                            index         = [int]$n.ifIndex
                            description   = [string]$n.InterfaceDescription
                            status        = [string]$n.Status
                            mac           = [string]$n.MacAddress
                            ipv4          = $v4
                            ipv6          = $v6
                            dhcp4         = $dhcp4
                            dhcp6         = $dhcp6
                            gateway       = $gw
                            networkCategory = $cat
                            changedAt     = $null
                            previous4     = $null
                            previous6     = $null
                        }
                    }

                    $doh = @(
                        Get-DnsClientDohServerAddress -ErrorAction SilentlyContinue | ForEach-Object {
                            [pscustomobject]@{
                                address       = [string]$_.ServerAddress
                                template      = [string]$_.DohTemplate
                                allowFallback = ([string]$_.AllowFallbackToUacp) -eq 'True'
                                autoUpgrade   = ([string]$_.AutoUpgrade) -eq 'True'
                            }
                        }
                    )

                    $v6route = @(Get-NetRoute -AddressFamily IPv6 -DestinationPrefix '::/0' -ErrorAction SilentlyContinue)
                    Reply @{
                        adapters = $rows
                        doh = $doh
                        ipv6Reachable = ($v6route.Count -gt 0)
                        error = $null
                    }
                }

                'resolve' {
                    $name = [string]$p.name
                    $qtype = [string]$p.type
                    if (-not $qtype) { $qtype = 'A' }
                    $server = $p.server
                    $sw = [System.Diagnostics.Stopwatch]::StartNew()
                    $records = @()
                    $err = $null
                    try {
                        $qt = $qtype.ToUpperInvariant()
                        $common = @{ Name = $name; DnsOnly = $true; ErrorAction = 'Stop' }
                        if ($qt -eq 'PTR') { $common.Type = 'PTR' }
                        elseif ($qt -eq 'AAAA') { $common.Type = 'AAAA' }
                        elseif ($qt -eq 'MX') { $common.Type = 'MX' }
                        elseif ($qt -eq 'TXT') { $common.Type = 'TXT' }
                        elseif ($qt -eq 'CNAME') { $common.Type = 'CNAME' }
                        else { $common.Type = 'A' }
                        if ($server) { $common.Server = [string]$server }
                        $ans = Resolve-DnsName @common
                        foreach ($r in @($ans)) {
                            $data = $null
                            if ($r.IPAddress) { $data = [string]$r.IPAddress }
                            elseif ($r.NameHost) { $data = [string]$r.NameHost }
                            elseif ($r.NameExchange) { $data = [string]$r.NameExchange }
                            elseif ($r.SOA) { $data = [string]$r.SOA }
                            elseif ($r.SessionState) { $data = [string]$r.SessionState }
                            if ($data) {
                                $records += [pscustomobject]@{
                                    name = [string]$r.Name
                                    type = [string]$r.Type
                                    data = $data
                                    ttl  = [int]$r.TTL
                                }
                            }
                        }
                    } catch {
                        $err = [string]$_.Exception.Message
                    }
                    $sw.Stop()
                    Reply @{
                        query = $name
                        type = $qtype
                        server = $(if ($server) { [string]$server } else { $null })
                        records = $records
                        elapsedMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
                        error = $err
                    }
                }

                'set' {
                    $indexes = @($p.indexes)
                    $servers = @($p.servers)
                    foreach ($i in $indexes) {
                        Set-DnsClientServerAddress -InterfaceIndex ([int]$i) -ServerAddresses $servers -ErrorAction Stop
                        if ($p.doh) {
                            foreach ($s in $servers) {
                                Add-DnsClientDohServerAddress -ServerAddress $s -DohTemplate ([string]$p.template) -AllowFallbackToUacp $false -ErrorAction SilentlyContinue
                            }
                        }
                    }
                    Clear-DnsClientCache
                    Reply @{ success = $true; message = 'Resolver configured.'; error = $null }
                }

                'revert' {
                    foreach ($e in @($p.entries)) {
                        if ($e.reset) {
                            Set-DnsClientServerAddress -InterfaceIndex ([int]$e.index) -ResetServerAddresses -ErrorAction Stop
                        } else {
                            Set-DnsClientServerAddress -InterfaceIndex ([int]$e.index) -ServerAddresses @($e.servers) -ErrorAction Stop
                        }
                    }
                    Clear-DnsClientCache
                    Reply @{ success = $true; message = 'Resolver restored.'; error = $null }
                }

                default { Reply @{ success = $false; message = 'Unknown operation.'; error = 'unknown' } }
            }
        } catch {
            Reply @{ success = $false; message = [string]$_.Exception.Message; error = [string]$_.Exception.Message }
        }
        """;

    // --- JSON shapes the script answers -------------------------------------

    private sealed class ReadResult
    {
        public List<ReadAdapter>? Adapters { get; init; }
        public List<DohServer>? Doh { get; init; }
        public bool Ipv6Reachable { get; init; }
        public string? Error { get; init; }
    }

    private sealed class ReadAdapter
    {
        public string? Name { get; init; }
        public int Index { get; init; }
        public string? Description { get; init; }
        public string? Status { get; init; }
        public string? Mac { get; init; }
        public List<string>? Ipv4 { get; init; }
        public List<string>? Ipv6 { get; init; }
        public bool Dhcp4 { get; init; }
        public bool Dhcp6 { get; init; }
        public string? Gateway { get; init; }
        public string? NetworkCategory { get; init; }
        public DateTimeOffset? ChangedAt { get; init; }
        public List<string>? Previous4 { get; init; }
        public List<string>? Previous6 { get; init; }
    }
}
