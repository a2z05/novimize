using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Network;

/// <summary>
/// The read-only network utilities, plus the three that put the stack back to
/// a known state.
///
/// Everything that observes runs without asking anything. Everything that
/// changes — renewing a lease, restarting an adapter, resetting TCP/IP or
/// Winsock — carries the exact command lines it will run, and those lines are
/// shown before the action rather than logged after it. A reset that runs
/// before anybody has read it is the failure mode this class exists to avoid.
/// </summary>
public sealed class NetToolbox
{
    /// <summary>
    /// Where "what is my public address" is asked. It is an outbound request
    /// to a third party, which is why it is never part of a normal read: the
    /// page has to ask for it, and the note says who was asked.
    /// </summary>
    public const string PublicIpUrl = "https://api.ipify.org?format=json";

    public const string PublicIpNote =
        "Asked api.ipify.org over HTTPS. Nothing else about this machine is sent.";

    private readonly DnsManager _dns;

    public NetToolbox(DnsManager? dns = null) => _dns = dns ?? new DnsManager();

    // --- reading -----------------------------------------------------------

    public async Task<NetworkOverview> OverviewAsync(bool withPublicIp = false, CancellationToken cancel = default)
    {
        var dns = await _dns.StatusAsync(cancel);
        var raw = await RunAsync(new { op = "overview" }, cancel);
        var read = raw is null ? null : JsonSerializer.Deserialize<OverviewRead>(raw, Raw);

        var overview = new NetworkOverview
        {
            Dns = dns,
            Adapters = read?.Adapters?.Select(a => new AdapterInfo
            {
                Name = a.Name ?? string.Empty,
                Description = a.Description ?? string.Empty,
                Status = a.Status ?? string.Empty,
                Mac = a.Mac ?? string.Empty,
                Kind = a.Kind ?? string.Empty,
                Physical = a.Physical,
                Ipv4 = a.Ipv4 ?? new(),
                Ipv6 = a.Ipv6 ?? new(),
                Gateway = a.Gateway,
                Dhcp = a.Dhcp,
                Mtu = a.Mtu,
                Metric = a.Metric,
                NetworkCategory = a.NetworkCategory,
                Dns4 = a.Dns4,
            }).ToList() ?? new(),
            Routes = read?.Routes?.ToList() ?? new(),
            Profiles = read?.Profiles?.ToList() ?? new(),
            Tcp = read?.Tcp ?? new TcpSnapshot(),
            // The address on the adapter that actually routes, not the first
            // one in index order — that is usually a disconnected NIC holding
            // a 169.254 link-local address nobody can use.
            Gateway = read?.Adapters?.Select(a => a.Gateway).FirstOrDefault(g => !string.IsNullOrEmpty(g)),
            LocalIp = read?.Adapters?
                .Where(a => !string.IsNullOrEmpty(a.Gateway) && a.Ipv4 is not null)
                .SelectMany(a => a.Ipv4)
                .FirstOrDefault(ip => !ip.StartsWith("169.254."))
                ?? read?.Adapters?.SelectMany(a => a.Ipv4 ?? new()).FirstOrDefault(),
            Mtu = read?.Adapters?.Where(a => !string.IsNullOrEmpty(a.Gateway))
                .Select(a => a.Mtu).FirstOrDefault(m => m is not null)
                ?? read?.Adapters?.Select(a => a.Mtu).FirstOrDefault(m => m is not null),
            Ipv6Available = dns.Ipv6Available,
            Error = read?.Error ?? dns.ResolverInfo,
        };

        if (withPublicIp)
        {
            var (ip, error) = await PublicIpAsync(cancel);
            overview = overview with { PublicIp = ip, PublicIpNote = error ?? PublicIpNote };
        }

        return overview;
    }

    /// <summary>
    /// The caller's own public address. Only ever reached when something asked
    /// for it explicitly — see <see cref="PublicIpNote"/>.
    /// </summary>
    /// <summary>What asking for the public address costs: one outbound GET.</summary>
    public static NetworkChange PublicIpPreview() => new()
    {
        Action = "public-ip",
        Success = true,
        Unchanged = true,
        Affected = 1,
        Preview = new List<string> { $"GET {PublicIpUrl}" },
        Message = $"A single request to {new Uri(PublicIpUrl).Host} over HTTPS to ask what this machine's " +
                  "address is. Nothing else is sent.",
    };

    public async Task<(string? Ip, string? Error)> PublicIpAsync(CancellationToken cancel = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Novimize/1.0");
            var body = await http.GetStringAsync(PublicIpUrl, cancel);
            // The endpoint answers with {"ip":"1.2.3.4"}; taking the address
            // out with a match rather than a deserializer keeps a change to
            // their payload from being read as a successful empty answer.
            var match = Regex.Match(body, "\"ip\"\\s*:\\s*\"([0-9a-fA-F:.]+)\"");
            if (match.Success) return (match.Groups[1].Value, PublicIpNote);
            var plain = body.Trim();
            return Regex.IsMatch(plain, "^[0-9a-fA-F:.]+$")
                ? (plain, PublicIpNote)
                : (null, "api.ipify.org did not return an address.");
        }
        catch (Exception ex)
        {
            return (null, $"The public address could not be fetched: {ex.Message}");
        }
    }

    // --- traceroute --------------------------------------------------------

    public async Task<TraceReport> TraceAsync(string host, int maxHops = 30, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        var target = await ResolveTargetAsync(host, cancel);

        var result = await ProcessRunner.RunAsync("tracert",
            new[] { "-d", "-h", Math.Clamp(maxHops, 1, 64).ToString(), "-w", "2000", host },
            TimeSpan.FromSeconds(Math.Max(45, maxHops * 3)));

        var report = ParseTracert(host, result.StdOut);
        if (report.Hops.Count == 0 && !result.Success)
            return report with { Error = result.Output.Trim() };

        var last = report.Hops.Count > 0 ? report.Hops[^1].Host : null;
        return report with
        {
            TargetIp = target,
            Reached = target is not null && last is not null
                      && last.Equals(target, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static async Task<string?> ResolveTargetAsync(string host, CancellationToken cancel)
    {
        if (Regex.IsMatch(host, @"^\d{1,3}(\.\d{1,3}){3}$")) return host;
        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host, cancel);
            return addresses.FirstOrDefault(a =>
                a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString()
                ?? addresses.FirstOrDefault()?.ToString();
        }
        catch { return null; }
    }

    /// <summary>
    /// tracert prints one hop per line, three timings (or a star), and — with
    /// -d — the address at the end. The surrounding prose is localized, so the
    /// parse leans only on the numbers and the star, which are not.
    /// </summary>
    public static TraceReport ParseTracert(string host, string output)
    {
        var hops = new List<TraceHop>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var number = Regex.Match(line, @"^\s*(\d{1,3})\s+");
            if (!number.Success) continue;

            var times = new List<int>();
            foreach (Match m in Regex.Matches(line, @"(<\d+|\d+)\s*ms"))
                times.Add(m.Groups[1].Value.StartsWith('<') ? 0 : int.Parse(m.Groups[1].Value));

            // A hop that answered nothing still gets a line of its own: the
            // star row is where the trace went quiet, and dropping it would
            // renumber every hop after it.
            var addresses = Regex.Matches(line, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
            if (addresses.Count == 0 && times.Count == 0 && !line.Contains('*')) continue;

            hops.Add(new TraceHop
            {
                Hop = int.Parse(number.Groups[1].Value),
                Host = addresses.Count > 0 ? addresses[^1].Value : "*",
                Times = times,
            });
        }

        if (hops.Count == 0)
        {
            var firstLine = output.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
            return new TraceReport { Host = host, Error = firstLine ?? "tracert produced no hops." };
        }
        return new TraceReport { Host = host, Hops = hops };
    }

    /// <summary>Reverse lookup: an address back to the name that points at it.</summary>
    public Task<LookupReport> ReverseAsync(string address, CancellationToken cancel = default) =>
        _dns.ResolveAsync(address, "PTR", null, cancel);

    public Task<LookupReport> LookupAsync(string name, string type = "A", string? server = null, CancellationToken cancel = default) =>
        _dns.ResolveAsync(name, type, server, cancel);

    // --- changes -----------------------------------------------------------

    /// <summary>
    /// What Fix Network would run, without running it. Shown before the action
    /// so the dialog is a preview and not a description of one.
    /// </summary>
    public NetworkChange FixPreview(string level)
    {
        var commands = FixCommands(level, Array.Empty<string>());
        return new NetworkChange
        {
            Action = "fix-preview",
            Success = true,
            Unchanged = true,
            Affected = commands.Count,
            Preview = commands,
            RestartRequired = level.Equals("full", StringComparison.OrdinalIgnoreCase),
            Message = level.Equals("full", StringComparison.OrdinalIgnoreCase)
                ? "A full reset rewrites the TCP/IP stack and the Winsock catalog, and needs a restart."
                : "A quick fix renews the lease, flushes the resolver and restarts the adapters.",
        };
    }

    /// <summary>
    /// The three reset paths. `quick` renews the lease and restarts the
    /// adapters; `full` adds the Winsock and TCP/IP resets, which are the ones
    /// that cannot be undone from here and are the reason a restart is asked
    /// for rather than assumed.
    /// </summary>
    public async Task<NetworkChange> FixAsync(string level, bool confirm, CancellationToken cancel = default)
    {
        level = level.Equals("full", StringComparison.OrdinalIgnoreCase) ? "full" : "quick";
        var adapters = await AdapterNamesAsync(cancel);
        var commands = FixCommands(level, adapters);

        if (!confirm)
            return new NetworkChange
            {
                Action = "fix",
                Success = false,
                Message = "Fix Network rewrites part of the network configuration. Nothing has been run; " +
                          "the commands below are what it would do.",
                Preview = commands,
                RestartRequired = level == "full",
            };

        if (!Elevation.IsElevated())
            return new NetworkChange
            {
                Action = "fix",
                Success = false,
                NeedsElevation = true,
                Message = "Fix Network needs administrator rights, so nothing was changed.",
                Preview = commands,
                RestartRequired = level == "full",
            };

        var log = new List<string>();
        var failed = new List<string>();
        foreach (var command in commands)
        {
            var parts = SplitCommandLine(command);
            if (parts.Length == 0) continue;
            try
            {
                var result = await ProcessRunner.RunAsync(parts[0], parts[1..], TimeSpan.FromSeconds(90));
                log.Add($"$ {command}\n{result.Output.Trim()}");
                if (!result.Success && result.ExitCode != 0) failed.Add(command);
            }
            catch (Exception ex)
            {
                failed.Add($"{command} — {ex.Message}");
            }
        }

        return new NetworkChange
        {
            Action = "fix",
            Success = failed.Count == 0,
            Affected = commands.Count - failed.Count,
            Preview = commands,
            RestartRequired = level == "full",
            Message = failed.Count == 0
                ? $"{commands.Count} step(s) ran. " + (level == "full"
                    ? "Restart Windows to finish the reset."
                    : "The adapters were reloaded.")
                : $"{failed.Count} step(s) did not report success: {string.Join("; ", failed)}.",
            Log = string.Join("\n\n", log),
        };
    }

    private static List<string> FixCommands(string level, IReadOnlyList<string> adapters)
    {
        // No /release: it drops every adapter's lease at once, including the
        // VPN and virtual ones the user did not ask about. /renew asks for a
        // fresh lease on its own, which is the part that actually helps.
        var commands = new List<string>
        {
            "ipconfig /flushdns",
            "ipconfig /renew",
        };

        // The whole PowerShell body is one quoted argument: an adapter called
        // "Wi Fi" would otherwise arrive as two arguments, and PowerShell
        // would be told to restart an adapter named 'Wi with a stray quote.
        foreach (var adapter in adapters)
            commands.Add(PSRestart(adapter));

        if (level == "full")
        {
            commands.Add("netsh int ip reset");
            commands.Add("netsh winsock reset");
        }
        return commands;
    }

    /// <summary>What restarting one adapter would run, without running it.</summary>
    public static NetworkChange RestartPreview(string name) => new()
    {
        Action = "restart-adapter",
        Success = true,
        Unchanged = true,
        Affected = 1,
        Preview = new List<string> { PSRestart(name) },
        Message = $"{name} would be taken down and brought back up, dropping every connection it carries.",
    };

    /// <summary>The PowerShell body that reloads one adapter.</summary>
    public static string RestartBody(string adapter) =>
        $"Restart-NetAdapter -Name '{adapter.Replace("'", "''")}' -Confirm:$false";

    /// <summary>That body as one quoted command line, the way the preview shows it.</summary>
    public static string PSRestart(string adapter) =>
        $"powershell -NoProfile -NonInteractive -Command \"{RestartBody(adapter)}\"";

    /// <summary>Put one adapter down and up again.</summary>
    public async Task<NetworkChange> RestartAdapterAsync(string name, bool confirm, CancellationToken cancel = default)
    {
        var adapters = await AdapterNamesAsync(cancel);
        var target = adapters.FirstOrDefault(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))
                     ?? adapters.FirstOrDefault(a => a.Contains(name, StringComparison.OrdinalIgnoreCase));

        var commands = new List<string> { PSRestart(target ?? name) };

        if (target is null)
            return new NetworkChange
            {
                Action = "restart-adapter",
                Success = false,
                Message = $"No adapter matches '{name}'.",
                Preview = commands,
            };

        if (!confirm)
            return new NetworkChange
            {
                Action = "restart-adapter",
                Success = false,
                Message = $"Restarting {target} drops every connection currently using it. Nothing has been run.",
                Preview = commands,
            };

        if (!Elevation.IsElevated())
            return new NetworkChange
            {
                Action = "restart-adapter",
                Success = false,
                NeedsElevation = true,
                Message = "Restarting an adapter needs administrator rights.",
                Preview = commands,
            };

        var result = await ProcessRunner.RunAsync("powershell",
            new[] { "-NoProfile", "-NonInteractive", "-Command", RestartBody(target) },
            TimeSpan.FromSeconds(60));

        return new NetworkChange
        {
            Action = "restart-adapter",
            Success = result.Success,
            Affected = result.Success ? 1 : 0,
            Preview = commands,
            Message = result.Success
                ? $"{target} was restarted."
                : $"Restarting {target} failed: {result.Output.Trim()}",
            Log = result.Output.Trim(),
        };
    }

    private async Task<IReadOnlyList<string>> AdapterNamesAsync(CancellationToken cancel)
    {
        // Physical adapters that are up, and nothing else: restarting a
        // software VPN or a hypervisor switch is somebody else's problem and
        // would take their tunnel down with it.
        var result = await ProcessRunner.RunAsync("powershell",
            new[] { "-NoProfile", "-NonInteractive", "-Command",
                    "(Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and -not $_.Virtual }).Name" },
            TimeSpan.FromSeconds(30));
        cancel.ThrowIfCancellationRequested();
        return result.StdOut.Split('\n')
            .Select(l => l.Trim().Trim('\r'))
            .Where(l => l.Length > 0 && !l.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Split a command line the way the preview shows it. Quotes are honoured
    /// so a name with a space still reaches one argument rather than two, and
    /// nothing here is passed through a shell.
    /// </summary>
    public static string[] SplitCommandLine(string command)
    {
        var parts = new List<string>();
        foreach (Match m in Regex.Matches(command, @"[\""].+?[\""]|[^ ]+"))
        {
            var value = m.Value;
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            parts.Add(value);
        }
        return parts.ToArray();
    }

    // --- PowerShell ---------------------------------------------------------

    private static readonly JsonSerializerOptions Raw = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<string?> RunAsync(object payload, CancellationToken cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"novimize-net-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(directory, "payload.json");
        var scriptPath = Path.Combine(directory, "net.ps1");
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

            var start = result.StdOut.IndexOf('{');
            return start < 0 ? null : result.StdOut[start..];
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

        function Reply([object]$body) { $body | ConvertTo-Json -Depth 10 -Compress }

        try {
            switch ($p.op) {

                'overview' {
                    $nics = @(Get-NetAdapter -ErrorAction SilentlyContinue | Sort-Object -Property ifIndex)
                    $ips = @(Get-NetIPConfiguration -ErrorAction SilentlyContinue)
                    $ifaces = @(Get-NetIPInterface -ErrorAction SilentlyContinue)
                    $dns = @(Get-DnsClientServerAddress -ErrorAction SilentlyContinue)
                    $profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue)
                    $adapters = @()

                    foreach ($n in $nics) {
                        $cfg = $ips | Where-Object { $_.InterfaceIndex -eq $n.ifIndex } | Select-Object -First 1
                        $v4 = @(); $v6 = @(); $gw = $null; $dhcpServer = $null
                        if ($cfg) {
                            foreach ($a in @($cfg.IPv4Address))   { if ($a) { $v4 += [string]$a.IPAddress } }
                            foreach ($a in @($cfg.IPv6Address))   { if ($a) { $v6 += [string]$a.IPAddress } }
                            if ($cfg.IPv4DefaultGateway) { $gw = [string]$cfg.IPv4DefaultGateway.NextHop }
                        }

                        $v4if = $ifaces | Where-Object { $_.InterfaceIndex -eq $n.ifIndex -and $_.AddressFamily -eq 'IPv4' } | Select-Object -First 1
                        $dhcp = $null; $mtu = $null; $metric = $null
                        if ($v4if) {
                            $dhcp = [string]$v4if.Dhcp
                            if ($v4if.NlMtu) { $mtu = [int]$v4if.NlMtu }
                            if ($v4if.InterfaceMetric) { $metric = [int]$v4if.InterfaceMetric }
                        }

                        $d4 = $dns | Where-Object { $_.InterfaceIndex -eq $n.ifIndex -and $_.AddressFamily -eq 2 } | Select-Object -First 1
                        $dns4 = $null
                        if ($d4) { $dns4 = (@($d4.ServerAddresses) -join ', ') }

                        $cat = $null
                        $prof = $profiles | Where-Object { $_.InterfaceIndex -eq $n.ifIndex } | Select-Object -First 1
                        if ($prof) { $cat = [string]$prof.NetworkCategory }

                        $isVirtual = $false
                        if ($n.PSObject.Properties['Virtual']) { $isVirtual = [bool]$n.Virtual }

                        $adapters += [pscustomobject]@{
                            name          = [string]$n.Name
                            description   = [string]$n.InterfaceDescription
                            status        = [string]$n.Status
                            mac           = [string]$n.MacAddress
                            kind          = [string]$n.NdisPhysicalMedium
                            physical      = (-not $isVirtual)
                            ipv4          = $v4
                            ipv6          = $v6
                            gateway       = $gw
                            dhcp          = $dhcp
                            mtu           = $mtu
                            metric        = $metric
                            networkCategory = $cat
                            dns4          = $dns4
                        }
                    }

                    # -AddressStore is not a parameter of Get-NetRoute (it is a
                    # property), and asking for it aborts the whole read. One
                    # section failing must not cost the others.
                    $routes = @()
                    try {
                    $routes = @(
                        Get-NetRoute -ErrorAction SilentlyContinue |
                            Where-Object { $_.Store -ne 'Persistent' } |
                            Sort-Object -Property RouteMetric |
                            ForEach-Object {
                                [pscustomobject]@{
                                    destination = [string]$_.DestinationPrefix
                                    prefix      = [string]$_.DestinationPrefix
                                    nextHop     = [string]$_.NextHop
                                    interface   = [string]$_.InterfaceAlias
                                    metric      = [string]$_.RouteMetric
                                    protocol    = [string]$_.Protocol
                                    store       = [string]$_.Store
                                }
                            }
                    )
                    } catch { $routes = @() }

                    $profileRows = @()
                    try {
                    $profileRows = @(
                        Get-NetConnectionProfile -ErrorAction SilentlyContinue | ForEach-Object {
                            [pscustomobject]@{
                                name             = [string]$_.Name
                                interfaceAlias   = [string]$_.InterfaceAlias
                                category         = [string]$_.NetworkCategory
                                ipv4Connectivity = [string]$_.IPv4Connectivity
                                ipv6Connectivity = [string]$_.IPv6Connectivity
                            }
                        }
                    )
                    } catch { $profileRows = @() }

                    # A curated set of documented Tcpip\Parameters values. Absent
                    # means "OS default", which is the answer, not a gap.
                    $wanted = @(
                        'DefaultTTL','Tcp1323Opts','TcpWindowSize','GlobalMaxTcpWindowSize',
                        'TcpMaxDataRetransmissions','TcpTimedWaitDelay','MaxUserPort',
                        'EnablePMTUDiscovery','EnablePMTUBHDetect','SackOpts',
                        'SynAttackProtect','EnableRSS','EnableTCPA','EnableECN','TcpFinTimeout'
                    )
                    $params = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters' -ErrorAction SilentlyContinue
                    $values = @()
                    foreach ($w in $wanted) {
                        $raw = $null
                        if ($params) { $raw = $params.PSObject.Properties[$w].Value }
                        $values += [pscustomobject]@{ key = $w; value = $(if ($null -ne $raw) { [string]$raw } else { $null }) }
                    }

                    $tcpTemplate = $null
                    $rtt = $null
                    $iw = $null
                    try {
                        $t = Get-NetTCPSetting -SettingName Internet -ErrorAction SilentlyContinue | Select-Object -First 1
                        if ($t) {
                            if ($t.CongestionProvider) { $tcpTemplate = [string]$t.CongestionProvider }
                            if ($t.InitialRtt) { $rtt = [int]$t.InitialRtt }
                            if ($t.AutoTuningLevelLocal) { $iw = [string]$t.AutoTuningLevelLocal }
                        }
                    } catch { }

                    Reply @{
                        adapters = $adapters
                        routes   = $routes
                        profiles = $profileRows
                        tcp      = @{
                            values = $values
                            congestionProvider = $tcpTemplate
                            rtt = $rtt
                            initialWindow = $iw
                            error = $null
                        }
                        error = $null
                    }
                }

                default { Reply @{ error = 'Unknown operation.' } }
            }
        } catch {
            Reply @{ error = [string]$_.Exception.Message }
        }
        """;

    // --- read shapes --------------------------------------------------------

    private sealed class OverviewRead
    {
        public List<OverviewAdapter>? Adapters { get; init; }
        public List<RouteEntry>? Routes { get; init; }
        public List<NetworkProfileInfo>? Profiles { get; init; }
        public TcpSnapshot? Tcp { get; init; }
        public string? Error { get; init; }
    }

    private sealed class OverviewAdapter
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Status { get; init; }
        public string? Mac { get; init; }
        public string? Kind { get; init; }
        public bool Physical { get; init; }
        public List<string>? Ipv4 { get; init; }
        public List<string>? Ipv6 { get; init; }
        public string? Gateway { get; init; }
        public string? Dhcp { get; init; }
        public long? Mtu { get; init; }
        public int? Metric { get; init; }
        public string? NetworkCategory { get; init; }
        public string? Dns4 { get; init; }
    }
}
