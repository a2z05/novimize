using System.Text.Json;
using System.Text.Json.Serialization;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Update;

/// <summary>
/// Where Windows Update stands, what is owed, and how to finish it.
///
/// Nothing here disables updating. The brief is explicit that permanently
/// switching updates off is not a default optimization, and there is no code
/// in this class that writes to the update policy at all — the only writes
/// are "open the settings page" and "restart to finish what is already
/// installed".
///
/// The pending-reboot question is answered from the three places Windows
/// actually records it rather than from one, because a machine can be owed a
/// restart by servicing while the Windows Update key is quiet.
/// </summary>
public sealed class UpdateManager
{
    /// <summary>
    /// Where Windows records that a restart is owed. Each has a different
    /// cause, so each is reported by name rather than as one boolean. This
    /// list is the one the reader checks — a marker written down here and not
    /// looked at would be a claim the page could not back.
    ///
    /// A RunOnce entry is deliberately not among them: plenty of programs
    /// leave one behind permanently, and calling that "a restart is owed"
    /// would be a wrong answer rather than a cautious one.
    /// </summary>
    public static readonly (string Key, string Reason)[] RebootMarkers =
    {
        (@"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
         "servicing (DISM or a component update) is waiting for a restart"),
        (@"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
         "Windows Update installed something that needs a restart"),
        (@"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\PostRebootReporting",
         "an update is reporting back after a restart"),
    };

    public async Task<WindowsUpdateStatus> StatusAsync(CancellationToken cancel = default)
    {
        var raw = await RunAsync(new { op = "status" }, cancel);
        var read = raw is null ? null : JsonSerializer.Deserialize<ReadResult>(raw, Raw);
        if (read is null)
            return new WindowsUpdateStatus { Error = "The Windows Update state could not be read." };

        var reasons = read.RebootReasons ?? new();
        if (read.PendingFileRenames) reasons.Add("files are queued to be renamed or deleted at the next boot");

        return new WindowsUpdateStatus
        {
            State = read.State ?? "Unknown",
            UpdateServiceRunning = read.ServiceRunning,
            LastSearchSuccess = ParseDate(read.LastSearch),
            LastInstallSuccess = ParseDate(read.LastInstall),
            LastBoot = ParseDate(read.LastBoot),
            PendingRebootReasons = reasons,
            History = (read.History ?? new()).Select(h => new UpdateHistoryEntry
            {
                Title = h.Title ?? string.Empty,
                Operation = h.Operation ?? string.Empty,
                Result = h.Result ?? string.Empty,
                When = ParseDate(h.When),
                HResult = h.HResult,
            }).ToList(),
            Error = read.Error,
        };
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    /// <summary>
    /// Ask Windows Update what is waiting. Deliberately not part of the status
    /// read: it is a network round trip to Microsoft that takes tens of
    /// seconds, and a page that fires one on load would be making a request
    /// nobody asked for.
    /// </summary>
    public async Task<WindowsUpdateStatus> ScanAsync(CancellationToken cancel = default)
    {
        var current = await StatusAsync(cancel);
        var raw = await RunAsync(new { op = "scan" }, cancel);
        var read = raw is null ? null : JsonSerializer.Deserialize<ReadResult>(raw, Raw);

        if (read is null)
            return current with { Scanned = true, ScanNote = "Windows Update did not answer the search." };

        return current with
        {
            Available = (read.Available ?? new()).Select(a => new PendingUpdate
            {
                Title = a.Title ?? string.Empty,
                Id = a.Id ?? string.Empty,
                Severity = a.Severity ?? string.Empty,
                SizeBytes = a.SizeBytes,
                IsDownloaded = a.IsDownloaded,
                Description = a.Description,
            }).ToList(),
            Scanned = true,
            ScanNote = read.Error is null
                ? null
                : $"the search reported: {read.Error}",
        };
    }

    /// <summary>Open the Windows Update page. Writes nothing.</summary>
    public static WindowsUpdateChange Open() => new()
    {
        Action = "open",
        Success = true,
        Affected = 1,
        Message = "Windows Update was opened in Settings.",
        Preview = new List<string> { "explorer.exe ms-settings:windowsupdate" },
    };

    /// <summary>What a restart to finish updates would do, without doing it.</summary>
    public static WindowsUpdateChange RestartPreview() => new()
    {
        Action = "restart",
        Success = true,
        Unchanged = true,
        Affected = 1,
        Message = "A restart would finish whatever Windows Update has already installed. " +
                  "Anything unsaved is lost, and nothing is downloaded by restarting.",
        Preview = new List<string> { "shutdown.exe /r /t 60" },
        RestartRequired = true,
    };

    /// <summary>
    /// Restart with a minute's grace. A restart is the one action here that
    /// cannot be waited out, so the delay is the point: it is time to close
    /// things rather than a countdown the user has to beat.
    /// </summary>
    public async Task<WindowsUpdateChange> RestartAsync(bool confirm, CancellationToken cancel = default)
    {
        var preview = RestartPreview();
        if (!confirm)
            return preview with { Success = false, Message = preview.Message + " Nothing has been run." };

        var result = await ProcessRunner.RunAsync(
            "shutdown.exe", new[] { "/r", "/t", "60" }, TimeSpan.FromSeconds(30));

        return new WindowsUpdateChange
        {
            Action = "restart",
            Success = result.Success,
            Affected = result.Success ? 1 : 0,
            Message = result.Success
                ? "This machine restarts in 60 seconds. Run `shutdown /a` to cancel."
                : $"The restart could not be scheduled: {result.Output.Trim()}",
            Preview = preview.Preview,
            RestartRequired = result.Success,
            Log = result.Output.Trim(),
        };
    }

    private static WindowsUpdateChange Fail(string action, string message) => new()
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
        var directory = Path.Combine(Path.GetTempPath(), $"novimize-update-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(directory, "payload.json");
        var scriptPath = Path.Combine(directory, "update.ps1");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(payloadPath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                cancel);
            await File.WriteAllTextAsync(scriptPath, Script, cancel);

            // A search talks to Microsoft and can take a minute; a status read
            // must not be held hostage to one.
            var timeout = payload.ToString()!.Contains("scan")
                ? TimeSpan.FromMinutes(3)
                : TimeSpan.FromSeconds(60);

            var result = await ProcessRunner.RunAsync("powershell.exe", new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", scriptPath, "-Payload", payloadPath,
            }, timeout);

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

        function Reply([object]$body) { $body | ConvertTo-Json -Depth 10 -Compress }

        function Iso([object]$value) {
            if ($null -eq $value) { return $null }
            try {
                $d = [datetime]$value
                if ($d -le [datetime]::MinValue.AddYears(1)) { return $null }
                return $d.ToUniversalTime().ToString('o')
            } catch { return $null }
        }

        try {
            switch ($p.op) {

                'status' {
                    $serviceRunning = $false
                    try { $serviceRunning = ((Get-Service -Name wuauserv -ErrorAction Stop).Status -eq 'Running') } catch { }

                    $autoUpdate = $null
                    try { $autoUpdate = (New-Object -ComObject Microsoft.Update.AutoUpdate) } catch { }

                    $lastSearch = $null
                    $lastInstall = $null
                    if ($autoUpdate) {
                        try { $lastSearch = Iso $autoUpdate.Results.LastSearchSuccessDate } catch { }
                        try { $lastInstall = Iso $autoUpdate.Results.LastInstallationSuccessDate } catch { }
                    }

                    $lastBoot = $null
                    try { $lastBoot = Iso (Get-CimInstance Win32_OperatingSystem).LastBootUpTime } catch { }

                    $reasons = @()
                    $markers = @(
                        @{ path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'; why = 'servicing (DISM or a component update) is waiting for a restart' }
                        @{ path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'; why = 'Windows Update installed something that needs a restart' }
                        @{ path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\PostRebootReporting'; why = 'an update is reporting back after a restart' }
                    )
                    foreach ($m in $markers) {
                        if (Test-Path -LiteralPath $m.path) { $reasons += [string]$m.why }
                    }

                    $renames = $false
                    try {
                        $sm = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -ErrorAction Stop
                        if ($sm.PendingFileRenameOperations) { $renames = $true }
                    } catch { }

                    $history = @()
                    try {
                        $session = New-Object -ComObject Microsoft.Update.Session
                        $searcher = $session.CreateUpdateSearcher()
                        $count = [Math]::Min(25, $searcher.GetTotalHistoryCount())
                        if ($count -gt 0) {
                            $rows = $searcher.QueryHistory(0, $count)
                            foreach ($h in $rows) {
                                $resultText = ''
                                try { $resultText = [string]$h.ResultCode } catch { }
                                $history += [pscustomobject]@{
                                    title   = [string]$h.Title
                                    operation = [string]$h.Operation
                                    result  = $resultText
                                    when    = Iso $h.Date
                                    hresult = [int]$h.HResult
                                }
                            }
                        }
                    } catch { }

                    $state = 'Unknown'
                    if ($reasons.Count -gt 0 -or $renames) { $state = 'Pending reboot' }
                    elseif (-not $serviceRunning) { $state = 'Service stopped' }
                    elseif ($history.Count -gt 0) { $state = 'Installed' }

                    Reply @{
                        state = $state
                        serviceRunning = $serviceRunning
                        lastSearch = $lastSearch
                        lastInstall = $lastInstall
                        lastBoot = $lastBoot
                        rebootReasons = $reasons
                        pendingFileRenames = $renames
                        history = $history
                        error = $null
                    }
                    exit
                }

                'scan' {
                    $available = @()
                    $err = $null
                    try {
                        $session = New-Object -ComObject Microsoft.Update.Session
                        $searcher = $session.CreateUpdateSearcher()
                        $found = $searcher.Search("IsInstalled=0 and IsHidden=0")
                        foreach ($u in $found.Updates) {
                            $available += [pscustomobject]@{
                                title       = [string]$u.Title
                                id          = [string]$u.Identity.UpdateID
                                severity    = [string]$u.MsrcSeverity
                                sizeBytes   = $(if ($u.MaxDownloadSize) { [long]$u.MaxDownloadSize } else { $null })
                                isDownloaded = [bool]$u.IsDownloaded
                                description = $(if ($u.Description) { [string]$u.Description } else { $null })
                            }
                        }
                    } catch {
                        $err = [string]$_.Exception.Message
                    }

                    Reply @{ available = $available; error = $err }
                    exit
                }

                default { Reply @{ error = 'Unknown operation.' } }
            }
        } catch {
            Reply @{ error = [string]$_.Exception.Message }
        }
        """;

    private sealed class ReadResult
    {
        public string? State { get; init; }
        public bool ServiceRunning { get; init; }
        public string? LastSearch { get; init; }
        public string? LastInstall { get; init; }
        public string? LastBoot { get; init; }
        public List<string>? RebootReasons { get; init; }
        public bool PendingFileRenames { get; init; }
        public List<RawHistory>? History { get; init; }
        public List<RawAvailable>? Available { get; init; }
        public string? Error { get; init; }
    }

    private sealed class RawHistory
    {
        public string? Title { get; init; }
        public string? Operation { get; init; }
        public string? Result { get; init; }
        public string? When { get; init; }
        public int HResult { get; init; }
    }

    private sealed class RawAvailable
    {
        public string? Title { get; init; }
        public string? Id { get; init; }
        public string? Severity { get; init; }
        public long? SizeBytes { get; init; }
        public bool IsDownloaded { get; init; }
        public string? Description { get; init; }
    }
}
