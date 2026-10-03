using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Startup;

/// <summary>
/// Everything that runs when you sign in, and nothing that is ever deleted.
///
/// Disabling writes the same StartupApproved flag Task Manager writes — one
/// byte in a value Windows already understands — so restoring is writing it
/// back. Deleting a Run entry would take the program's own registration with
/// it, and a program that expects to find itself at logon does not fail
/// quietly when it is gone.
/// </summary>
public sealed class StartupManager
{
    /// <summary>Proceses that start an updater, which then starts the program.</summary>
    private static readonly string[] UpdaterPatterns =
    {
        "update", "updater", "autoupdate", "auto-update", "--processstart",
        "squirrel", "quirrel", "maintenanceservice", "releaseupdate",
    };

    public async Task<StartupStatus> ReadAsync(CancellationToken cancel = default)
    {
        var raw = await RunAsync(new { op = "read" }, cancel);
        var read = raw is null ? null : JsonSerializer.Deserialize<ReadResult>(raw, Raw);
        if (read?.Items is null)
            return new StartupStatus { Error = read?.Error ?? "The startup configuration could not be read." };

        var items = read.Items.Select(ToItem).ToList();
        return new StartupStatus
        {
            Items = items,
            UserStartupFolder = read.UserFolder ?? string.Empty,
            CommonStartupFolder = read.CommonFolder ?? string.Empty,
            EnabledCount = items.Count(i => i.Enabled),
            DisabledCount = items.Count(i => !i.Enabled),
            BrokenCount = items.Count(i => i.Broken),
            Error = read.Error,
        };
    }

    private static StartupItem ToItem(RawItem raw)
    {
        var target = ExtractTarget(raw.Command);
        var broken = target is not null && !File.Exists(target);
        var (impact, reason) = Classify(raw, target, broken);

        return new StartupItem
        {
            Id = $"{raw.Kind}:{raw.Hive}:{raw.Name}",
            Name = raw.Name,
            Publisher = PublisherOf(target),
            Command = raw.Command ?? string.Empty,
            TargetPath = target,
            Location = raw.Location ?? string.Empty,
            Kind = ParseKind(raw.Kind),
            Hive = raw.Hive,
            TaskPath = raw.TaskPath,
            Enabled = raw.Enabled,
            // HKLM entries need administrator rights to flip; saying so up
            // front beats a prompt nobody was expecting.
            Writable = !raw.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase)
                       || Elevation.IsElevated(),
            Impact = impact,
            ImpactReason = reason,
            Broken = broken,
        };
    }

    /// <summary>
    /// The executable a startup command points at.
    ///
    /// A quoted command is unambiguous. An unquoted one is not, and Windows
    /// does not treat it as ending at the first space either: the Run key
    /// parser grows the candidate prefix at each space until it finds a file
    /// that exists, which is the only reason an entry like
    /// <c>C:\Program Files (x86)\App\app.exe -silent</c> starts at all. This
    /// walks the same prefixes, so an unquoted path with spaces in it is
    /// recognised instead of being reported as a missing file called
    /// <c>C:\Program</c>.
    /// </summary>
    public static string? ExtractTarget(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());

        var quote = expanded.StartsWith('"') ? '"'
            : expanded.StartsWith('\'') ? '\''
            : '\0';

        if (quote != '\0')
        {
            var close = expanded.IndexOf(quote, 1);
            return close > 1 ? expanded[1..close] : expanded.Trim(quote);
        }

        // A bare "app.exe --flag" with no directory: Windows resolves it on
        // the PATH, so there is no file path to test existence against.
        var first = expanded.IndexOf(' ');
        if (first < 0) return LooksLikePath(expanded) ? expanded : null;

        string? withExtension = null;
        var parts = expanded.Split(' ');
        var prefix = parts[0];
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) prefix += " " + parts[i];
            if (!LooksLikePath(prefix)) continue;
            if (File.Exists(prefix)) return prefix;

            // The first prefix that looks like a program is as far as a
            // missing file can be claimed: everything after it is arguments,
            // and appending those will never make the file appear.
            if (HasExecutableExtension(prefix)) return prefix;
            if (Path.GetExtension(prefix).Length > 0) withExtension = prefix;
        }

        // Nothing matched. The longest prefix that carries an extension is
        // still better than "C:\Program", because it is the thing that
        // actually failed.
        return withExtension ?? (LooksLikePath(parts[0]) ? parts[0] : null);
    }

    private static bool LooksLikePath(string value) =>
        value.Contains('\\') || value.Contains('/');

    private static bool HasExecutableExtension(string value)
    {
        var ext = Path.GetExtension(value);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A guess, and labelled as one. Nothing here is measured — measuring
    /// would mean running the program — so the rules are the three things that
    /// can be known without running anything: the file is gone, the command
    /// starts an updater that then starts the program, or it is a plain single
    /// executable. Every row carries the reason it was classified.
    /// </summary>
    public static (string Impact, string Reason) Classify(RawItem raw, string? target, bool broken)
    {
        if (broken)
            return ("Broken", $"the file {target} no longer exists");

        var command = (raw.Command ?? string.Empty).ToLowerInvariant();
        var name = (raw.Name ?? string.Empty).ToLowerInvariant();

        foreach (var pattern in UpdaterPatterns)
        {
            if (command.Contains(pattern) || name.Contains(pattern))
                return ("Heavy", "it starts through an updater, which then starts the program — " +
                                 "two launches to get one running");
        }

        if (raw.Kind == "task")
            return ("Medium", "a scheduled task, which Windows runs as its own process rather " +
                              "than as part of sign-in");

        if (string.IsNullOrWhiteSpace(StripTarget(raw.Command)))
            return ("Low", "one executable with no arguments — the cheapest shape a startup entry has");

        return ("Medium", "a program launched with arguments, so what it costs depends on what it is told to do");
    }

    private static string StripTarget(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());

        var quote = expanded.StartsWith('"') ? '"'
            : expanded.StartsWith('\'') ? '\''
            : '\0';

        if (quote != '\0')
        {
            var close = expanded.IndexOf(quote, 1);
            return close > 0 && close + 1 < expanded.Length ? expanded[(close + 1)..].Trim() : string.Empty;
        }

        var space = expanded.IndexOf(' ');
        return space < 0 ? string.Empty : expanded[(space + 1)..].Trim();
    }

    private static string? PublisherOf(string? target)
    {
        if (target is null || !File.Exists(target)) return null;
        try
        {
            var name = FileVersionInfo.GetVersionInfo(target).CompanyName;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch { return null; }
    }

    private static StartupKind ParseKind(string kind) => kind switch
    {
        "folder" => StartupKind.StartupFolder,
        "common-folder" => StartupKind.CommonStartupFolder,
        "task" => StartupKind.ScheduledTask,
        "startup-task" => StartupKind.StartupTask,
        _ => StartupKind.Run,
    };

    // --- changing ----------------------------------------------------------

    /// <summary>What flipping this entry would write, without writing it.</summary>
    public static StartupChange Preview(StartupItem item, bool enabled)
    {
        return new StartupChange
        {
            Action = enabled ? "enable" : "disable",
            Success = true,
            Unchanged = item.Enabled == enabled,
            Affected = 1,
            Message = item.Enabled == enabled
                ? $"{item.Name} is already {(enabled ? "enabled" : "disabled")}."
                : $"{item.Name} would be {(enabled ? "enabled" : "disabled")} at sign-in. Nothing is removed.",
            Preview = new List<string> { DescribeWrite(item, enabled) },
        };
    }

    private static string DescribeWrite(StartupItem item, bool enabled) => item.Kind switch
    {
        StartupKind.ScheduledTask => $"{(enabled ? "Enable" : "Disable")}-ScheduledTask " +
                                     $"-TaskName '{item.Name}' -TaskPath '{item.TaskPath ?? "\\"}'",
        StartupKind.StartupTask => "UWP startup tasks are registered by their app; Novimize does not change them.",
        _ => $"StartupApproved\\{ApprovedSub(item)}[{item.Name}] = {(enabled ? "0x02 (enabled)" : "0x03 (disabled)")}",
    };

    private static string ApprovedSub(StartupItem item) =>
        item.Kind is StartupKind.StartupFolder or StartupKind.CommonStartupFolder ? "StartupFolder" : "Run";

    public async Task<StartupChange> SetEnabledAsync(
        string id, bool enabled, bool confirm, CancellationToken cancel = default)
    {
        var status = await ReadAsync(cancel);
        var item = status.Items.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return Fail("startup", $"No startup entry with id '{id}'.");

        var preview = Preview(item, enabled);
        if (item.Enabled == enabled) return preview;

        if (item.Kind == StartupKind.StartupTask)
            return preview with
            {
                Success = false,
                Message = $"{item.Name} is a Store app's own startup toggle. Open the app's settings " +
                          "or Task Manager to change it — Novimize will not rewrite its registration.",
            };

        if (!confirm)
            return preview with
            {
                Success = false,
                Message = $"{(enabled ? "Enabling" : "Disabling")} {item.Name} changes what runs at sign-in. " +
                          "Nothing has been written.",
            };

        if (!item.Writable && !Elevation.IsElevated())
            return preview with
            {
                Success = false,
                NeedsElevation = true,
                Message = $"{item.Name} is registered for every user, so changing it needs administrator rights.",
            };

        var raw = await RunAsync(new
        {
            op = enabled ? "enable" : "disable",
            kind = item.Kind switch
            {
                StartupKind.StartupFolder => "folder",
                StartupKind.CommonStartupFolder => "common-folder",
                StartupKind.ScheduledTask => "task",
                StartupKind.StartupTask => "startup-task",
                _ => "run",
            },
            hive = item.Hive,
            name = item.Name,
            taskPath = item.TaskPath ?? "\\",
        }, cancel);

        var read = raw is null ? null : JsonSerializer.Deserialize<ChangeRead>(raw, Raw);
        if (read is null)
            return Fail("startup", "The startup entry was not changed; PowerShell did not answer.");

        return new StartupChange
        {
            Action = enabled ? "enable" : "disable",
            Success = read.Success,
            Affected = read.Success ? 1 : 0,
            Message = read.Success
                ? $"{item.Name} is now {(enabled ? "enabled" : "disabled")} at sign-in. Nothing was removed."
                : read.Message ?? "The startup entry could not be changed.",
            Preview = preview.Preview,
            Log = read.Log,
            NeedsElevation = read.Message?.Contains("administrator", StringComparison.OrdinalIgnoreCase) == true,
        };
    }

    /// <summary>Where an entry lives, in Explorer, with the file selected.</summary>
    public async Task<StartupChange> OpenAsync(string id, CancellationToken cancel = default)
    {
        var status = await ReadAsync(cancel);
        var item = status.Items.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (item is null) return Fail("startup", $"No startup entry with id '{id}'.");

        string? argument;
        if (item.Kind is StartupKind.StartupFolder or StartupKind.CommonStartupFolder)
        {
            if (item.TargetPath is null || !File.Exists(item.TargetPath))
                return Fail("startup", $"{item.Name} points at a file that is not on disk any more.");
            argument = "/select," + item.TargetPath;
        }
        else if (item.TargetPath is not null && File.Exists(item.TargetPath))
        {
            argument = Path.GetDirectoryName(item.TargetPath);
            if (argument is null) return Fail("startup", $"{item.Name} has no folder to open.");
        }
        else
        {
            return Fail("startup", $"{item.Name} has no file on disk to open.");
        }

        await ProcessRunner.RunAsync("explorer.exe", new[] { argument }, TimeSpan.FromSeconds(20));

        return new StartupChange
        {
            Action = "open",
            Success = true,
            Affected = 1,
            Message = $"Opened {item.TargetPath}.",
        };
    }

    private static StartupChange Fail(string action, string message) => new()
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
        var directory = Path.Combine(Path.GetTempPath(), $"novimize-startup-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(directory, "payload.json");
        var scriptPath = Path.Combine(directory, "startup.ps1");
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
            }, TimeSpan.FromSeconds(60));

            var start = result.StdOut.IndexOf('{');
            return start < 0 ? null : result.StdOut[start..];
        }
        catch { return null; }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* temp */ }
        }
    }

    /// <summary>
    /// One script for reading and for flipping the one flag. The flag is a
    /// binary value Windows already reads, so nothing here edits a Run key:
    /// the registration stays exactly as the program wrote it.
    /// </summary>
    private const string Script = """
        param([string]$Payload)

        $ErrorActionPreference = 'Stop'
        $p = Get-Content -LiteralPath $Payload -Raw -Encoding UTF8 | ConvertFrom-Json

        function Reply([object]$body) { $body | ConvertTo-Json -Depth 8 -Compress }

        function ApprovedRoot([string]$hive) {
            $root = if ($hive -eq 'HKLM') { 'HKLM:' } else { 'HKCU:' }
            Join-Path $root 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved'
        }

        function ApprovedSubFor([string]$kind) {
            if ($kind -eq 'folder' -or $kind -eq 'common-folder') { 'StartupFolder' } else { 'Run' }
        }

        function ReadFlag([string]$hive, [string]$sub, [string]$name) {
            # Absent means enabled: that is the default for an entry nobody
            # has touched in Task Manager.
            $path = Join-Path (ApprovedRoot $hive) $sub
            if (-not (Test-Path $path)) { return $true }
            $v = (Get-Item $path).GetValue($name)
            if ($null -eq $v) { return $true }
            $b = [byte[]]$v
            if ($b.Length -eq 0) { return $true }
            return ($b[0] -eq 2)
        }

        function WriteFlag([string]$hive, [string]$sub, [string]$name, [bool]$enabled) {
            $path = Join-Path (ApprovedRoot $hive) $sub
            if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
            $key = Get-Item $path
            $existing = $key.GetValue($name)
            $b = if ($null -ne $existing -and $existing -is [byte[]] -and $existing.Length -ge 4) {
                     [byte[]]$existing.Clone()
                 } else {
                     New-Object byte[] 12
                 }

            if ($enabled) {
                $b[0] = 2
                for ($i = 1; $i -lt $b.Length; $i++) { $b[$i] = 0 }
            } else {
                $b[0] = 3
                for ($i = 1; $i -lt 4 -and $i -lt $b.Length; $i++) { $b[$i] = 0 }
                if ($b.Length -ge 12) {
                    $stamp = [BitConverter]::GetBytes([long][DateTime]::Now.ToFileTime())
                    for ($i = 0; $i -lt 8; $i++) { $b[$i + 4] = $stamp[$i] }
                }
            }
            $key.SetValue($name, $b, [Microsoft.Win32.RegistryValueKind]::Binary)
        }

        try {
            switch ($p.op) {

                'read' {
                    $items = @()

                    foreach ($entry in @(
                        @{ hive = 'HKCU'; sub = 'Run'; root = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run';  kind = 'run';          label = 'HKCU Run' }
                        @{ hive = 'HKLM'; sub = 'Run'; root = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run';  kind = 'run';          label = 'HKLM Run' }
                    )) {
                        if (-not (Test-Path $entry.root)) { continue }
                        $key = Get-Item $entry.root
                        foreach ($n in $key.GetValueNames()) {
                            $items += [pscustomobject]@{
                                name     = [string]$n
                                command  = [string]$key.GetValue($n)
                                location = [string]$entry.label
                                kind     = $entry.kind
                                hive     = $entry.hive
                                taskPath = $null
                                enabled  = (ReadFlag $entry.hive $entry.sub $n)
                            }
                        }
                    }

                    foreach ($entry in @(
                        @{ hive = 'HKCU'; root = [Environment]::GetFolderPath('Startup');       sub = 'StartupFolder'; kind = 'folder';        label = 'Startup folder' }
                        @{ hive = 'HKLM'; root = [Environment]::GetFolderPath('CommonStartup'); sub = 'StartupFolder'; kind = 'common-folder'; label = 'Common startup folder' }
                    )) {
                        if (-not (Test-Path $entry.root)) { continue }
                        foreach ($f in @(Get-ChildItem -LiteralPath $entry.root -Force -ErrorAction SilentlyContinue)) {
                            $items += [pscustomobject]@{
                                name     = [string]$f.Name
                                command  = [string]$f.FullName
                                location = [string]$entry.label
                                kind     = $entry.kind
                                hive     = $entry.hive
                                taskPath = $null
                                enabled  = (ReadFlag $entry.hive $entry.sub $f.Name)
                            }
                        }
                    }

                    foreach ($t in @(Get-ScheduledTask -ErrorAction SilentlyContinue)) {
                        $triggers = @($t.Triggers | Where-Object {
                            $_.CimClass.CimClassName -match 'LogonTrigger|BootTrigger'
                        })
                        if ($triggers.Count -eq 0) { continue }
                        $exe = $null
                        try { $exe = [string]$t.Actions[0].Execute } catch { }
                        $items += [pscustomobject]@{
                            name     = [string]$t.TaskName
                            command  = [string]$exe
                            location = 'Scheduled task'
                            kind     = 'task'
                            hive     = 'HKLM'
                            taskPath = [string]$t.TaskPath
                            enabled  = (([string]$t.State) -ne 'Disabled')
                        }
                    }

                    Reply @{
                        items = $items
                        userFolder = [Environment]::GetFolderPath('Startup')
                        commonFolder = [Environment]::GetFolderPath('CommonStartup')
                        error = $null
                    }
                    exit
                }

                'enable'  { $desired = $true }
                'disable' { $desired = $false }
                default   { Reply @{ success = $false; message = 'Unknown operation.' }; exit }
            }

            $kind = [string]$p.kind
            if ($kind -eq 'task') {
                $taskArgs = @{ TaskName = [string]$p.name; TaskPath = [string]$p.taskPath }
                if ($desired) { Enable-ScheduledTask @taskArgs | Out-Null } else { Disable-ScheduledTask @taskArgs | Out-Null }
            } elseif ($kind -eq 'startup-task') {
                Reply @{ success = $false; message = 'UWP startup tasks are not changed by Novimize.' }
                exit
            } else {
                WriteFlag ([string]$p.hive) (ApprovedSubFor $kind) ([string]$p.name) ([bool]$desired)
            }

            Reply @{ success = $true; message = 'ok' }
        } catch {
            Reply @{ success = $false; message = [string]$_.Exception.Message }
        }
        """;

    private sealed class ReadResult
    {
        public List<RawItem>? Items { get; init; }
        public string? UserFolder { get; init; }
        public string? CommonFolder { get; init; }
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
public sealed class RawItem
{
    public string Name { get; init; } = string.Empty;
    public string? Command { get; init; }
    public string? Location { get; init; }
    public string Kind { get; init; } = "run";
    public string Hive { get; init; } = "HKCU";
    public string? TaskPath { get; init; }
    public bool Enabled { get; init; } = true;
}
