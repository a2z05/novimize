using System.Net;
using System.Net.Sockets;

namespace WinOpt.Engine.Blocker;

/// <summary>What a parse made of a downloaded file.</summary>
public sealed record ParsedList(IReadOnlyList<string> Values, int Lines, int Skipped)
{
    /// <summary>Lines that were neither a comment nor something usable.</summary>
    public int Comments => Lines - Skipped - Values.Count;
}

/// <summary>
/// Reading third-party blocklist text without importing its mistakes.
///
/// A downloaded file is not a list of rules — it is a list of claims, some of
/// which are comments, some of which are not domains at all, and a few of
/// which, if applied literally, would break the machine they landed on. The
/// checks here are the ones that matter: <c>localhost</c> must never become a
/// block, a line that cannot be read must be counted rather than silently
/// dropped, and a wildcard is not a hostname.
/// </summary>
public static class BlocklistFormats
{
    /// <summary>
    /// Names a hosts file may map to the loopback address that must never be
    /// written as a block. Blocking any of them takes the machine off its own
    /// name — printers, dev servers and anything using <c>localhost</c> stop
    /// resolving, and the fix is not obvious from the symptom.
    /// </summary>
    private static readonly HashSet<string> NeverBlock = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "localhost.localdomain", "local", "broadcasthost",
        "ip6-localhost", "ip6-loopback", "ip6-localnet", "ip6-mcastprefix",
        "ip6-allhosts", "ip6-allrouters", "ip6-allnodes", "ip6-allmcasthosts",
        "localhost6", "localhost6.localdomain6", "example.invalid",
    };

    /// <summary>
    /// Hosts-formatted text → the names worth writing as blocks.
    /// Accepts <c>0.0.0.0 name</c>, <c>127.0.0.1 name</c> and a bare
    /// <c>name</c>, with an optional trailing comment.
    /// </summary>
    public static ParsedList ParseDomains(string text)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = 0;
        var skipped = 0;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            lines++;
            if (line[0] is '#' or ';') continue;

            // Drop a trailing comment before splitting: "# tracker" must not
            // become a domain called "# tracker".
            var comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment].TrimEnd();
            if (line.Length == 0) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // Two fields means "address name"; more than two means a name list.
            // One field means the file writes bare domains, which is legal.
            var names = parts.Length >= 2 && IsAddressOrCidr(parts[0]) ? parts[1..] : parts;

            foreach (var part in names)
            {
                var domain = NormalizeDomain(part);
                if (domain is null)
                {
                    skipped++;
                    continue;
                }
                if (seen.Add(domain)) values.Add(domain);
            }
        }

        return new ParsedList(values, lines, skipped);
    }

    /// <summary>
    /// Address-set text → IPs and CIDR ranges, for a firewall rule.
    /// Hosts-formatted files are accepted too, since they carry addresses.
    /// </summary>
    public static ParsedList ParseAddresses(string text)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = 0;
        var skipped = 0;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            lines++;
            if (line[0] is '#' or ';') continue;

            var comment = line.IndexOfAny(new[] { '#', ';' });
            if (comment >= 0) line = line[..comment].TrimEnd();
            if (line.Length == 0) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var token = parts[0];

            // "0.0.0.0 example.com" in a file we are reading for addresses:
            // take the address, ignore the name.
            if (parts.Length >= 2 && IsAddressOrCidr(parts[0]) && !IsAddressOrCidr(parts[1]))
                token = parts[0];

            var normalized = NormalizeAddress(token);
            if (normalized is null)
            {
                skipped++;
                continue;
            }
            if (seen.Add(normalized)) values.Add(normalized);
        }

        return new ParsedList(values, lines, skipped);
    }

    /// <summary>
    /// A domain that is safe to write into a hosts file, or null.
    ///
    /// Refuses: the loopback names, anything without a dot, anything that is
    /// an address rather than a name, uppercase-only and malformed labels, and
    /// wildcards — a <c>*.example.com</c> entry is an adblock expression, not
    /// a hosts line, and writing it verbatim blocks a host that does not exist
    /// while leaving the real one alone.
    /// </summary>
    public static string? NormalizeDomain(string token)
    {
        var value = token.Trim().TrimEnd('.').ToLowerInvariant();
        if (value.Length == 0 || value.Length > 253) return null;
        if (value.Contains('*') || value.Contains('/') || value.Contains(':')) return null;
        if (!value.Contains('.')) return null;
        if (NeverBlock.Contains(value)) return null;
        if (IPAddress.TryParse(value, out _)) return null;

        foreach (var label in value.Split('.'))
        {
            if (label.Length is 0 or > 63) return null;
            if (label[0] == '-' || label[^1] == '-') return null;
            foreach (var c in label)
                if (c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
                    return null;
        }
        return value;
    }

    /// <summary>A single IP or CIDR range in canonical form, or null.</summary>
    public static string? NormalizeAddress(string token)
    {
        var value = token.Trim();
        if (value.Length == 0) return null;

        var slash = value.IndexOf('/');
        var host = slash < 0 ? value : value[..slash];

        if (!IPAddress.TryParse(host, out var address)) return null;

        if (slash >= 0)
        {
            if (!int.TryParse(value[(slash + 1)..], out var prefix)) return null;
            var max = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            if (prefix < 0 || prefix > max) return null;
            return $"{address}/{prefix}";
        }

        return address.ToString();
    }

    public static bool IsAddressOrCidr(string token) => NormalizeAddress(token) is not null;

    /// <summary>
    /// The difference between two sets, counted. Used to tell the user what a
    /// refresh would change before it changes it.
    /// </summary>
    public static (int Added, int Removed) Diff(IReadOnlyCollection<string> before, IReadOnlyCollection<string> after)
    {
        var afterSet = new HashSet<string>(after, StringComparer.OrdinalIgnoreCase);
        var beforeSet = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        return (afterSet.Except(beforeSet).Count(), beforeSet.Except(afterSet).Count());
    }
}
