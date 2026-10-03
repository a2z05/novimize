using System.Text.Json;
using System.Text.Json.Serialization;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Debloat;

/// <summary>
/// The allowlist and denylist for the Debloat Center.
///
/// It is a file rather than code because it is an opinion that will change:
/// a package Microsoft retires, or one a build starts depending on, is a one
/// line edit here rather than a release. Everything the file says is a
/// recommendation; the things that are facts — Windows marking a package
/// non-removable, a framework, another package waiting on it — are decided by
/// the engine and cannot be overridden by the file.
/// </summary>
public sealed class DebloatPolicy
{
    public const string FileName = "policy.json";

    private readonly Dictionary<string, DebloatEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public string Directory { get; }
    public IReadOnlyDictionary<string, DebloatEntry> Entries => _entries;

    public DebloatPolicy(string directory) => Directory = directory;

    /// <summary>Same resolution order as the tweak, app and blocklist catalogues.</summary>
    public static string ResolveDirectory()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "debloat");
        if (System.IO.Directory.Exists(portable)) return portable;

        var bundled = Path.Combine(AppContext.BaseDirectory, "resources", "debloat");
        if (System.IO.Directory.Exists(bundled)) return bundled;

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "debloat");
            if (System.IO.Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return portable;
    }

    public int Load()
    {
        _entries.Clear();
        var path = Path.Combine(Directory, FileName);
        if (!File.Exists(path)) return 0;

        try
        {
            var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("entries", out var inner) ? inner : default;

            if (array.ValueKind != JsonValueKind.Array) return 0;

            foreach (var element in array.EnumerateArray())
            {
                try
                {
                    var entry = element.Deserialize<DebloatEntry>(new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new JsonStringEnumConverter() },
                    });
                    if (entry is not null && !string.IsNullOrWhiteSpace(entry.Id))
                        _entries[entry.Id] = entry;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Skipped an unreadable debloat entry in {path}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load {path}: {ex.Message}");
        }

        return _entries.Count;
    }

    public DebloatEntry? Find(string id) =>
        _entries.TryGetValue(id, out var entry) ? entry : null;
}

/// <summary>
/// Installed Store packages, and what it is safe to do with them.
///
/// Two independent things decide whether a package can go. The first is the
/// engine's own check: Windows marks packages non-removable, frameworks are
/// what other packages are built on, and a package something else is waiting
/// on takes that something with it. The second is the policy file, which is an
/// opinion and can be edited. The engine's check always wins, and it is
/// reported as a refusal with a reason rather than as the row quietly not
/// being offered — a list that silently omits half the machine is a list
/// nobody can trust.
///
/// Nothing here removes a package without saying which one first, and every
/// removal records whether a copy exists to put it back.
/// </summary>
public sealed class DebloatManager
{
    private readonly DebloatPolicy _policy;

    public DebloatManager(string? policyDirectory = null)
    {
        _policy = new DebloatPolicy(policyDirectory ?? DebloatPolicy.ResolveDirectory());
        _policy.Load();
    }

    public DebloatPolicy Policy => _policy;

    public async Task<DebloatStatus> ReadAsync(bool allUsers = false, CancellationToken cancel = default)
    {
        var raw = await RunAsync(new { op = "read", allUsers }, cancel);
        var read = raw is null ? null : JsonSerializer.Deserialize<ReadResult>(raw, Raw);
        if (read?.Packages is null)
            return new DebloatStatus
            {
                PolicyPath = Path.Combine(_policy.Directory, DebloatPolicy.FileName),
                PolicyEntries = _policy.Entries.Count,
                Error = read?.Error ?? "The package list could not be read.",
            };

        // Who depends on whom: the removal decision needs the reverse edge,
        // and PowerShell gives the forward one.
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in read.Packages)
        foreach (var dependency in package.Dependencies ?? new())
        {
            // AppX lists a package's own related packages among its
            // dependencies, so without this every package would report itself
            // as waiting on itself and nothing would ever be removable.
            if (package.Name is not null
                && dependency.Equals(package.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!dependents.TryGetValue(dependency, out var list))
                dependents[dependency] = list = new();
            if (package.Name is not null && !list.Contains(package.Name)) list.Add(package.Name);
        }

        var provisioned = new HashSet<string>(
            read.Provisioned ?? new(), StringComparer.OrdinalIgnoreCase);

        var packages = read.Packages
            .Select(p => ToPackage(p, dependents, provisioned))
            .OrderBy(p => p.Verdict)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DebloatStatus
        {
            Packages = packages,
            Removable = packages.Count(p => p.Removable),
            ProtectedCount = packages.Count(p => !p.Removable),
            Frameworks = packages.Count(p => p.IsFramework),
            Scope = allUsers ? "Machine" : "User",
            PolicyPath = Path.Combine(_policy.Directory, DebloatPolicy.FileName),
            PolicyEntries = _policy.Entries.Count,
            Error = read.Error,
        };
    }

    private DebloatPackage ToPackage(
        RawPackage raw,
        Dictionary<string, List<string>> dependents,
        HashSet<string> provisioned)
    {
        var name = raw.Name ?? string.Empty;
        var entry = _policy.Find(name);
        var verdict = entry?.Verdict ?? DebloatVerdict.Unknown;

        // The engine's checks come first and the policy file cannot override
        // them: an opinion that a package is safe does not make Windows
        // willing to remove it, nor does it put the thing that depends on it
        // back.
        string? refusal;
        if (raw.IsFramework)
            refusal = "a framework package — other packages are built on it.";
        else if (raw.NonRemovable)
            refusal = "Windows marks this package non-removable.";
        else if (dependents.TryGetValue(name, out var waiting) && waiting.Count > 0)
            refusal = $"required by {string.Join(", ", waiting.Take(5))}"
                      + (waiting.Count > 5 ? $" and {waiting.Count - 5} more" : "") + ".";
        // The policy's "protected" verdict is enforced here rather than left
        // to the caller, so it reads as a refusal with a reason on the row
        // instead of a button that silently fails after the click.
        else if (verdict == DebloatVerdict.Protected)
            refusal = string.IsNullOrWhiteSpace(entry?.Reason)
                ? "The debloat policy marks this protected."
                : entry!.Reason;
        else
            refusal = null;

        var reason = refusal ?? entry?.Reason ?? string.Empty;

        return new DebloatPackage
        {
            Name = name,
            FullName = raw.PackageFullName ?? string.Empty,
            Publisher = CleanPublisher(raw.Publisher),
            Scope = raw.AllUsers ? "Machine" : "User",
            Version = raw.Version ?? string.Empty,
            IsFramework = raw.IsFramework,
            NonRemovable = raw.NonRemovable,
            DependedOnBy = dependents.TryGetValue(name, out var list) ? list : new(),
            InstalledLocation = string.IsNullOrWhiteSpace(raw.InstallLocation) ? null : raw.InstallLocation,
            Provisioned = provisioned.Contains(name),
            Verdict = verdict,
            Reason = reason,
            EngineRefused = refusal is not null,
            RefusalReason = refusal,
        };
    }

    /// <summary>The policy file stores a display name; the certificate subject is not one.</summary>
    private static string CleanPublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return string.Empty;
        var text = publisher;
        var organization = System.Text.RegularExpressions.Regex.Match(text, @"O=([^,]+)");
        if (organization.Success) return organization.Groups[1].Value.Trim();
        return text.Length > 60 ? text[..60] + "…" : text;
    }

    // --- changing ----------------------------------------------------------

    public async Task<DebloatChange> RemoveAsync(
        string name, bool allUsers, bool confirm, CancellationToken cancel = default)
    {
        var status = await ReadAsync(allUsers, cancel);
        var package = status.Packages.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (package is null)
            return Fail("remove", $"No installed package called '{name}'.");

        var commands = new List<string>
        {
            allUsers
                ? $"Remove-AppxPackage -Package '{package.FullName}' -AllUsers"
                : $"Remove-AppxPackage -Package '{package.FullName}'",
        };
        if (package.Provisioned)
            commands.Add($"Add-AppxPackage -Register '{package.InstalledLocation}\\AppxManifest.xml' -DisableDevelopmentMode   (put it back)");

        var preview = new DebloatChange
        {
            Action = "remove",
            Success = true,
            Affected = 1,
            Message = $"{package.Name} would be removed for {(allUsers ? "every user" : "you")}.",
            Preview = commands,
        };

        if (package.EngineRefused)
            return preview with { Success = false, Message = $"{package.Name} will not be removed: {package.RefusalReason}" };

        if (package.Verdict == DebloatVerdict.Protected)
            return preview with
            {
                Success = false,
                Message = $"{package.Name} will not be removed: {package.Reason}",
            };

        if (!confirm)
            return preview with
            {
                Success = false,
                Message = package.Provisioned
                    ? $"{package.Name} would be removed. A provisioned copy exists, so it can be registered again. Nothing has been run."
                    : $"{package.Name} would be removed. A provisioned copy could not be confirmed — " +
                      "reading those needs administrator rights — so it may have to come back from " +
                      "the Store. Nothing has been run.",
            };

        if (allUsers && !Elevation.IsElevated())
            return preview with
            {
                Success = false,
                NeedsElevation = true,
                Message = $"Removing a package for every user needs administrator rights, so nothing was changed.",
            };

        var raw = await RunAsync(new
        {
            op = "remove",
            fullName = package.FullName,
            name = package.Name,
            allUsers,
        }, cancel);

        var read = raw is null ? null : JsonSerializer.Deserialize<ChangeRead>(raw, Raw);
        if (read is null)
            return Fail("remove", $"{package.Name} was not removed; PowerShell did not answer.");

        return new DebloatChange
        {
            Action = "remove",
            Success = read.Success,
            Affected = read.Success ? 1 : 0,
            Message = read.Success
                ? $"{package.Name} was removed for {(allUsers ? "every user" : "you")}."
                  + (package.Provisioned
                      ? " A provisioned copy remains, so it can be registered again."
                      : " If a provisioned copy remains it can be registered again; otherwise the " +
                        "Store is where it comes back from.")
                : read.Message ?? $"{package.Name} could not be removed.",
            Preview = commands,
            Log = read.Log,
            RestartRequired = read.Success,
            NeedsElevation = read.Message?.Contains("administrator", StringComparison.OrdinalIgnoreCase) == true,
        };
    }

    /// <summary>
    /// Put a removed package back: re-register the provisioned copy when there
    /// is one, which needs no download, and otherwise say where it has to come
    /// from rather than reaching for an installer nobody chose.
    /// </summary>
    public async Task<DebloatChange> RestoreAsync(string name, bool allUsers, bool confirm, CancellationToken cancel = default)
    {
        var status = await ReadAsync(allUsers, cancel);
        var package = status.Packages.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (package is null)
            return Fail("restore", $"No installed package called '{name}'. It may need to be reinstalled from the Microsoft Store.");

        var commands = new List<string>
        {
            allUsers
                ? $"Add-AppxProvisionedPackage -Online -PackageName '{package.Name}'"
                : $"Add-AppxPackage -Register '{package.InstalledLocation}\\AppxManifest.xml' -DisableDevelopmentMode",
        };

        if (!confirm)
            return new DebloatChange
            {
                Action = "restore",
                Success = false,
                Affected = 1,
                Message = $"{package.Name} would be registered again. Nothing has been run.",
                Preview = commands,
            };

        if (package.InstalledLocation is null)
            return new DebloatChange
            {
                Action = "restore",
                Success = false,
                Message = $"{package.Name} has no install location to register from. Reinstall it from the Microsoft Store.",
                Preview = commands,
            };

        var raw = await RunAsync(new
        {
            op = "restore",
            fullName = package.FullName,
            name = package.Name,
            location = package.InstalledLocation,
            allUsers,
        }, cancel);

        var read = raw is null ? null : JsonSerializer.Deserialize<ChangeRead>(raw, Raw);
        if (read is null)
            return Fail("restore", $"{package.Name} was not restored; PowerShell did not answer.");

        return new DebloatChange
        {
            Action = "restore",
            Success = read.Success,
            Affected = read.Success ? 1 : 0,
            Message = read.Success
                ? $"{package.Name} was registered again."
                : read.Message ?? $"{package.Name} could not be registered.",
            Preview = commands,
            Log = read.Log,
            RestartRequired = read.Success,
        };
    }

    private static DebloatChange Fail(string action, string message) => new()
    {
        Action = action,
        Success = false,
        Message = message,
    };

    // --- PowerShell ---------------------------------------------------------

    private static readonly JsonSerializerOptions Raw = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<string?> RunAsync(object payload, CancellationToken cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"novimize-debloat-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(directory, "payload.json");
        var scriptPath = Path.Combine(directory, "debloat.ps1");
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
            }, TimeSpan.FromSeconds(180));

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
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $p = Get-Content -LiteralPath $Payload -Raw -Encoding UTF8 | ConvertFrom-Json

        function Reply([object]$body) { $body | ConvertTo-Json -Depth 8 -Compress }

        try {
            switch ($p.op) {

                'read' {
                    $scope = if ($p.allUsers) { 'AllUsers' } else { 'CurrentUser' }
                    $rows = @()
                    foreach ($x in @(Get-AppxPackage -ErrorAction SilentlyContinue)) {
                        $deps = @()
                        try { $deps = @($x.Dependencies | ForEach-Object { [string]$_.Name }) } catch { }

                        $all = $false
                        try {
                            $other = Get-AppxPackage -AllUsers -Name $x.Name -ErrorAction SilentlyContinue
                            $all = [bool]$other
                        } catch { }

                        $rows += [pscustomobject]@{
                            name            = [string]$x.Name
                            packageFullName = [string]$x.PackageFullName
                            publisher       = [string]$x.Publisher
                            version         = [string]$x.Version
                            isFramework     = [bool]$x.IsFramework
                            nonRemovable    = [bool]$x.NonRemovable
                            installLocation = [string]$x.InstallLocation
                            allUsers        = $all
                            dependencies    = $deps
                        }
                    }

                    $provisioned = @()
                    try {
                        $provisioned = @(Get-AppxProvisionedPackage -Online -ErrorAction Stop |
                            ForEach-Object { [string]$_.DisplayName })
                    } catch { }

                    # A package that was removed is no longer in Get-AppxPackage,
                    # so Restore would have nothing to act on. The provisioned
                    # copy is what it comes back from, and listing it is the
                    # only way "put it back" is a button rather than a memory.
                    $installed = @{}
                    foreach ($r in $rows) { $installed[[string]$r.name] = $true }
                    try {
                        foreach ($prov in @(Get-AppxProvisionedPackage -Online -ErrorAction Stop)) {
                            $n = [string]$prov.DisplayName
                            if (-not $n -or $installed.ContainsKey($n)) { continue }
                            $rows += [pscustomobject]@{
                                name            = $n
                                packageFullName = [string]$prov.PackageName
                                publisher       = [string]$prov.Publisher
                                version         = [string]$prov.Version
                                isFramework     = $false
                                nonRemovable    = $false
                                installLocation = [string]$prov.InstallLocation
                                allUsers        = $true
                                dependencies    = @()
                            }
                        }
                    } catch { }

                    Reply @{ packages = $rows; provisioned = $provisioned; scope = $scope; error = $null }
                    exit
                }

                'remove'  { $desired = 'remove' }
                'restore' { $desired = 'restore' }
                default   { Reply @{ success = $false; message = 'Unknown operation.' }; exit }
            }

            $full = [string]$p.fullName
            $name = [string]$p.name

            if ($desired -eq 'remove') {
                if ($p.allUsers) {
                    Remove-AppxPackage -Package $full -AllUsers -ErrorAction Stop
                } else {
                    Remove-AppxPackage -Package $full -ErrorAction Stop
                }
            } else {
                $location = [string]$p.location
                if (-not $location -or -not (Test-Path -LiteralPath $location)) {
                    Reply @{ success = $false; message = ($name + ' has no install location left to register from.') }
                    exit
                }
                $manifest = Join-Path $location 'AppxManifest.xml'
                if (-not (Test-Path -LiteralPath $manifest)) {
                    Reply @{ success = $false; message = ($name + ' has no AppxManifest.xml to register from.') }
                    exit
                }
                Add-AppxPackage -Register $manifest -DisableDevelopmentMode -ErrorAction Stop
            }

            Reply @{ success = $true; message = 'ok' }
        } catch {
            Reply @{ success = $false; message = [string]$_.Exception.Message }
        }
        """;

    private sealed class ReadResult
    {
        public List<RawPackage>? Packages { get; init; }
        public List<string>? Provisioned { get; init; }
        public string? Scope { get; init; }
        public string? Error { get; init; }
    }

    private sealed class ChangeRead
    {
        public bool Success { get; init; }
        public string? Message { get; init; }
        public string? Log { get; init; }
    }
}

/// <summary>One raw row out of the reader, before it is interpreted.</summary>
public sealed class RawPackage
{
    public string? Name { get; init; }
    public string? PackageFullName { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public bool IsFramework { get; init; }
    public bool NonRemovable { get; init; }
    public string? InstallLocation { get; init; }
    public bool AllUsers { get; init; }
    public List<string>? Dependencies { get; init; }
}
