using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>Which mechanism a rule is enforced through.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BlockKind
{
    /// <summary>A domain or IP resolved to a null address by the hosts file.</summary>
    Hosts,

    /// <summary>A Windows Firewall rule that drops traffic for an application.</summary>
    Firewall,
}

/// <summary>
/// Why a rule exists. The brief requires these to stay separate, because
/// "ads" and "telemetry" and "malware" are different claims with different
/// costs when they turn out to be wrong, and "software" in particular must
/// never be presented as anything other than an optional endpoint rule.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BlockCategory
{
    Ads,
    Trackers,
    Telemetry,
    Malware,
    Analytics,

    /// <summary>
    /// Optional software-specific endpoints. Never framed as an activation or
    /// licensing bypass — only as "this product phones home to these hosts",
    /// with the breakage spelled out.
    /// </summary>
    Software,

    /// <summary>Written by the user, or by a list they imported by hand.</summary>
    Custom,
}

/// <summary>How much a rule is expected to break if it is wrong.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BlockSeverity
{
    /// <summary>A tracker domain. Nothing depends on it.</summary>
    Low,

    /// <summary>May change how a product behaves, or log you out.</summary>
    Medium,

    /// <summary>May stop a product working. Requires an explicit confirmation.</summary>
    High,
}

/// <summary>
/// One enforced rule, as the application understands it. The hosts file itself
/// remains the source of truth for what is active: this is a reading of it,
/// enriched with whatever the block comment said about the rule's origin.
/// </summary>
public sealed record BlockRule
{
    /// <summary>Domain, IP, or the firewall rule name. Unique within its kind.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public BlockKind Kind { get; init; }

    /// <summary>The address or rule value as written on disk.</summary>
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public BlockCategory Category { get; init; } = BlockCategory.Custom;

    /// <summary>What this blocks and why, in one sentence a person can read.</summary>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; init; }

    /// <summary>Where it came from: a source id, "custom", or "import".</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; init; }

    [JsonPropertyName("severity")]
    public BlockSeverity Severity { get; init; } = BlockSeverity.Low;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// False when the line was found outside the managed section. Novimize
    /// lists those so the count is honest, and never edits or removes them.
    /// </summary>
    [JsonPropertyName("managed")]
    public bool Managed { get; init; }

    /// <summary>For firewall rules: the executable, service, or address they apply to.</summary>
    [JsonPropertyName("application")]
    public string? Application { get; init; }

    [JsonPropertyName("addedAt")]
    public DateTimeOffset? AddedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>A blocklist somebody else publishes, referred to but never shipped.</summary>
public sealed record BlockSource
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public BlockCategory Category { get; init; }

    /// <summary>Where to fetch the list from. Shown before anything is downloaded.</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>The project's own page, not the raw file.</summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    /// <summary>What it blocks, in a sentence. Not a claim about what it improves.</summary>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; init; }

    /// <summary>What stops working if this list is applied. Null means "nothing known".</summary>
    [JsonPropertyName("breakage")]
    public string? Breakage { get; init; }

    [JsonPropertyName("format")]
    public string Format { get; init; } = "hosts";

    [JsonPropertyName("severity")]
    public BlockSeverity Severity { get; init; } = BlockSeverity.Low;

    /// <summary>Free-text search aids. Not a taxonomy — the category is the taxonomy.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = new();
}

/// <summary>A list as it currently stands on this machine, before any change.</summary>
public sealed record BlockFetchResult
{
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Where the downloaded copy is kept, so a later update can diff it.</summary>
    [JsonPropertyName("cachedAt")]
    public string? CachedAt { get; init; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; init; }

    [JsonPropertyName("domains")]
    public int Domains { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    /// <summary>Domains this fetch has that the applied copy does not.</summary>
    [JsonPropertyName("added")]
    public int Added { get; init; }

    /// <summary>Domains the applied copy has that this fetch does not.</summary>
    [JsonPropertyName("removed")]
    public int Removed { get; init; }

    [JsonPropertyName("fetchedAt")]
    public DateTimeOffset FetchedAt { get; init; }

    /// <summary>True when nothing has been applied yet, so there is no diff to show.</summary>
    [JsonPropertyName("firstTime")]
    public bool FirstTime { get; init; }
}

/// <summary>What one write to the hosts file or the firewall did.</summary>
public record BlockChange
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>True when the machine was already in the requested state.</summary>
    [JsonPropertyName("unchanged")]
    public bool Unchanged { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>How many managed rules the action touched or left behind.</summary>
    [JsonPropertyName("affected")]
    public int Affected { get; init; }

    /// <summary>Where the file stood before the first write, if a backup was taken.</summary>
    [JsonPropertyName("backup")]
    public string? Backup { get; init; }

    /// <summary>Set when the operation could not proceed without administrator rights.</summary>
    [JsonPropertyName("needsElevation")]
    public bool NeedsElevation { get; init; }

    [JsonPropertyName("log")]
    public string Log { get; init; } = string.Empty;
}

/// <summary>Everything the Blocker page reads in one pass.</summary>
public sealed record BlockerStatus
{
    [JsonPropertyName("hostsPath")]
    public string HostsPath { get; init; } = string.Empty;

    [JsonPropertyName("hostsExists")]
    public bool HostsExists { get; init; }

    /// <summary>True when markers were found but do not pair up, and nothing is safe to write.</summary>
    [JsonPropertyName("hostsMalformed")]
    public bool HostsMalformed { get; init; }

    [JsonPropertyName("managed")]
    public int Managed { get; init; }

    [JsonPropertyName("enabled")]
    public int Enabled { get; init; }

    [JsonPropertyName("unmanaged")]
    public int Unmanaged { get; init; }

    /// <summary>A copy taken before the first Novimize write, if one exists.</summary>
    [JsonPropertyName("backupExists")]
    public bool BackupExists { get; init; }

    [JsonPropertyName("backupPath")]
    public string? BackupPath { get; init; }

    [JsonPropertyName("writable")]
    public bool Writable { get; init; }

    [JsonPropertyName("firewallRules")]
    public int FirewallRules { get; init; }

    [JsonPropertyName("firewallReadable")]
    public bool FirewallReadable { get; init; }

    [JsonPropertyName("sources")]
    public List<BlockSource> Sources { get; init; } = new();

    [JsonPropertyName("applied")]
    public List<AppliedSource> Applied { get; init; } = new();

    [JsonPropertyName("rules")]
    public List<BlockRule> Rules { get; init; } = new();
}

/// <summary>One source that has been applied to the hosts file, and when.</summary>
public sealed record AppliedSource
{
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("domains")]
    public int Domains { get; init; }

    [JsonPropertyName("appliedAt")]
    public DateTimeOffset AppliedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}
