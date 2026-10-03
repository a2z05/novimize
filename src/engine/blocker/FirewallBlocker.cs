using System.Text;
using System.Text.Json;
using WinOpt.Core.Models;
using WinOpt.Engine;

namespace WinOpt.Engine.Blocker;

/// <summary>
/// Windows Firewall rules that Novimize owns, and nothing else.
///
/// The whole safety story here is one prefix. Every rule this class creates is
/// named <c>Novimize.Block.&lt;id&gt;</c> and filed under the group
/// <c>Novimize</c>, so "ours" is answerable from the rule itself rather than
/// from a list that can drift out of step with what is on the machine. Reads
/// filter on that prefix; deletes filter on that prefix; a rule the user made
/// by hand is never enumerated as removable, let alone removed.
///
/// The engine drives the firewall through the documented <c>NetSecurity</c>
/// cmdlets rather than <c>netsh</c>, because <c>netsh advfirewall</c> prints
/// prose that changes with the display language, and a parser that only works
/// in English fails silently somewhere it cannot report. PowerShell is handed
/// the payload as a file and answers as JSON, so no address or path is ever
/// interpolated into a command line that something else has to read back.
/// </summary>
public sealed class FirewallBlocker
{
    /// <summary>Display group stamped on every rule this class writes.</summary>
    public const string Group = "Novimize";

    /// <summary>Rule-name prefix; the id follows it verbatim.</summary>
    public const string Prefix = "Novimize.Block.";

    /// <summary>
    /// Addresses per firewall rule. One rule can carry a list, but a rule with
    /// ten thousand addresses is slow to open in the MMC snap-in and slow for
    /// Windows to evaluate, so large sets are split into numbered rules.
    /// </summary>
    public const int AddressChunk = 500;

    /// <summary>Where full address sets are kept between runs.</summary>
    public string SetDirectory { get; }

    public FirewallBlocker(string? setDirectory = null)
    {
        SetDirectory = setDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "blocker", "ips");
    }

    /// <summary>
    /// The stable name for a rule id. Ids are restricted to the characters
    /// that survive this round trip, so stripping the prefix always recovers
    /// the id the caller gave — the alternative is a hash, and a rule the user
    /// cannot match back to their own list is a rule they cannot manage.
    /// </summary>
    public static string RuleName(string id) => Prefix + id;

    /// <summary>
    /// Whether a rule name is Novimize's. A name that carries the prefix but
    /// no id is not a rule this class can act on, so it counts as somebody
    /// else's — calling it ours would hand back an empty id to every caller.
    /// </summary>
    public static bool IsManaged(string? name) =>
        name is not null
        && name.Length > Prefix.Length
        && name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The id of a managed rule, or null when it is not one of ours.</summary>
    public static string? IdOf(string? name) =>
        IsManaged(name) ? name![Prefix.Length..] : null;

    /// <summary>False for anything that would not round-trip through a rule name.</summary>
    public static bool IsSafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 120) return false;
        foreach (var c in id)
        {
            var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                or '.' or '_' or '-';
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>
    /// What the machine's firewall says about the rules this group owns.
    /// Reading does not need administrator rights; writing does.
    /// </summary>
    public async Task<FirewallRead> ReadAsync(CancellationToken cancel = default)
    {
        var result = await RunAsync(new { op = "read" }, cancel);
        if (result is null)
            return new FirewallRead { Rules = new(), Error = "The firewall could not be queried." };

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(error.GetString()))
            return new FirewallRead { Rules = new(), Error = error.GetString() };

        var rules = new List<FirewallEntry>();
        if (root.TryGetProperty("rules", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
                rules.Add(ReadEntry(item));
        }
        return new FirewallRead { Rules = rules };
    }

    private static FirewallEntry ReadEntry(JsonElement item)
    {
        static string Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var id = IdOf(Text(item, "name")) ?? Text(item, "name");
        var remote = new List<string>();
        if (item.TryGetProperty("remote", out var addresses) && addresses.ValueKind == JsonValueKind.Array)
            foreach (var a in addresses.EnumerateArray())
                if (a.ValueKind == JsonValueKind.String)
                    remote.Add(a.GetString() ?? "");

        return new FirewallEntry
        {
            Id = id,
            Name = Text(item, "name"),
            Description = NullIfEmpty(Text(item, "description")),
            Enabled = item.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.True,
            Direction = Text(item, "direction"),
            Action = Text(item, "action"),
            Program = NullIfEmpty(Text(item, "program")),
            RemoteAddresses = remote,
        };
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    // --- changes -----------------------------------------------------------

    /// <summary>Block one executable from reaching the network.</summary>
    public Task<BlockChange> BlockProgramAsync(
        string id, string program, BlockCategory category, string? source, string? purpose,
        BlockSeverity severity = BlockSeverity.Low, CancellationToken cancel = default)
    {
        if (!IsSafeId(id))
            return Task.FromResult(Error("write", $"'{id}' is not a usable rule id: letters, digits, dot, dash and underscore only."));
        if (string.IsNullOrWhiteSpace(program))
            return Task.FromResult(Error("write", "No program path was given to block."));

        return ChangeAsync("block-program", new
        {
            op = "add-program",
            id,
            program,
            description = Describe(category, severity, source, purpose),
        }, cancel);
    }

    /// <summary>Block a set of addresses, split across as many rules as it needs.</summary>
    public async Task<BlockChange> BlockAddressesAsync(
        string id, IReadOnlyList<string> addresses, BlockCategory category, string? source, string? purpose,
        BlockSeverity severity = BlockSeverity.Low, CancellationToken cancel = default)
    {
        if (!IsSafeId(id))
            return Error("block-addresses", $"'{id}' is not a usable rule id: letters, digits, dot, dash and underscore only.");
        if (addresses.Count == 0)
            return Error("block-addresses", "No addresses were given to block.");

        var written = new List<string>();
        for (var offset = 0; offset < addresses.Count; offset += AddressChunk)
        {
            var chunk = addresses.Skip(offset).Take(AddressChunk).ToList();
            var name = offset == 0 ? id : $"{id}.{offset / AddressChunk}";
            var change = await ChangeAsync("block-addresses", new
            {
                op = "add-addresses",
                id = name,
                addresses = chunk,
                description = Describe(category, severity, source, purpose),
            }, cancel);
            if (!change.Success) return change;
            written.Add(name);
        }

        // The set is kept beside the rules so a later update knows what it
        // wrote, and so the page can show the list without asking Windows.
        try
        {
            Directory.CreateDirectory(SetDirectory);
            File.WriteAllLines(Path.Combine(SetDirectory, id + ".ips"), addresses);
        }
        catch (Exception ex)
        {
            return Error("block-addresses",
                $"The rules were written ({written.Count} of them) but the address set could not be saved: {ex.Message}",
                affected: written.Count);
        }

        return new BlockChange
        {
            Action = "block-addresses",
            Success = true,
            Affected = written.Count,
            Message = $"Blocked {addresses.Count} address{(addresses.Count == 1 ? "" : "es")} across {written.Count} firewall rule{(written.Count == 1 ? "" : "s")}.",
        };
    }

    /// <summary>Turn one rule, or a whole id's worth of chunks, on or off.</summary>
    public Task<BlockChange> SetEnabledAsync(string id, bool enabled, CancellationToken cancel = default)
        => ChangeAsync(enabled ? "enable" : "disable", new { op = "set-enabled", id, enabled }, cancel);

    /// <summary>Remove the rules for one id, leaving every other rule alone.</summary>
    public async Task<BlockChange> RemoveAsync(string id, CancellationToken cancel = default)
    {
        var change = await ChangeAsync("remove", new { op = "remove-prefix", id }, cancel);
        TryDeleteSet(id);
        return change;
    }

    /// <summary>
    /// One-click rollback: every rule with the Novimize prefix, and only those.
    /// </summary>
    public async Task<BlockChange> RemoveAllAsync(CancellationToken cancel = default)
    {
        var change = await ChangeAsync("remove-all", new { op = "remove-all" }, cancel);
        try
        {
            if (Directory.Exists(SetDirectory)) Directory.Delete(SetDirectory, recursive: true);
        }
        catch { /* the rules are gone either way; a leftover file is not a hazard */ }
        return change;
    }

    private void TryDeleteSet(string id)
    {
        try
        {
            foreach (var file in Directory.Exists(SetDirectory)
                         ? Directory.GetFiles(SetDirectory, "*.ips")
                         : Array.Empty<string>())
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Equals(id, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
        }
        catch { /* a file that cannot be deleted is not a blocked rule */ }
    }

    /// <summary>
    /// The description stamped on a rule, and the parser that reads it back.
    ///
    /// The firewall has nowhere to keep structured metadata, so the provenance
    /// rides in the description — which is the right place anyway: open the
    /// rule in the MMC snap-in with Novimize uninstalled and it still says who
    /// wrote it, what it is for, and how much damage it can do if it is wrong.
    /// The format is fixed and split on <see cref="DescSeparator"/>; anything
    /// that does not parse reads as an ordinary custom rule rather than
    /// failing, so a description edited by hand never breaks the page.
    /// </summary>
    public const string DescSeparator = " — ";

    public static string Describe(BlockCategory category, BlockSeverity severity, string? source, string? purpose) =>
        string.Join(DescSeparator,
            "Novimize",
            string.IsNullOrWhiteSpace(source) ? "custom" : source,
            category.ToString(),
            severity.ToString(),
            string.IsNullOrWhiteSpace(purpose) ? "-" : purpose);

    public static void ParseDescription(string? description,
        out BlockCategory category, out BlockSeverity severity, out string source, out string? purpose)
    {
        category = BlockCategory.Custom;
        severity = BlockSeverity.Low;
        source = "custom";
        purpose = null;
        if (string.IsNullOrWhiteSpace(description)) return;

        var parts = description.Split(DescSeparator, StringSplitOptions.None);
        if (parts.Length < 4 || !parts[0].Equals("Novimize", StringComparison.Ordinal)) return;

        if (!string.IsNullOrWhiteSpace(parts[1]) && parts[1] != "-") source = parts[1];
        if (Enum.TryParse(parts[2], ignoreCase: true, out BlockCategory parsed)) category = parsed;
        if (Enum.TryParse(parts[3], ignoreCase: true, out BlockSeverity sev)) severity = sev;
        if (parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]) && parts[4] != "-") purpose = parts[4];
    }

    private static BlockChange Error(string action, string message, int affected = 0) => new()
    {
        Action = action,
        Success = false,
        Message = message,
        Affected = affected,
    };

    private async Task<BlockChange> ChangeAsync(string action, object payload, CancellationToken cancel)
    {
        if (!Elevation.IsElevated())
            return new BlockChange
            {
                Action = action,
                Success = false,
                NeedsElevation = true,
                Message = "Changing firewall rules needs administrator rights.",
            };

        var output = await RunAsync(payload, cancel);
        if (output is null)
            return Error(action, "PowerShell did not produce an answer, so nothing can be claimed about the firewall.");

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(error.GetString()))
            return Error(action, error.GetString()!);

        return new BlockChange
        {
            Action = action,
            Success = true,
            Affected = root.TryGetProperty("affected", out var count) && count.TryGetInt32(out var n) ? n : 1,
            Message = root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                ? message.GetString() ?? "Done."
                : "Done.",
        };
    }

    // --- talking to PowerShell --------------------------------------------

    /// <summary>
    /// Hand PowerShell a payload file and read its JSON back.
    ///
    /// Everything else in the codebase builds a command line, and for
    /// netsh-style verbs that is fine because the arguments are flags. Here the
    /// payload is an executable path and thousands of addresses, and a
    /// mis-quoted one of those does not fail — it writes the wrong rule. A file
    /// cannot be mis-quoted.
    /// </summary>
    private async Task<string?> RunAsync(object payload, CancellationToken cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"novimize-fw-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(directory, "payload.json");
        var scriptPath = Path.Combine(directory, "firewall.ps1");
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

            var stdout = result.StdOut.Trim();
            if (stdout.Length == 0) return null;
            // Anything printed before the JSON (a profile banner, a warning)
            // would make the document unparseable, so start at the first brace.
            var start = stdout.IndexOf('{');
            return start < 0 ? null : stdout[start..];
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* temp */ }
        }
    }

    /// <summary>
    /// The whole firewall surface: one script, one payload, one JSON answer.
    /// Kept in one place so a rule can be created and read back through the
    /// same naming and group conventions without two implementations to keep
    /// in step.
    /// </summary>
    private const string Script = """
        param([string]$Payload)

        $ErrorActionPreference = 'Stop'
        $p = Get-Content -LiteralPath $Payload -Raw -Encoding UTF8 | ConvertFrom-Json
        $group = 'Novimize'
        $prefix = 'Novimize.Block.'

        function Reply([object]$body) {
            $body | ConvertTo-Json -Depth 8 -Compress
        }

        try {
            switch ($p.op) {
                'read' {
                    $rows = @(
                        Get-NetFirewallRule -ErrorAction SilentlyContinue |
                            Where-Object { $_.Group -eq $group -or $_.Name -like ($prefix + '*') } |
                            ForEach-Object {
                                $r = $_
                                $app = $r | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue
                                $addr = $r | Get-NetFirewallAddressFilter -ErrorAction SilentlyContinue
                                [pscustomobject]@{
                                    name        = [string]$r.Name
                                    display     = [string]$r.DisplayName
                                    description = [string]$r.Description
                                    group       = [string]$r.Group
                                    enabled     = ($r.Enabled.ToString() -eq 'True')
                                    direction   = $r.Direction.ToString()
                                    action      = $r.Action.ToString()
                                    program     = [string]$app.Program
                                    remote      = @(@($addr.RemoteAddress) | ForEach-Object { [string]$_ })
                                }
                            }
                    )
                    Reply @{ rules = $rows; error = $null }
                }

                'add-program' {
                    $name = $prefix + $p.id
                    Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
                    New-NetFirewallRule -Name $name -DisplayName $name -Group $group `
                        -Description ([string]$p.description) `
                        -Direction Outbound -Action Block -Enabled True -Profile Any `
                        -Program ([string]$p.program) | Out-Null
                    Reply @{ affected = 1; message = ('Blocked ' + $p.program + ' from reaching the network.'); error = $null }
                }

                'add-addresses' {
                    $name = $prefix + $p.id
                    Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
                    New-NetFirewallRule -Name $name -DisplayName $name -Group $group `
                        -Description ([string]$p.description) `
                        -Direction Outbound -Action Block -Enabled True -Profile Any `
                        -RemoteAddress @($p.addresses) | Out-Null
                    Reply @{ affected = 1; message = ('Blocked ' + @($p.addresses).Count + ' address(es).'); error = $null }
                }

                'set-enabled' {
                    $targets = @(Get-NetFirewallRule -ErrorAction SilentlyContinue |
                        Where-Object { [string]$_.Name -like ($prefix + $p.id + '*') })
                    foreach ($r in $targets) {
                        if ($p.enabled) { $r | Enable-NetFirewallRule } else { $r | Disable-NetFirewallRule }
                    }
                    $verb = if ($p.enabled) { 'enabled' } else { 'disabled' }
                    Reply @{ affected = $targets.Count; message = ('Rule set ' + $verb + '.'); error = $null }
                }

                'remove-prefix' {
                    $targets = @(Get-NetFirewallRule -ErrorAction SilentlyContinue |
                        Where-Object { [string]$_.Name -like ($prefix + $p.id + '*') })
                    foreach ($r in $targets) { Remove-NetFirewallRule -Name $r.Name }
                    Reply @{ affected = $targets.Count; message = ('Removed ' + $targets.Count + ' rule(s).'); error = $null }
                }

                'remove-all' {
                    $targets = @(Get-NetFirewallRule -ErrorAction SilentlyContinue |
                        Where-Object { [string]$_.Name -like ($prefix + '*') })
                    foreach ($r in $targets) { Remove-NetFirewallRule -Name $r.Name }
                    Reply @{ affected = $targets.Count; message = ('Removed ' + $targets.Count + ' Novimize firewall rule(s).'); error = $null }
                }

                default {
                    Reply @{ error = ('Unknown firewall operation ' + $p.op); affected = 0 }
                }
            }
        }
        catch {
            Reply @{ error = $_.Exception.Message; affected = 0 }
        }
        """;
}

/// <summary>What the firewall read returned.</summary>
public sealed record FirewallRead
{
    public List<FirewallEntry> Rules { get; init; } = new();
    public string? Error { get; init; }
}

/// <summary>One Novimize-owned firewall rule, as Windows reports it.</summary>
public sealed record FirewallEntry
{
    /// <summary>The id after the prefix — the same id the caller passed in.</summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Enabled { get; init; }
    public string Direction { get; init; } = "Outbound";
    public string Action { get; init; } = "Block";
    public string? Program { get; init; }
    public List<string> RemoteAddresses { get; init; } = new();
}
