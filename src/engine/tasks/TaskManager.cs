using System.Text.Json;
using System.Text.Json.Serialization;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Tasks;

/// <summary>
/// Scheduled tasks: what is registered, what ran last, and the enabled flag.
///
/// Tasks are never deleted. Disabling writes the same state the Task Scheduler
/// console does, and the state found before the first change is kept so
/// "restore" means the state the machine was actually in rather than a guess
/// that it was enabled.
///
/// A task's name is not unique — it is the name and its folder together — so
/// every operation carries both, and the id the page shows is that pair.
/// </summary>
public sealed class TaskManager
{
    private readonly string _statePath;

    public TaskManager(string? statePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "tasks");
        _statePath = statePath ?? Path.Combine(dir, "state.json");
    }

    public async Task<ScheduledTaskStatus> ReadAsync(CancellationToken cancel = default)
    {
        var raw = await RunAsync(new { op = "read" }, cancel);
        var read = raw is null ? null : JsonSerializer.Deserialize<ReadResult>(raw, Raw);
        if (read?.Tasks is null)
            return new ScheduledTaskStatus { Error = read?.Error ?? "The task list could not be read." };

        var saved = Load();
        var tasks = read.Tasks.Select(t =>
        {
            var enabled = !string.Equals(t.State, "Disabled", StringComparison.OrdinalIgnoreCase);
            var id = (t.TaskPath ?? "\\") + t.Name;
            return new ScheduledTaskEntry
            {
                Name = t.Name ?? string.Empty,
                TaskPath = t.TaskPath ?? "\\",
                Path = id,
                Id = Encode(id),
                State = t.State ?? string.Empty,
                Enabled = enabled,
                Author = t.Author ?? string.Empty,
                Description = t.Description ?? string.Empty,
                Trigger = t.Trigger ?? string.Empty,
                Command = t.Command ?? string.Empty,
                Arguments = t.Arguments ?? string.Empty,
                WorkingDirectory = t.WorkingDirectory ?? string.Empty,
                LastRun = ParseDate(t.LastRun),
                NextRun = ParseDate(t.NextRun),
                LastResult = t.LastResult,
                SystemTask = (t.TaskPath ?? "\\").StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase),
                ChangedByNovimize = saved.ContainsKey(id),
                OriginalEnabled = saved.TryGetValue(id, out var was) ? was : null,
            };
        }).OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();

        return new ScheduledTaskStatus
        {
            Tasks = tasks,
            Enabled = tasks.Count(t => t.Enabled),
            Disabled = tasks.Count(t => !t.Enabled),
            ChangedByNovimize = tasks.Count(t => t.ChangedByNovimize),
            Error = read.Error,
        };
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    /// <summary>
    /// The id the page and the CLI pass around. A folder and a task name both
    /// contain characters that have no business on a command line, so they are
    /// encoded rather than escaped — the pair round-trips exactly.
    /// </summary>
    public static string Encode(string taskPathAndName) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(taskPathAndName))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Decode(string id)
    {
        var padded = id.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch { return id; }
    }

    // --- changing ----------------------------------------------------------

    public async Task<ScheduledTaskChange> PerformAsync(
        string id, string action, bool confirm, CancellationToken cancel = default)
    {
        action = action.ToLowerInvariant();
        if (action is not ("enable" or "disable" or "run"))
            return Fail(action, $"Unknown task action '{action}'.");

        var status = await ReadAsync(cancel);
        var key = Decode(id);
        var task = status.Tasks.FirstOrDefault(t =>
            t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
            || t.Path.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (task is null)
            return Fail(action, "No scheduled task with that id.");

        var commands = new List<string>
        {
            action == "run"
                ? $"Start-ScheduledTask -TaskName '{task.Name}' -TaskPath '{task.TaskPath}'"
                : $"{(action == "enable" ? "Enable" : "Disable")}-ScheduledTask -TaskName '{task.Name}' -TaskPath '{task.TaskPath}'",
        };

        var preview = new ScheduledTaskChange
        {
            Action = action,
            Success = true,
            Affected = 1,
            Message = action switch
            {
                "run" => $"{task.Name} would be started now, outside its normal schedule.",
                "enable" => $"{task.Name} would run on its trigger again.",
                _ => $"{task.Name} would stop running on its trigger. The task itself stays registered.",
            },
            Preview = commands,
        };

        if (action == "enable" && task.Enabled)
            return preview with { Unchanged = true, Message = $"{task.Name} is already enabled." };
        if (action == "disable" && !task.Enabled)
            return preview with { Unchanged = true, Message = $"{task.Name} is already disabled." };

        if (!confirm)
            return preview with { Success = false, Message = preview.Message + " Nothing has been run." };

        if (!Elevation.IsElevated() && action != "run")
            return preview with
            {
                Success = false,
                NeedsElevation = true,
                Message = $"Changing {task.Name} needs administrator rights, so nothing was changed.",
            };

        if (action is "enable" or "disable") Remember(task);

        var raw = await RunAsync(new
        {
            op = action,
            name = task.Name,
            taskPath = task.TaskPath,
        }, cancel);

        var read = raw is null ? null : JsonSerializer.Deserialize<ChangeRead>(raw, Raw);
        if (read is null)
            return Fail(action, $"{task.Name} was not changed; PowerShell did not answer.");

        return new ScheduledTaskChange
        {
            Action = action,
            Success = read.Success,
            Affected = read.Success ? 1 : 0,
            Message = read.Success
                ? action == "run"
                    ? $"{task.Name} was started."
                    : $"{task.Name} is now {(action == "enable" ? "enabled" : "disabled")}. The task was not deleted."
                : read.Message ?? $"{task.Name} could not be changed.",
            Preview = commands,
            Log = read.Log,
            NeedsElevation = read.Message?.Contains("administrator", StringComparison.OrdinalIgnoreCase) ?? false,
        };
    }

    /// <summary>Put a task back to whether it was enabled before Novimize touched it.</summary>
    public async Task<ScheduledTaskChange> RestoreAsync(string id, bool confirm, CancellationToken cancel = default)
    {
        var status = await ReadAsync(cancel);
        var key = Decode(id);
        var task = status.Tasks.FirstOrDefault(t =>
            t.Id.Equals(id, StringComparison.OrdinalIgnoreCase) || t.Path.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (task is null) return Fail("restore", "No scheduled task with that id.");
        if (task.OriginalEnabled is null)
            return new ScheduledTaskChange
            {
                Action = "restore",
                Success = true,
                Unchanged = true,
                Message = $"{task.Name} has not been changed by Novimize, so there is nothing to put back.",
            };

        return await PerformAsync(task.Id, task.OriginalEnabled.Value ? "enable" : "disable", confirm, cancel);
    }

    private static ScheduledTaskChange Fail(string action, string message) => new()
    {
        Action = action,
        Success = false,
        Message = message,
    };

    // --- saved state --------------------------------------------------------

    private Dictionary<string, bool> Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(_statePath)) ?? new();
        }
        catch { return new(); }
    }

    private void Remember(ScheduledTaskEntry task)
    {
        if (task.OriginalEnabled is not null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var state = Load();
            if (state.ContainsKey(task.Path)) return;
            state[task.Path] = task.Enabled;
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
        var directory = Path.Combine(Path.GetTempPath(), $"novimize-tasks-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(directory, "payload.json");
        var scriptPath = Path.Combine(directory, "tasks.ps1");
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
            }, TimeSpan.FromSeconds(120));

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

        function DescribeTrigger($t) {
            # The class is MSFT_TaskLogonTrigger: strip the MSFT_Task prefix
            # and the Trigger suffix, or every rule below matches nothing and
            # the column reads "MSFT_TaskLogon" instead of "At logon".
            $className = $null
            try { $className = [string]$t.CimClass.CimClassName } catch { }
            $kind = ''
            if ($className) { $kind = ($className -replace '^MSFT_Task','') -replace 'Trigger$','' }

            if (-not $kind) {
                # A hundred tasks here report the base class, and some report
                # no class at all. Their own fields still read better than an
                # empty string, which the join would turn into ";; ".
                $boundary = $null
                try { $boundary = $t.StartBoundary } catch { }
                if ($boundary) { return ('At ' + [string]$boundary) }
                $enabled = $null
                try { $enabled = $t.Enabled } catch { }
                if ($null -ne $enabled) { return 'On a schedule' }
                return 'Trigger'
            }

            switch -Regex ($kind) {
                '^Boot'   { return 'At startup' }
                '^Logon'  {
                    if ($t.UserId) { return ('At logon of ' + [string]$t.UserId) }
                    return 'At logon'
                }
                '^Time'   {
                    if ($t.StartBoundary) { return ('At ' + [string]$t.StartBoundary) }
                    return 'At a scheduled time'
                }
                '^Weekly' { return 'Weekly' }
                '^Monthly'{ return 'Monthly' }
                '^Idle'   { return 'When the machine is idle' }
                '^Event'  { return 'On an event' }
                '^Registration' { return 'At registration' }
                '^SessionState' { return 'On session state change' }
                '^MultiSession' { return 'On session count change' }
                # An unrecognised class still has to read as something: the raw
                # identifier with its camel humps split beats an empty string
                # that the join turns into ";; ".
                default   {
                    if (-not $kind) { return 'Trigger' }
                    return ($kind -creplace '([a-z0-9])([A-Z])', '$1 $2')
                }
            }
        }

        try {
            switch ($p.op) {

                'read' {
                    $rows = @()
                    foreach ($t in @(Get-ScheduledTask -ErrorAction SilentlyContinue)) {
                        $info = $null
                        try { $info = $t | Get-ScheduledTaskInfo -ErrorAction Stop } catch { }

                        $trigger = @($t.Triggers | ForEach-Object { DescribeTrigger $_ } |
                                     Where-Object { $_ }) -join '; '
                        if (-not $trigger) { $trigger = 'No trigger' }

                        # Not $args: that is an automatic variable.
                        $cmd = ''; $actionArgs = ''; $wd = ''
                        try {
                            $a = $t.Actions[0]
                            $cmd = [string]$a.Execute
                            $actionArgs = [string]$a.Arguments
                            $wd = [string]$a.WorkingDirectory
                        } catch { }

                        $last = $null; $next = $null; $code = 0
                        if ($info) {
                            if ($info.LastRunTime -and $info.LastRunTime -gt [datetime]::MinValue) { $last = $info.LastRunTime.ToString('o') }
                            if ($info.NextRunTime -and $info.NextRunTime -gt [datetime]::MinValue) { $next = $info.NextRunTime.ToString('o') }
                            # LastTaskResult is a HRESULT: values such as 0x800710E0 sit
                            # above Int32.MaxValue, and casting to [int] throws.
                            $code = [long]$info.LastTaskResult
                        }

                        $rows += [pscustomobject]@{
                            name           = [string]$t.TaskName
                            taskPath       = [string]$t.TaskPath
                            state          = [string]$t.State
                            author         = (Clean ([string]$t.Author))
                            description    = (Clean ([string]$t.Description))
                            trigger        = $trigger
                            command        = $cmd
                            arguments      = $actionArgs
                            workingDirectory = $wd
                            lastRun        = $last
                            nextRun        = $next
                            lastResult     = $code
                        }
                    }
                    Reply @{ tasks = $rows; error = $null }
                    # Answered: without this the script falls into the write
                    # logic below and replies a second time, which makes the
                    # stdout two JSON documents and breaks every reader.
                    exit
                }

                'enable'  { $desired = $true }
                'disable' { $desired = $false }
                'run'     { $desired = $null }
                default   { Reply @{ success = $false; message = 'Unknown operation.' }; exit }
            }

            # $args is an automatic variable; splatting into it is asking for
            # trouble in a script that also has a param block.
            $taskArgs = @{ TaskName = [string]$p.name; TaskPath = [string]$p.taskPath }
            if ($null -eq $desired) {
                Start-ScheduledTask @taskArgs -ErrorAction Stop
            } elseif ($desired) {
                Enable-ScheduledTask @taskArgs -ErrorAction Stop | Out-Null
            } else {
                Disable-ScheduledTask @taskArgs -ErrorAction Stop | Out-Null
            }

            Reply @{ success = $true; message = 'ok' }
        } catch {
            Reply @{ success = $false; message = [string]$_.Exception.Message }
        }
        """;

    private sealed class ReadResult
    {
        public List<RawTask>? Tasks { get; init; }
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
public sealed class RawTask
{
    public string? Name { get; init; }
    public string? TaskPath { get; init; }
    public string? State { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string? Trigger { get; init; }
    public string? Command { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? LastRun { get; init; }
    public string? NextRun { get; init; }
    public long LastResult { get; init; }
}
