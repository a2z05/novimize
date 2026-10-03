using System.Text.Json;
using System.Text.Json.Serialization;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Services;

/// <summary>
/// Windows services: what they are, what needs them, and what Novimize will
/// not touch.
///
/// Two rules govern every write. A service on the protected list is refused
/// with the reason attached — not silently skipped, because a control that
/// does nothing without saying so is worse than no control. And a service
/// other services are waiting on is refused by name, because stopping it
/// stops them too and the user should be told which ones before the click
/// rather than after.
///
/// Nothing here is ever mass-disabled, and the start mode recorded before the
/// first change is what "restore" puts back.
/// </summary>
public sealed class ServiceManager
{
    /// <summary>
    /// Services Novimize refuses to stop or change the start mode of. The
    /// string is the reason it is refused, and it is shown to the user.
    /// </summary>
    private static readonly Dictionary<string, string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Windefend"] = "Windows Defender's real-time protection. Turning that off is a decision this app does not make for you.",
        ["mpssvc"] = "Windows Defender Firewall. The Blocker can add rules to it; it cannot be switched off from here.",
        ["wscsvc"] = "Security Center, which reports whether the protections above are actually on.",
        ["SecurityHealthService"] = "The Security Health tray that reports Defender's state.",
        ["EventLog"] = "The event log. Without it no failure on this machine can be diagnosed.",
        ["EventSystem"] = "COM+ event delivery, which half the other services on this list depend on.",
        ["RpcSs"] = "RPC — every inter-process call on Windows goes through it.",
        ["RpcEptMapper"] = "The RPC endpoint mapper, without which named pipes do not resolve.",
        ["DcomLaunch"] = "DCOM launch. Stopping it takes down services that have not started yet.",
        ["LSM"] = "The Local Session Manager. Stopping it ends the session.",
        ["PlugPlay"] = "Plug and Play. Devices stop being recognised.",
        ["SamSs"] = "The security account manager — local accounts stop authenticating.",
        ["CryptSvc"] = "Cryptographic services. Drivers and certificates stop verifying.",
        ["Winmgmt"] = "WMI, which is how this application reads most of what it shows you.",
        ["Schedule"] = "Task Scheduler. Disabling it disables every scheduled task, including Novimize's own.",
        ["Profsvc"] = "User profile loading. Stopping it logs people out.",
        ["Dhcp"] = "DHCP. Without it there is no address to test anything with.",
        ["Dnscache"] = "The DNS client cache, which is how any name resolves at all.",
        ["NlaSvc"] = "Network location awareness, which decides whether the network counts as public or private.",
        ["netprofm"] = "Network list service. Network profiles stop reporting.",
        ["LanmanWorkstation"] = "The SMB client. Mapped drives and UNC paths stop working.",
        ["LanmanServer"] = "The SMB server. Shares stop being reachable.",
    };

    private readonly string _statePath;

    public ServiceManager(string? statePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "services");
        _statePath = statePath ?? Path.Combine(dir, "state.json");
    }

    public static bool IsProtected(string name, out string reason)
    {
        if (Protected.TryGetValue(name, out var found))
        {
            reason = found;
            return true;
        }
        reason = string.Empty;
        return false;
    }

    public static IReadOnlyDictionary<string, string> ProtectedServices => Protected;

    public async Task<ServiceStatus> ReadAsync(CancellationToken cancel = default)
    {
        var raw = await RunAsync(new { op = "read" }, cancel);
        var read = raw is null ? null : JsonSerializer.Deserialize<ReadResult>(raw, Raw);
        if (read?.Services is null)
            return new ServiceStatus { Error = read?.Error ?? "The service list could not be read." };

        var saved = Load();
        var services = read.Services.Select(s =>
        {
            var protectedEntry = IsProtected(s.Name ?? string.Empty, out var reason);
            var mode = ParseMode(s.StartMode);
            return new ServiceEntry
            {
                Name = s.Name ?? string.Empty,
                DisplayName = s.DisplayName ?? string.Empty,
                Description = s.Description ?? string.Empty,
                Publisher = s.Publisher ?? string.Empty,
                Status = s.Status ?? string.Empty,
                StartMode = mode,
                Path = string.IsNullOrWhiteSpace(s.Path) ? null : s.Path,
                Requires = s.Requires ?? new(),
                DependentOn = s.Dependents ?? new(),
                Protected = protectedEntry,
                ProtectReason = protectedEntry ? reason : null,
                OriginalStartMode = saved.TryGetValue(s.Name ?? string.Empty, out var original)
                    ? ParseMode(original)
                    : null,
            };
        }).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return new ServiceStatus
        {
            Services = services,
            Running = services.Count(s => s.Status.Equals("Running", StringComparison.OrdinalIgnoreCase)),
            Stopped = services.Count(s => s.Status.Equals("Stopped", StringComparison.OrdinalIgnoreCase)),
            ProtectedCount = services.Count(s => s.Protected),
            ElevationKnown = true,
            Error = read.Error,
        };
    }

    private static ServiceStartMode ParseMode(string? value) => value?.ToLowerInvariant() switch
    {
        "boot" => ServiceStartMode.Boot,
        "system" => ServiceStartMode.System,
        "auto" or "automatic" => ServiceStartMode.Automatic,
        "disabled" => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Manual,
    };

    // --- changing ----------------------------------------------------------

    private static readonly HashSet<string> StartActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "start", "stop", "restart", "manual", "automatic", "disabled",
    };

    public async Task<ServiceChange> PerformAsync(
        string name, string action, bool confirm, CancellationToken cancel = default)
    {
        action = action.ToLowerInvariant();
        if (!StartActions.Contains(action))
            return Fail(action, $"Unknown service action '{action}'.");

        var status = await ReadAsync(cancel);
        var service = status.Services.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (service is null)
            return Fail(action, $"No service called '{name}'.");

        var commands = CommandsFor(service, action);
        var preview = new ServiceChange
        {
            Action = action,
            Success = true,
            Affected = 1,
            Message = Describe(service, action),
            Preview = commands,
        };

        // The protected list comes before the confirmation: there is no
        // "are you sure" for a rule that exists precisely so it cannot be
        // confirmed past.
        if (service.Protected)
            return preview with
            {
                Success = false,
                Message = $"{service.Name} will not be changed: {service.ProtectReason}",
            };

        if (action is "stop" && service.DependentOn.Count > 0)
            return preview with
            {
                Success = false,
                Message = $"{service.Name} is required by {service.DependentOn.Count} other service(s): "
                          + string.Join(", ", service.DependentOn.Take(8))
                          + (service.DependentOn.Count > 8 ? ", …" : "")
                          + ". Stopping it stops them.",
            };

        if (action is "start" && service.Requires.Count > 0)
        {
            var missing = service.Requires
                .Where(r => status.Services.All(s => !s.Name.Equals(r, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (missing.Count > 0)
                return preview with
                {
                    Success = false,
                    Message = $"{service.Name} requires {string.Join(", ", missing)}, which could not be found.",
                };
        }

        if (action is "manual" or "automatic" or "disabled")
        {
            if (service.StartMode.ToString().Equals(action, StringComparison.OrdinalIgnoreCase)
                || (action == "automatic" && service.StartMode == ServiceStartMode.Boot)
                || (action == "automatic" && service.StartMode == ServiceStartMode.System))
                return preview with { Unchanged = true, Message = $"{service.Name} is already {action}." };
        }

        if (!confirm)
            return preview with
            {
                Success = false,
                Message = $"{Describe(service, action)} Nothing has been run.",
            };

        if (!Elevation.IsElevated())
            return preview with
            {
                Success = false,
                NeedsElevation = true,
                Message = $"Changing {service.Name} needs administrator rights, so nothing was changed.",
            };

        Remember(service);

        var raw = await RunAsync(new
        {
            op = action,
            name = service.Name,
        }, cancel);

        var read = raw is null ? null : JsonSerializer.Deserialize<ChangeRead>(raw, Raw);
        if (read is null)
            return Fail(action, $"{service.Name} was not changed; PowerShell did not answer.");

        return new ServiceChange
        {
            Action = action,
            Success = read.Success,
            Affected = read.Success ? 1 : 0,
            Unchanged = !read.Success && (read.Message?.Contains("already", StringComparison.OrdinalIgnoreCase) ?? false),
            Message = read.Success
                ? $"{service.Name}: {Describe(service, action).TrimEnd('.')}."
                : read.Message ?? $"{service.Name} could not be changed.",
            Preview = commands,
            Log = read.Log,
            NeedsElevation = read.Message?.Contains("administrator", StringComparison.OrdinalIgnoreCase) ?? false,
        };
    }

    private static List<string> CommandsFor(ServiceEntry service, string action) => action switch
    {
        "start" => new() { $"Start-Service -Name '{service.Name}'" },
        "stop" => new() { $"Stop-Service -Name '{service.Name}' -Force" },
        "restart" => new() { $"Restart-Service -Name '{service.Name}' -Force" },
        "manual" => new() { $"Set-Service -Name '{service.Name}' -StartupType Manual" },
        "automatic" => new() { $"Set-Service -Name '{service.Name}' -StartupType Automatic" },
        _ => new() { $"Set-Service -Name '{service.Name}' -StartupType Disabled" },
    };

    private static string Describe(ServiceEntry service, string action) => action switch
    {
        "start" => $"{service.DisplayName} would be started.",
        "stop" => $"{service.DisplayName} would be stopped.",
        "restart" => $"{service.DisplayName} would be stopped and started again, dropping whatever it was doing.",
        "manual" => $"{service.DisplayName} would start only when something asks for it.",
        "automatic" => $"{service.DisplayName} would start at every boot.",
        _ => $"{service.DisplayName} would be stopped and would not start again until something starts it by hand.",
    };

    /// <summary>Put the start mode back to what it was before the first change.</summary>
    public async Task<ServiceChange> RestoreAsync(string name, bool confirm, CancellationToken cancel = default)
    {
        var status = await ReadAsync(cancel);
        var service = status.Services.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (service is null) return Fail("restore", $"No service called '{name}'.");
        if (service.Protected)
            return Fail("restore", $"{service.Name} will not be changed: {service.ProtectReason}");
        if (service.OriginalStartMode is null)
            return new ServiceChange
            {
                Action = "restore",
                Success = true,
                Unchanged = true,
                Message = $"{service.Name} has not been changed by Novimize, so there is nothing to put back.",
            };

        return await PerformAsync(name, ModeAction(service.OriginalStartMode.Value), confirm, cancel);
    }

    private static string ModeAction(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => "automatic",
        ServiceStartMode.Disabled => "disabled",
        _ => "manual",
    };

    private ServiceChange Fail(string action, string message) => new()
    {
        Action = action,
        Success = false,
        Message = message,
    };

    // --- saved start modes --------------------------------------------------

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_statePath)) ?? new();
        }
        catch { return new(); }
    }

    private void Remember(ServiceEntry service)
    {
        if (service.OriginalStartMode is not null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var state = Load();
            if (state.ContainsKey(service.Name)) return;
            state[service.Name] = service.StartMode.ToString();
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* remembering is a convenience, not a requirement */ }
    }

    // --- PowerShell ---------------------------------------------------------

    private static readonly JsonSerializerOptions Raw = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<string?> RunAsync(object payload, CancellationToken cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"novimize-services-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(directory, "payload.json");
        var scriptPath = Path.Combine(directory, "services.ps1");
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
        # Output is read back as UTF-8, and a description containing a
        # typographic quote is common enough that leaving it to the console
        # encoding is how a perfectly valid document stops parsing.
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $p = Get-Content -LiteralPath $Payload -Raw -Encoding UTF8 | ConvertFrom-Json

        function Clean([string]$s) {
            if (-not $s) { return $s }
            # Straight quotes are what ConvertTo-Json knows how to escape;
            # the curly ones are not escaped and arrive as stray delimiters.
            return ($s -replace [string][char]0x201C, '"' `
                       -replace [string][char]0x201D, '"' `
                       -replace [string][char]0x2018, "'" `
                       -replace [string][char]0x2019, "'")
        }

        function Reply([object]$body) { $body | ConvertTo-Json -Depth 8 -Compress }

        try {
            switch ($p.op) {

                'read' {
                    $rows = @()
                    foreach ($s in @(Get-Service -ErrorAction SilentlyContinue)) {
                        $cim = $null
                        try { $cim = Get-CimInstance -ClassName Win32_Service -Filter ("Name='" + ($s.Name -replace "'","''") + "'") -ErrorAction Stop } catch { }
                        $publisher = $null
                        $path = $null
                        if ($cim) {
                            $path = [string]$cim.PathName
                            if ($path) {
                                $exe = $path.Trim()
                                if ($exe.StartsWith('"')) {
                                    $close = $exe.IndexOf('"', 1)
                                    if ($close -gt 1) { $exe = $exe.Substring(1, $close - 1) }
                                } else {
                                    $sp = $exe.IndexOf(' ')
                                    if ($sp -gt 0) { $exe = $exe.Substring(0, $sp) }
                                }
                                if ($exe -and (Test-Path -LiteralPath $exe)) {
                                    try { $publisher = [string](Get-Item -LiteralPath $exe).VersionInfo.CompanyName } catch { }
                                }
                            }
                        }

                        $requires = @()
                        $dependents = @()
                        try { $requires = @($s.RequiredServices | ForEach-Object { [string]$_.Name }) } catch { }
                        try { $dependents = @($s.DependentServices | ForEach-Object { [string]$_.Name }) } catch { }

                        $mode = 'Manual'
                        if ($cim) { $mode = [string]$cim.StartMode }

                        $rows += [pscustomobject]@{
                            name        = [string]$s.Name
                            displayName = (Clean ([string]$s.DisplayName))
                            description = (Clean $(if ($cim) { [string]$cim.Description } else { '' }))
                            publisher   = $(if ($publisher) { $publisher } else { '' })
                            status      = [string]$s.Status
                            startMode   = $mode
                            path        = $path
                            requires    = $requires
                            dependents  = $dependents
                        }
                    }
                    Reply @{ services = $rows; error = $null }
                    # Answered: without this the script falls into the write
                    # logic below and replies a second time, which makes the
                    # stdout two JSON documents and breaks every reader.
                    exit
                }

                'start'     { $op = 'start' }
                'stop'      { $op = 'stop' }
                'restart'   { $op = 'restart' }
                'manual'    { $op = 'manual' }
                'automatic' { $op = 'automatic' }
                'disabled'  { $op = 'disabled' }
                default     { Reply @{ success = $false; message = 'Unknown operation.' }; exit }
            }

            $name = [string]$p.name
            switch ($op) {
                'start'     { Start-Service -Name $name -ErrorAction Stop }
                'stop'      { Stop-Service -Name $name -Force -ErrorAction Stop }
                'restart'   { Restart-Service -Name $name -Force -ErrorAction Stop }
                'manual'    { Set-Service -Name $name -StartupType Manual -ErrorAction Stop }
                'automatic' { Set-Service -Name $name -StartupType Automatic -ErrorAction Stop }
                'disabled'  { Set-Service -Name $name -StartupType Disabled -ErrorAction Stop }
            }

            Reply @{ success = $true; message = 'ok' }
        } catch {
            Reply @{ success = $false; message = [string]$_.Exception.Message }
        }
        """;

    private sealed class ReadResult
    {
        public List<RawService>? Services { get; init; }
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
public sealed class RawService
{
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Publisher { get; init; }
    public string? Status { get; init; }
    public string? StartMode { get; init; }
    public string? Path { get; init; }
    public List<string>? Requires { get; init; }
    public List<string>? Dependents { get; init; }
}
