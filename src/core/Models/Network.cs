using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

// ── DNS ─────────────────────────────────────────────────────────────────────

/// <summary>
/// A public resolver somebody else runs, referred to by address.
///
/// The addresses are data, not endorsements: nothing here is described as the
/// fastest, because "fastest" depends on where you are and what your ISP does
/// with UDP/53, and a page that ranks them would be making a claim it cannot
/// keep. <see cref="LatencyMs"/> is filled in at read time by measuring, so the
/// number the user sees is a measurement from their machine, not a promise.
/// </summary>
public sealed record DnsProvider
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>IPv4 resolvers, primary first.</summary>
    public List<string> Ipv4 { get; init; } = new();

    /// <summary>IPv6 resolvers. Empty when none could be confirmed.</summary>
    public List<string> Ipv6 { get; init; } = new();

    /// <summary>
    /// The RFC 8484 endpoint, or null when it could not be confirmed. Null
    /// means the DNS section offers plain DNS for this provider and says so,
    /// rather than writing a URL into Windows that has not been checked.
    /// </summary>
    public string? DohTemplate { get; init; }

    public string? Homepage { get; init; }

    /// <summary>What is different about this resolver, in one sentence.</summary>
    public string? Note { get; init; }

    /// <summary>Filled in by a live latency measurement, not by the file.</summary>
    [JsonIgnore]
    public int? LatencyMs { get; init; }

    [JsonIgnore]
    public int? PacketLoss { get; init; }
}

/// <summary>One adapter's DNS configuration, as the machine has it now.</summary>
public sealed record AdapterDns
{
    public string Name { get; init; } = string.Empty;
    public int Index { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Mac { get; init; } = string.Empty;

    public List<string> Ipv4 { get; init; } = new();
    public List<string> Ipv6 { get; init; } = new();

    /// <summary>True when the servers come from DHCP rather than from a setting.</summary>
    public bool Dhcp4 { get; init; }
    public bool Dhcp6 { get; init; }

    /// <summary>True when Novimize changed this adapter, so it can be put back.</summary>
    public bool ChangedByNovimize { get; init; }
    public DateTimeOffset? ChangedAt { get; init; }

    /// <summary>What was configured before, saved on the first change.</summary>
    public List<string>? Previous4 { get; init; }
    public List<string>? Previous6 { get; init; }

    public string? NetworkCategory { get; init; }
    public string? Gateway { get; init; }
}

/// <summary>Windows' own record of what it knows about DoH for one server.</summary>
public sealed record DohServer
{
    public string Address { get; init; } = string.Empty;
    public string? Template { get; init; }
    public bool AllowFallback { get; init; }
    public bool AutoUpgrade { get; init; }
}

public sealed record DnsStatus
{
    public List<AdapterDns> Adapters { get; init; } = new();

    /// <summary>What Windows knows about DoH, read from Windows.</summary>
    public List<DohServer> Doh { get; init; } = new();

    public List<DnsProvider> Providers { get; init; } = new();

    /// <summary>Which catalogue entry every non-empty server list matches, or "custom".</summary>
    public string ActiveProvider { get; init; } = "none";

    public bool Ipv6Available { get; init; }

    /// <summary>
    /// True when the machine answers over IPv6 at all. False on a connection
    /// with no v6 route, which is the common case and worth saying out loud
    /// rather than listing v6 servers that cannot be reached.
    /// </summary>
    public bool Ipv6Reachable { get; init; }

    public string? ResolverInfo { get; init; }

    /// <summary>True when the local resolver has not been looked at yet.</summary>
    public bool Measured { get; init; }
}

// ── Change results ──────────────────────────────────────────────────────────

/// <summary>
/// What one network operation did, with the commands it ran or would run.
/// The preview is not decoration: the brief asks for a preview before any
/// reset, and a list of the exact command lines is the only preview that
/// cannot be optimistic about what will happen.
/// </summary>
public record NetworkChange
{
    public string Action { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool Unchanged { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Affected { get; init; }
    public bool NeedsElevation { get; init; }
    public string Log { get; init; } = string.Empty;

    /// <summary>Command lines, one per entry, in the order they run.</summary>
    public List<string> Preview { get; init; } = new();

    /// <summary>Set when the operation wants a restart to finish.</summary>
    public bool RestartRequired { get; init; }
}

// ── Toolbox ─────────────────────────────────────────────────────────────────

public sealed record PingReport
{
    public string Host { get; init; } = string.Empty;
    public int Sent { get; init; }
    public int Lost { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
    public int? Average { get; init; }
    public List<int> Times { get; init; } = new();
    public string? Error { get; init; }

    [JsonIgnore]
    public double LossPercent => Sent == 0 ? 0 : Lost * 100.0 / Sent;
}

public sealed record TraceHop
{
    public int Hop { get; init; }
    public string Host { get; init; } = string.Empty;
    public List<int> Times { get; init; } = new();
}

public sealed record TraceReport
{
    public string Host { get; init; } = string.Empty;
    public List<TraceHop> Hops { get; init; } = new();

    /// <summary>The address the target resolved to, when it could be resolved.</summary>
    public string? TargetIp { get; init; }

    /// <summary>True only when the last hop is the target itself.</summary>
    public bool Reached { get; init; }
    public string? Error { get; init; }
}

public sealed record LookupRecord
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Data { get; init; } = string.Empty;
    public int Ttl { get; init; }
}

public sealed record LookupReport
{
    public string Query { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? Server { get; init; }
    public List<LookupRecord> Records { get; init; } = new();
    public double ElapsedMs { get; init; }
    public string? Error { get; init; }
}

public sealed record AdapterInfo
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Mac { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public bool Physical { get; init; }
    public List<string> Ipv4 { get; init; } = new();
    public List<string> Ipv6 { get; init; } = new();
    public string? Gateway { get; init; }
    public string? Dhcp { get; init; }
    public long? Mtu { get; init; }
    public int? Metric { get; init; }
    public string? NetworkCategory { get; init; }
    public string? Dns4 { get; init; }
}

public sealed record RouteEntry
{
    public string Destination { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;
    public string NextHop { get; init; } = string.Empty;
    public string Interface { get; init; } = string.Empty;
    public string Metric { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public string Store { get; init; } = string.Empty;
}

public sealed record TcpSnapshot
{
    public List<KeyValuePair<string, string>> Values { get; init; } = new();
    public string? CongestionProvider { get; init; }
    public int? Rtt { get; init; }
    public string? InitialWindow { get; init; }
    public string? Error { get; init; }
}

public sealed record NetworkProfileInfo
{
    public string Name { get; init; } = string.Empty;
    public string InterfaceAlias { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string IPv4Connectivity { get; init; } = string.Empty;
    public string IPv6Connectivity { get; init; } = string.Empty;
}

/// <summary>Everything the Network page reads in one pass.</summary>
public sealed record NetworkOverview
{
    public DnsStatus Dns { get; init; } = new();
    public List<AdapterInfo> Adapters { get; init; } = new();
    public List<RouteEntry> Routes { get; init; } = new();
    public List<NetworkProfileInfo> Profiles { get; init; } = new();
    public TcpSnapshot Tcp { get; init; } = new();
    public string? Gateway { get; init; }
    public string? LocalIp { get; init; }
    public long? Mtu { get; init; }
    public bool Ipv6Available { get; init; }

    /// <summary>
    /// Only filled in when the caller explicitly asked for it: a public IP
    /// lookup is an outbound request to a third party, and doing it silently
    /// on page load would be exactly the kind of thing this app should not do.
    /// </summary>
    public string? PublicIp { get; init; }
    public string? PublicIpNote { get; init; }
    public string? Error { get; init; }
}
